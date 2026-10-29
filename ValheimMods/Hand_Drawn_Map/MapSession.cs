using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BepInEx;
using UnityEngine;

namespace Hand_Drawn_Map
{
    internal sealed class MapSession : IDisposable
    {
        private const string RpcName = "NPR_SharedMap_V1";
        private enum Message : byte { Hello, Welcome, Edit, Commit, Reject, Start, Part, Finish, Resync, Ping, Pong }
        private readonly MapPlugin plugin;
        private readonly Dictionary<ZRpc, Peer> peers = new Dictionary<ZRpc, Peer>();
        private readonly List<MapEdit> outgoing = new List<MapEdit>();
        private readonly Dictionary<Guid, MapEdit> inflight = new Dictionary<Guid, MapEdit>();
        private readonly List<MapEdit> received = new List<MapEdit>();
        private readonly List<Transfer> transfers = new List<Transfer>();
        private readonly List<MapEdit> waitingCommits = new List<MapEdit>();
        private MapReplica replica = new MapReplica();
        private SharedMap owner;
        private SharedMap published;
        private MapJournal journal;
        private ChartDocument view;
        private Action changed;
        private long ownerWorld, clientWorld, sequence;
        private MapEditorGuard localGuard = new MapEditorGuard();
        private Guid clientSession;
        private bool ownerFailed;
        private string ownerError;
        private float helloAt, nextSend, nextPing, nextFade, syncStarted;
        private int lastTick = -1;
        private float clientStarted;
        private double clockOffset;
        private Download download;
        private bool syncing;
        private bool personal, personalOnly;
        private long character;
        internal readonly List<TemporaryInk> Annotations = new List<TemporaryInk>();
        internal bool CanEdit => replica.Ready && !syncing && !ownerFailed && replica.Pending.Count < 256 && inflight.Count < 256;
        internal string Status => ownerError ?? (ownerFailed ? "Map is read-only" : null) ?? replica.Error ?? (syncing ? "Downloading shared map..." : !replica.Ready ? "Connecting to map owner..." : replica.Pending.Count > 0 ? "Pending edits: " + replica.Pending.Count : personal ? "Personal map" : "Shared map");
        internal double Now => UtcNow + clockOffset;
        private static double UtcNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        private sealed class Peer
        {
            public ZNetPeer Network;
            public Guid Session;
            public long Actor;
            public bool Welcomed;
            public readonly MapEditorGuard Guard = new MapEditorGuard();
            public float LastSnapshot = -100;
            public readonly Dictionary<Guid, string> Rejected = new Dictionary<Guid, string>();
        }
        private sealed class Transfer
        {
            public Peer Peer;
            public Guid Id = Guid.NewGuid();
            public byte[] Bytes, Hash;
            public int Offset;
            public long Revision;
            public bool Started;
        }
        private sealed class Download
        {
            public Guid Id;
            public byte[] Bytes, Hash;
            public int Offset;
            public long Revision;
        }

        internal MapSession(MapPlugin plugin) { this.plugin = plugin; }

        internal void Register(ZNetPeer network)
        {
            if (peers.ContainsKey(network.m_rpc)) return;
            var peer = new Peer { Network = network };
            peers.Add(network.m_rpc, peer);
            network.m_rpc.Register<ZPackage>(RpcName, (rpc, package) => Receive(peer, package));
        }

        internal void Bind(ChartDocument document, Action onChanged)
        { view = document; changed = onChanged; Refresh(); }

        internal void Unbind()
        {
            view = null; changed = null;
            Annotations.Clear();
        }

        internal void Tick()
        {
            if (lastTick == Time.frameCount) return;
            lastTick = Time.frameCount;
            ZNet net = ZNet.instance;
            if (net == null)
            {
                StopOwner(); ResetClient(); peers.Clear(); return;
            }
            if (!net.IsServer() && net.GetServerPeer() == null)
            {
                if (replica.Ready) { SaveRecovery(); replica.Ready = false; Refresh(); }
                return;
            }
            if (net.GetWorld() == null) return;
            long world = net.GetWorldUID();
            if (world == 0) return;
            long player = Player.m_localPlayer == null ? 0 : Player.m_localPlayer.GetPlayerID();
            if (clientWorld != 0 && (clientWorld != world || character != player || personalOnly != plugin.PersonalMapOnly.Value))
            {
                plugin.Window?.FinishSharedGesture(); Flush(); StopOwner(); ResetClient();
            }
            if (owner != null && ownerWorld != world) StopOwner();
            bool hasPlayer = !net.IsDedicated() && !plugin.Headless && player != 0;
            if (plugin.PersonalMapOnly.Value)
            {
                if (hasPlayer && !personal) StartPersonal(world);
                if (!hasPlayer) { StopOwner(); return; }
            }
            if (!personal && !plugin.PersonalMapOnly.Value && net.IsServer() && owner == null && (net.IsDedicated() || plugin.Headless || hasPlayer)) StartOwner(world);
            if (hasPlayer && (clientWorld != world || owner != null && !replica.Ready)) StartClient(world);
            foreach (Peer peer in peers.Values.ToArray())
            {
                if (!net.GetPeers().Contains(peer.Network)) { peers.Remove(peer.Network.m_rpc); transfers.RemoveAll(t => t.Peer == peer); continue; }
            }
            if (journal != null)
            {
                if (journal.Failure != null && !ownerFailed)
                {
                    ownerFailed = true; ownerError = "Shared map storage failed; editing paused";
                    plugin.LogToolError(journal.Failure);
                    foreach (MapEdit edit in waitingCommits.ToArray()) RejectAll(edit.Id, ownerError);
                    waitingCommits.Clear();
                }
                if (!ownerFailed)
                    while (waitingCommits.Count > 0 && waitingCommits[0].Revision <= journal.Durable)
                    {
                        MapEdit edit = waitingCommits[0]; waitingCommits.RemoveAt(0);
                        published.Apply(edit);
                        if (clientWorld == ownerWorld && replica.Ready) Accept(edit);
                        foreach (Peer peer in peers.Values.Where(p => !personal && p.Welcomed))
                            Send(peer, Message.Commit, writer => MapWire.WriteEdit(writer, edit));
                    }
            }
            if (owner != null && !personal) PumpTransfers();
            if (!personal && !net.IsServer() && clientWorld == world)
            {
                ZNetPeer server = net.GetServerPeer();
                if (server != null && server.m_uid != 0 && !replica.Ready && !syncing && Time.unscaledTime - helloAt > 2)
                {
                    helloAt = Time.unscaledTime;
                    if (peers.TryGetValue(server.m_rpc, out Peer peer)) Send(peer, Message.Hello, writer => { writer.Write(MapPlugin.Version); writer.Write(world); });
                }
                if (server == null && replica.Ready) { SaveRecovery(); replica.Ready = false; }
                if (server != null && clientSession == Guid.Empty && Time.unscaledTime - clientStarted > 10)
                {
                    StartPersonal(world);
                    plugin.LogMapInfo("No shared map response; using a personal map for this session.");
                }
                if (!personal && Time.unscaledTime >= nextSend && server != null && clientSession != Guid.Empty && !syncing)
                {
                    nextSend = Time.unscaledTime + 0.05f;
                    if (peers.TryGetValue(server.m_rpc, out Peer peer))
                        foreach (MapEdit edit in outgoing.ToArray())
                        { Send(peer, Message.Edit, writer => { writer.Write(clientSession.ToByteArray()); MapWire.WriteEdit(writer, edit); }); outgoing.Remove(edit); }
                }
                if (!personal && replica.Ready && Time.unscaledTime > nextPing && server != null && peers.TryGetValue(server.m_rpc, out Peer pingPeer))
                { nextPing = Time.unscaledTime + 3; Send(pingPeer, Message.Ping, writer => writer.Write(replica.Confirmed.Revision)); }
            }
            if (syncing && Time.unscaledTime - syncStarted > 30)
            { syncing = false; download = null; replica.Error = "Map sync timed out; press Resync"; replica.Ready = false; }
            if (replica.Ready && Annotations.Count > 0 && Time.unscaledTime > nextFade)
            { nextFade = Time.unscaledTime + 0.05f; Refresh(); }
        }

        private void StartOwner(long world)
        {
            ownerWorld = world;
            string path = personal
                ? Path.Combine(Paths.ConfigPath, MapPlugin.Guid, "personal", world + "_" + Player.m_localPlayer.GetPlayerID() + ".journal")
                : Path.Combine(Paths.ConfigPath, MapPlugin.Guid, "shared", world + ".journal");
            try
            {
                bool fresh = !File.Exists(path);
                journal = new MapJournal(path, world);
                owner = journal.Loaded;
                ownerFailed = journal.Corrupt;
                if (ownerFailed) ownerError = "Map save is damaged; recovered drawings are read-only";
                if (fresh && !ZNet.instance.IsDedicated() && !plugin.Headless) ImportPersonalMap(world);
                published = owner.Fork();
            }
            catch (Exception exception)
            {
                owner = new SharedMap(); ownerFailed = true;
                published = new SharedMap();
                ownerError = "Map could not be loaded; editing paused";
                plugin.LogToolError(exception);
            }
        }

        private void ImportPersonalMap(long world)
        {
            Player player = Player.m_localPlayer;
            if (player == null) return;
            string path = Path.Combine(Paths.ConfigPath, MapPlugin.Guid, world + "_" + player.GetPlayerID() + ".chart");
            if (!File.Exists(path)) return;
            ChartDocument old;
            try { old = ChartStore.Load(path); }
            catch (Exception exception) { plugin.LogToolError(exception); return; }
            foreach (ChartStroke stroke in old.Strokes)
            {
                Guid gesture = Guid.NewGuid();
                for (int i = 0; i < stroke.Points.Count; i += SharedMap.MaxChunkPoints - 1)
                {
                    var edit = new MapEdit { Kind = MapEditKind.Draw, Gesture = gesture, Width = stroke.Width,
                        Points = stroke.Points.Skip(i).Take(SharedMap.MaxChunkPoints).ToArray() };
                    if (!owner.Prepare(edit, 0, UtcNow, 120, out MapEdit accepted, out string error)) throw new InvalidDataException(error);
                    owner.Apply(accepted); journal.Append(owner.ForDisk(accepted));
                }
                var end = new MapEdit { Kind = MapEditKind.End, Gesture = gesture };
                if (owner.Prepare(end, 0, UtcNow, 120, out MapEdit closed, out _)) { owner.Apply(closed); journal.Append(closed); }
            }
            journal.Flush();
            plugin.LogMapInfo("Imported existing personal ink into the new " + (personal ? "personal" : "shared") + " map; original file retained.");
        }

        private void StartPersonal(long world)
        {
            plugin.Window?.FinishSharedGesture();
            Flush(); StopOwner(); ResetClient();
            personal = true;
            StartOwner(world);
            StartClient(world);
        }

        private void StartClient(long world)
        {
            bool local = personal;
            ResetClient(); clientWorld = world; personal = local;
            personalOnly = plugin.PersonalMapOnly.Value;
            character = Player.m_localPlayer == null ? 0 : Player.m_localPlayer.GetPlayerID();
            localGuard = new MapEditorGuard();
            clientStarted = Time.unscaledTime;
            if ((personal || ZNet.instance.IsServer()) && owner != null)
            {
                clientSession = Guid.NewGuid();
                replica.Actor = NewActor();
                replica.AnnotationLifetime = plugin.AnnotationLifetime.Value;
                replica.Snapshot(owner.Fork(), UtcNow);
                Refresh();
            }
            else helloAt = -100;
        }

        internal bool Draw(Guid gesture, ChartPoint[] points, float width, bool temporary)
            => Submit(new MapEdit { Gesture = gesture, Kind = temporary ? MapEditKind.Annotate : MapEditKind.Draw, Points = points, Width = width });

        internal bool Erase(Guid gesture, ChartPoint from, ChartPoint to, float radius)
        {
            if (!CanEdit) return false;
            Guid[] targets = replica.Predicted.Targets(from, to, radius, Now);
            bool result = false;
            for (int i = 0; i < targets.Length; i += SharedMap.MaxTargets)
                result |= Submit(new MapEdit { Gesture = gesture, Kind = MapEditKind.Erase, Points = new[] { from, to }, Width = radius,
                    Targets = targets.Skip(i).Take(SharedMap.MaxTargets).ToArray() });
            return result;
        }
        internal void End(Guid gesture) => Submit(new MapEdit { Kind = MapEditKind.End, Gesture = gesture });
        internal bool Undo() => Submit(new MapEdit { Kind = MapEditKind.Undo, UndoGesture = replica.Predicted.LastUndo(replica.Actor) });
        internal void ClearAnnotations() => Submit(new MapEdit { Kind = MapEditKind.ClearAnnotations });

        private bool Submit(MapEdit edit)
        {
            if (!CanEdit) return false;
            edit.Sequence = sequence + 1;
            if (!replica.Propose(edit, Now)) { Refresh(); return false; }
            sequence++;
            Refresh();
            if (personal || ZNet.instance.IsServer()) ProcessEdit(null, edit);
            else { outgoing.Add(edit); inflight.Add(edit.Id, edit); }
            return true;
        }

        private void ProcessEdit(Peer peer, MapEdit edit)
        {
            long actor = peer == null ? replica.Actor : peer.Actor;
            if (owner == null || ownerFailed) { Reject(peer, edit.Id, ownerError ?? "Map owner not ready"); return; }
            if (owner.Find(edit.Id, out MapEdit previous))
            {
                if (previous.Actor != actor) { Reject(peer, edit.Id, "Operation belongs to another editor"); return; }
                if (!(peer == null ? localGuard : peer.Guard).Replay(UtcNow, out _)) return;
                if (previous.Revision <= journal.Durable)
                { if (peer == null) Accept(previous); else Send(peer, Message.Commit, writer => MapWire.WriteEdit(writer, previous)); }
                return;
            }
            if (peer != null)
            {
                if (peer.Rejected.TryGetValue(edit.Id, out string previousError)) { Reject(peer, edit.Id, previousError); return; }
            }
            MapEditorGuard guard = peer == null ? localGuard : peer.Guard;
            if (!guard.Accept(edit.Sequence, UtcNow, out string orderError)) { Reject(peer, edit.Id, orderError); return; }
            if (!owner.Prepare(edit, actor, UtcNow, plugin.AnnotationLifetime.Value, out MapEdit accepted, out string error))
            { Reject(peer, edit.Id, error); return; }
            MapEdit disk = accepted.Copy();
            if (accepted.Kind == MapEditKind.Annotate || accepted.Kind == MapEditKind.ClearAnnotations)
            { disk.Kind = MapEditKind.Empty; disk.Points = Array.Empty<ChartPoint>(); disk.Targets = Array.Empty<Guid>(); }
            if (!journal.HasRoom(MapWire.EncodeEdit(disk).Length)) { Reject(peer, edit.Id, "Shared map storage limit reached"); return; }
            try
            {
                owner.Apply(accepted);
                journal.Append(owner.ForDisk(accepted));
                waitingCommits.Add(accepted);
            }
            catch (Exception exception)
            { ownerFailed = true; ownerError = "Shared map storage failed; editing paused"; Reject(peer, edit.Id, ownerError); plugin.LogToolError(exception); }
        }

        private void Reject(Peer peer, Guid id, string reason)
        {
            if (peer == null) { replica.Reject(id, reason, Now); Refresh(); }
            else
            {
                if (peer.Rejected.Count < 2048) peer.Rejected[id] = reason;
                Send(peer, Message.Reject, writer => { writer.Write(id.ToByteArray()); writer.Write(reason); });
            }
        }
        private void RejectAll(Guid id, string reason)
        { Reject(null, id, reason); foreach (Peer peer in peers.Values.Where(p => !personal && p.Welcomed)) Reject(peer, id, reason); }

        private void Accept(MapEdit edit)
        {
            inflight.Remove(edit.Id);
            if (syncing) { if (received.Count < 4096) received.Add(edit); else replica.Error = "Sync backlog exceeded"; return; }
            try { replica.Accept(edit, Now); Refresh(); }
            catch (InvalidOperationException) { RequestResync(); }
        }

        internal void RequestResync()
        {
            if (clientWorld == 0) return;
            plugin.Window?.FinishSharedGesture();
            if (personal || ZNet.instance != null && ZNet.instance.IsServer())
            {
                journal?.Flush();
                if (owner != null && !ownerFailed) replica.Snapshot(owner.Fork(), Now);
                Refresh(); return;
            }
            if (syncing && Time.unscaledTime - syncStarted < 5) return;
            SaveRecovery(); syncing = true; syncStarted = Time.unscaledTime; download = null; received.Clear();
            ZRpc rpc = ZNet.instance?.GetServerRPC();
            if (rpc != null && peers.TryGetValue(rpc, out Peer peer))
                Send(peer, Message.Resync, writer => writer.Write(clientSession.ToByteArray()));
            Refresh();
        }

        private void Receive(Peer peer, ZPackage package)
        {
            if (personal || plugin.PersonalMapOnly.Value) return;
            try
            {
                if (package.Size() > MapWire.MaxPacket) throw new InvalidDataException("Map packet too large");
                if (ZNet.instance == null || peer.Network.m_uid == 0 || !ZNet.instance.GetPeers().Contains(peer.Network)) return;
                using (var stream = new MemoryStream(package.GetArray(), false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadInt32() != MapWire.Protocol) throw new InvalidDataException("Incompatible map protocol");
                    Message kind = (Message)reader.ReadByte();
                    if (ZNet.instance.IsServer()) ReceiveOwner(peer, kind, reader);
                    else if (ZNet.instance.GetServerPeer() == peer.Network) ReceiveClient(peer, kind, reader);
                    MapWire.End(reader);
                }
            }
            catch (Exception exception)
            {
                plugin.LogToolWarning("Map packet rejected: " + exception.Message);
                if (ZNet.instance != null && !ZNet.instance.IsServer())
                { replica.Error = "Invalid map response; resync required"; syncing = false; replica.Ready = false; Refresh(); }
            }
        }

        private void ReceiveOwner(Peer peer, Message kind, BinaryReader reader)
        {
            if (kind == Message.Hello)
            {
                string version = reader.ReadString(); long world = reader.ReadInt64();
                MapWire.End(reader);
                if (owner == null) return;
                if (version != MapPlugin.Version || world != ownerWorld) return;
                if (!peer.Welcomed) { peer.Session = Guid.NewGuid(); peer.Actor = NewActor(); peer.Welcomed = true; }
                Send(peer, Message.Welcome, writer =>
                { writer.Write(peer.Session.ToByteArray()); writer.Write(ownerWorld); writer.Write(peer.Actor); writer.Write(UtcNow); writer.Write(ownerFailed); writer.Write(plugin.AnnotationLifetime.Value); });
                QueueSnapshot(peer);
            }
            else if (kind == Message.Edit)
            {
                Guid session = MapWire.ReadGuid(reader); MapEdit edit = MapWire.ReadEdit(reader);
                MapWire.End(reader);
                if (!peer.Welcomed || session != peer.Session) return;
                ProcessEdit(peer, edit);
            }
            else if (kind == Message.Resync)
            {
                Guid session = MapWire.ReadGuid(reader);
                MapWire.End(reader);
                if (peer.Welcomed && peer.Session == session) QueueSnapshot(peer);
            }
            else if (kind == Message.Ping)
            {
                long revision = reader.ReadInt64();
                MapWire.End(reader);
                if (peer.Welcomed) Send(peer, Message.Pong, writer => { writer.Write(published.Revision); writer.Write(published.Digest); writer.Write(UtcNow); writer.Write(ownerFailed); });
            }
            else throw new InvalidDataException("Invalid owner request");
        }

        private void ReceiveClient(Peer peer, Message kind, BinaryReader reader)
        {
            switch (kind)
            {
                case Message.Welcome:
                    Guid session = MapWire.ReadGuid(reader); long world = reader.ReadInt64(), actor = reader.ReadInt64();
                    double serverTime = reader.ReadDouble(); bool failed = reader.ReadBoolean(); float lifetime = reader.ReadSingle();
                    if (float.IsNaN(lifetime) || lifetime < 5 || lifetime > 1800) throw new InvalidDataException("Invalid annotation lifetime");
                    MapWire.End(reader);
                    clockOffset = serverTime - UtcNow;
                    if (world != clientWorld) throw new InvalidDataException("Wrong map world");
                    if (clientSession != Guid.Empty && clientSession != session) { SaveRecovery(); replica = new MapReplica(); sequence = 0; outgoing.Clear(); inflight.Clear(); }
                    clientSession = session; replica.Actor = actor; ownerFailed = failed;
                    replica.AnnotationLifetime = lifetime;
                    syncing = true; syncStarted = Time.unscaledTime; break;
                case Message.Commit:
                    MapEdit committed = MapWire.ReadEdit(reader); MapWire.End(reader); Accept(committed); break;
                case Message.Reject:
                    Guid id = MapWire.ReadGuid(reader); string reason = reader.ReadString();
                    MapWire.End(reader);
                    replica.Reject(id, reason, Now); outgoing.RemoveAll(e => e.Id == id); inflight.Remove(id); Refresh(); break;
                case Message.Start:
                    Guid transfer = MapWire.ReadGuid(reader); int size = MapWire.Count(reader, MapWire.MaxSnapshot);
                    long revision = reader.ReadInt64(); byte[] hash = reader.ReadBytes(32);
                    MapWire.End(reader);
                    if (hash.Length != 32 || size < 8) throw new InvalidDataException("Invalid snapshot header");
                    download = new Download { Id = transfer, Bytes = new byte[size], Hash = hash, Revision = revision };
                    syncing = true; syncStarted = Time.unscaledTime; break;
                case Message.Part:
                    Guid part = MapWire.ReadGuid(reader); int offset = reader.ReadInt32(), length = MapWire.Count(reader, MapWire.PartSize);
                    byte[] data = reader.ReadBytes(length);
                    MapWire.End(reader);
                    if (download == null || download.Id != part || offset != download.Offset || data.Length != length || offset + length > download.Bytes.Length)
                        throw new InvalidDataException("Invalid snapshot part");
                    Buffer.BlockCopy(data, 0, download.Bytes, offset, length); download.Offset += length; syncStarted = Time.unscaledTime; break;
                case Message.Finish:
                    Guid finish = MapWire.ReadGuid(reader);
                    MapWire.End(reader);
                    if (download == null || finish != download.Id || download.Offset != download.Bytes.Length) throw new InvalidDataException("Incomplete snapshot");
                    using (var sha = SHA256.Create()) if (!sha.ComputeHash(download.Bytes).SequenceEqual(download.Hash)) throw new InvalidDataException("Snapshot checksum mismatch");
                    SharedMap state = MapWire.ReadSnapshot(download.Bytes);
                    if (state.Revision != download.Revision) throw new InvalidDataException("Snapshot revision mismatch");
                    replica.Snapshot(state, Now);
                    syncing = false; download = null;
                    foreach (MapEdit edit in received.OrderBy(e => e.Revision).ToArray()) if (edit.Revision > state.Revision) replica.Accept(edit, Now);
                    received.Clear();
                    foreach (Guid key in inflight.Keys.ToArray()) if (replica.Confirmed.Contains(key)) inflight.Remove(key);
                    outgoing.Clear(); outgoing.AddRange(inflight.Values.OrderBy(e => e.Sequence));
                    Refresh(); break;
                case Message.Pong:
                    long serverRevision = reader.ReadInt64(); byte[] fingerprint = reader.ReadBytes(32);
                    double pingTime = reader.ReadDouble(); bool pingFailed = reader.ReadBoolean();
                    MapWire.End(reader);
                    clockOffset = pingTime - UtcNow; ownerFailed = pingFailed;
                    if (!syncing && replica.Ready && (serverRevision != replica.Confirmed.Revision || !fingerprint.SequenceEqual(replica.Confirmed.Digest))) RequestResync(); break;
                default: throw new InvalidDataException("Invalid client response");
            }
        }

        private void QueueSnapshot(Peer peer)
        {
            if (Time.unscaledTime - peer.LastSnapshot < 2 || transfers.Any(t => t.Peer == peer)) return;
            peer.LastSnapshot = Time.unscaledTime;
            SharedMap source = ownerFailed ? published : owner;
            byte[] bytes = MapWire.Snapshot(source);
            using (var sha = SHA256.Create()) transfers.Add(new Transfer { Peer = peer, Bytes = bytes, Hash = sha.ComputeHash(bytes), Revision = source.Revision });
        }

        private void PumpTransfers()
        {
            foreach (Transfer transfer in transfers.ToArray())
            {
                if ((journal?.Durable ?? 0) < transfer.Revision) continue;
                if (transfer.Peer.Network.m_rpc.GetSocket().GetSendQueueSize() > 512 * 1024) continue;
                if (!transfer.Started)
                {
                    Send(transfer.Peer, Message.Start, writer => { writer.Write(transfer.Id.ToByteArray()); writer.Write(transfer.Bytes.Length); writer.Write(transfer.Revision); writer.Write(transfer.Hash); });
                    transfer.Started = true;
                }
                for (int i = 0; i < 2 && transfer.Offset < transfer.Bytes.Length; i++)
                {
                    int offset = transfer.Offset, size = Math.Min(MapWire.PartSize, transfer.Bytes.Length - offset);
                    Send(transfer.Peer, Message.Part, writer => { writer.Write(transfer.Id.ToByteArray()); writer.Write(offset); writer.Write(size); writer.Write(transfer.Bytes, offset, size); });
                    transfer.Offset += size;
                }
                if (transfer.Offset == transfer.Bytes.Length)
                { Send(transfer.Peer, Message.Finish, writer => writer.Write(transfer.Id.ToByteArray())); transfers.Remove(transfer); }
            }
        }

        private static void Send(Peer peer, Message kind, Action<BinaryWriter> body)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(MapWire.Protocol); writer.Write((byte)kind); body(writer);
                if (stream.Length > MapWire.MaxPacket) throw new InvalidDataException("Outgoing map packet too large");
                peer.Network.m_rpc.Invoke(RpcName, new ZPackage(stream.ToArray()));
            }
        }

        private long NewActor()
        {
            long actor;
            do { actor = BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0); }
            while (actor == 0 || actor == replica.Actor || owner.Events.Any(edit => edit.Actor == actor) || peers.Values.Any(peer => peer.Actor == actor));
            return actor;
        }

        private void Refresh()
        {
            if (view != null) replica.Predicted.Fill(view, Annotations, Now);
            if (replica.Recovery.Count > 0 && WriteRecovery(replica.Recovery)) replica.Recovery.Clear();
            changed?.Invoke();
        }

        private void SaveRecovery()
        {
            if (replica.Pending.Count == 0) return;
            WriteRecovery(replica.Pending);
        }

        internal void PreserveDraft(Guid gesture, ChartPoint[] points, float width)
            => WriteRecovery(new[] { new MapEdit { Kind = MapEditKind.Draw, Gesture = gesture, Actor = replica.Actor, Points = points, Width = width } });

        private bool WriteRecovery(IEnumerable<MapEdit> edits)
        {
            try
            {
                MapEdit[] records = edits.ToArray();
                string path = Path.Combine(Paths.ConfigPath, MapPlugin.Guid, "recovery"); Directory.CreateDirectory(path);
                using (var writer = new BinaryWriter(File.Create(Path.Combine(path, clientWorld + "_" + Guid.NewGuid().ToString("N") + ".pending"))))
                { writer.Write(MapWire.Protocol); writer.Write(records.Length); foreach (MapEdit edit in records) MapWire.WriteEdit(writer, edit); }
                return true;
            }
            catch (Exception exception) { plugin.LogToolError(exception); }
            return false;
        }

        private void ResetClient()
        {
            if (clientWorld == 0) return;
            SaveRecovery();
            replica = new MapReplica(); clientWorld = 0; clientSession = Guid.Empty; sequence = 0;
            personal = false; personalOnly = false; character = 0;
            outgoing.Clear(); inflight.Clear(); received.Clear(); syncing = false; download = null; clockOffset = 0;
        }
        private void StopOwner()
        {
            journal?.Dispose(); journal = null; owner = null; published = null; ownerWorld = 0;
            ownerFailed = false; ownerError = null; waitingCommits.Clear(); transfers.Clear();
        }
        internal void Flush()
        {
            if (!personal && ZNet.instance != null && !ZNet.instance.IsServer())
            {
                ZRpc rpc = ZNet.instance.GetServerRPC();
                if (rpc != null && peers.TryGetValue(rpc, out Peer peer))
                    foreach (MapEdit edit in outgoing.ToArray())
                    { Send(peer, Message.Edit, writer => { writer.Write(clientSession.ToByteArray()); MapWire.WriteEdit(writer, edit); }); outgoing.Remove(edit); }
            }
            journal?.Flush();
            if (journal != null && journal.Failure == null && replica.Ready && !syncing)
                foreach (MapEdit edit in waitingCommits.Where(e => e.Revision <= journal.Durable).ToArray())
                    if (edit.Revision > replica.Confirmed.Revision) replica.Accept(edit, Now);
        }

        public void Dispose() { Flush(); ResetClient(); StopOwner(); }
    }
}
