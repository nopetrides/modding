using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hand_Drawn_Map;

internal static class SharedMapTests
{
    private static void Check(bool condition, string message = "Check failed") { if (!condition) throw new Exception(message); }
    private static ChartPoint P(float x, float z = 0) => new ChartPoint(x, z);
    private static MapEdit Draw(float from = -10, float to = 10) => new MapEdit
    { Gesture = Guid.NewGuid(), Kind = MapEditKind.Draw, Width = 0.1f, Points = new[] { P(from), P(to) } };
    private static MapEdit Apply(SharedMap owner, MapEdit request, long actor = 11, double now = 1000)
    {
        Check(owner.Prepare(MapWire.DecodeEdit(MapWire.EncodeEdit(request)), actor, now, 120, out MapEdit accepted, out string error), error);
        owner.Apply(accepted);
        return accepted;
    }
    private static ChartDocument View(SharedMap map, double now = 1000)
    {
        var doc = new ChartDocument(); map.Fill(doc, new List<TemporaryInk>(), now); return doc;
    }
    private static bool Visible(SharedMap map, float x)
    {
        foreach (ChartStroke stroke in View(map).Strokes)
            for (int i = 1; i < stroke.Points.Count; i++)
                if (ChartGeometry.DistanceSquared(P(x), stroke.Points[i - 1], stroke.Points[i]) < 0.0001f) return true;
        return false;
    }
    private static MapEdit Erase(Guid target, float from, float to, float radius = 1)
        => new MapEdit { Gesture = Guid.NewGuid(), Kind = MapEditKind.Erase, Width = radius, Points = new[] { P(from), P(to) }, Targets = new[] { target } };
    private static void Same(SharedMap a, SharedMap b)
    { Check(a.Revision == b.Revision && a.Digest.SequenceEqual(b.Digest), "Replicas diverged"); }

    internal static void Run(Action<string, Action> test)
    {
        test("Shared drawing is visible before any owner round trip", () =>
        {
            var replica = new MapReplica { Actor = 11, Ready = true };
            Check(replica.Propose(Draw(), 1000));
            Check(View(replica.Predicted).Strokes.Count == 1 && replica.Confirmed.Revision == 0 && replica.Pending.Count == 1);
        });
        test("Concurrent drawings rebase without duplicate ink", () =>
        {
            var owner = new SharedMap();
            var a = new MapReplica { Actor = 11, Ready = true }; var b = new MapReplica { Actor = 22, Ready = true };
            MapEdit da = Draw(-10, 0), db = Draw(0, 10);
            Check(a.Propose(da, 1000) && b.Propose(db, 1000));
            MapEdit cb = Apply(owner, db, 22); a.Accept(cb, 1000); b.Accept(cb, 1000);
            Check(View(a.Predicted).Strokes.Count == 2 && a.Pending.Count == 1);
            MapEdit ca = Apply(owner, da); b.Accept(ca, 1000); a.Accept(ca, 1000); a.Accept(ca, 1000);
            Same(a.Confirmed, owner); Same(b.Confirmed, owner);
            Check(a.Pending.Count == 0 && View(a.Predicted).Strokes.Count == 2);
        });
        test("Local erasing is visible before owner acknowledgement", () =>
        {
            var owner = new SharedMap(); MapEdit drawing = Apply(owner, Draw());
            var replica = new MapReplica { Actor = 22 }; replica.Snapshot(owner.Fork(), 1000);
            Check(replica.Propose(Erase(drawing.Id, 0, 0), 1000));
            Check(!Visible(replica.Predicted, 0) && Visible(owner, 0));
        });
        test("Overlapping erasures combine and undo removes only its author's erasure", () =>
        {
            var owner = new SharedMap(); MapEdit drawing = Apply(owner, Draw());
            Apply(owner, Erase(drawing.Id, -2, 0), 11);
            Apply(owner, Erase(drawing.Id, 0, 2), 22);
            Check(!Visible(owner, -2) && !Visible(owner, 2));
            Apply(owner, new MapEdit { Kind = MapEditKind.Undo }, 11);
            Check(Visible(owner, -2) && !Visible(owner, 0) && !Visible(owner, 2), "Undo restored another editor's erasure");
            Apply(owner, new MapEdit { Kind = MapEditKind.Undo }, 22);
            Check(Visible(owner, 0) && Visible(owner, 2));
        });
        test("An eraser never touches newer unobserved drawing", () =>
        {
            var owner = new SharedMap(); MapEdit first = Apply(owner, Draw());
            MapEdit stale = Erase(first.Id, -20, 20);
            Apply(owner, Draw(), 22);
            Apply(owner, stale);
            Check(View(owner).Strokes.Count == 1 && Visible(owner, 0));
        });
        test("Undoing drawing cannot erase another editor's drawing", () =>
        {
            var owner = new SharedMap(); Apply(owner, Draw(), 11); Apply(owner, Draw(), 22);
            Apply(owner, new MapEdit { Kind = MapEditKind.Undo }, 11);
            Check(View(owner).Strokes.Count == 1 && Visible(owner, 0));
        });
        test("Duplicate commands cannot create a second stroke", () =>
        {
            var owner = new SharedMap(); MapEdit request = Draw(); MapEdit accepted = Apply(owner, request);
            owner.Apply(accepted);
            Check(!owner.Prepare(request, 11, 1000, 120, out _, out _) && owner.Revision == 1 && View(owner).Strokes.Count == 1);
        });
        test("Late chunks are rejected after end or undo", () =>
        {
            var owner = new SharedMap(); MapEdit first = Apply(owner, Draw());
            Apply(owner, new MapEdit { Kind = MapEditKind.End, Gesture = first.Gesture });
            MapEdit late = Draw(); late.Gesture = first.Gesture;
            Check(!owner.Prepare(late, 11, 1000, 120, out _, out _));
            Apply(owner, new MapEdit { Kind = MapEditKind.Undo });
            Check(!owner.Prepare(late, 11, 1000, 120, out _, out _) && View(owner).Strokes.Count == 0);
        });
        test("Connection identity overrides a claimed author", () =>
        {
            var owner = new SharedMap(); MapEdit forged = Draw(); forged.Actor = 999;
            MapEdit accepted = Apply(owner, forged, 22);
            Check(accepted.Actor == 22);
            var undo = new MapEdit { Kind = MapEditKind.Undo, UndoGesture = forged.Gesture };
            Check(!owner.Prepare(undo, 999, 1000, 120, out _, out _));
            var hijack = Draw(); hijack.Gesture = forged.Gesture;
            Check(!owner.Prepare(hijack, 999, 1000, 120, out _, out _));
        });
        test("Unknown targets and invalid geometry leave owner state unchanged", () =>
        {
            var owner = new SharedMap(); byte[] before = owner.Digest;
            Check(!owner.Prepare(Erase(Guid.NewGuid(), 0, 0), 11, 1000, 120, out _, out _));
            MapEdit invalid = Draw(); invalid.Points[0] = P(float.NaN);
            Check(!owner.Prepare(invalid, 11, 1000, 120, out _, out _));
            invalid = Draw(); invalid.Width = float.PositiveInfinity;
            Check(!owner.Prepare(invalid, 11, 1000, 120, out _, out _));
            Check(owner.Revision == 0 && before.SequenceEqual(owner.Digest));
        });
        test("Snapshot at S plus concurrent deltas reaches the owner map", () =>
        {
            var owner = new SharedMap(); Apply(owner, Draw());
            byte[] snapshot = MapWire.Snapshot(owner);
            MapEdit second = Apply(owner, Draw(20, 30), 22);
            MapEdit third = Apply(owner, Erase(owner.Events[0].Id, 0, 0), 22);
            var replica = new MapReplica { Actor = 33 };
            replica.Snapshot(MapWire.ReadSnapshot(snapshot), 1000);
            replica.Accept(second, 1000); replica.Accept(third, 1000);
            Same(owner, replica.Confirmed);
        });
        test("Resync resolves a lost acknowledgement without duplicating pending ink", () =>
        {
            var owner = new SharedMap(); var replica = new MapReplica { Actor = 11, Ready = true };
            MapEdit request = Draw(); replica.Propose(request, 1000); Apply(owner, request);
            Check(replica.Pending.Count == 1);
            replica.Snapshot(MapWire.ReadSnapshot(MapWire.Snapshot(owner)), 1000);
            Check(replica.Pending.Count == 0 && View(replica.Predicted).Strokes.Count == 1);
            Same(replica.Confirmed, owner);
        });
        test("Resync preserves an unsent valid drawing as prediction", () =>
        {
            var owner = new SharedMap(); var replica = new MapReplica { Actor = 11, Ready = true };
            replica.Propose(Draw(), 1000);
            Apply(owner, Draw(20, 30), 22);
            replica.Snapshot(MapWire.ReadSnapshot(MapWire.Snapshot(owner)), 1000);
            Check(replica.Pending.Count == 1 && View(replica.Predicted).Strokes.Count == 2);
        });
        test("A missing canonical revision is detected before accepting later state", () =>
        {
            var owner = new SharedMap(); Apply(owner, Draw()); MapEdit second = Apply(owner, Draw(), 22);
            var replica = new MapReplica { Actor = 11, Ready = true };
            bool caught = false;
            try { replica.Accept(second, 1000); } catch (InvalidOperationException) { caught = true; }
            Check(caught && replica.Confirmed.Revision == 0);
        });
        test("Forked predictions do not modify confirmed groups or erase caches", () =>
        {
            var owner = new SharedMap(); MapEdit drawing = Apply(owner, Draw());
            byte[] before = MapWire.Snapshot(owner);
            SharedMap fork = owner.Fork(); Apply(fork, Erase(drawing.Id, -3, 3), 22);
            Apply(fork, new MapEdit { Kind = MapEditKind.Undo }, 22);
            Check(before.SequenceEqual(MapWire.Snapshot(owner)) && Visible(owner, 0));
        });
        test("Temporary annotations share, expire, and are excluded from undo", () =>
        {
            var owner = new SharedMap(); Apply(owner, Draw());
            MapEdit temporary = Draw(20, 30); temporary.Kind = MapEditKind.Annotate; Apply(owner, temporary, 22);
            var annotations = new List<TemporaryInk>(); var doc = new ChartDocument();
            owner.Fill(doc, annotations, 1000); Check(annotations.Count == 1 && doc.Strokes.Count == 1);
            Check(owner.LastUndo(22) == Guid.Empty);
            owner.Fill(doc, annotations, 1121); Check(annotations.Count == 0 && doc.Strokes.Count == 1);
        });
        test("Clearing annotations preserves permanent ink and rejects stale annotation chunks", () =>
        {
            var owner = new SharedMap(); Apply(owner, Draw());
            MapEdit temporary = Draw(20, 30); temporary.Kind = MapEditKind.Annotate; Apply(owner, temporary, 22);
            Apply(owner, new MapEdit { Kind = MapEditKind.ClearAnnotations }, 33);
            var list = new List<TemporaryInk>(); owner.Fill(new ChartDocument(), list, 1000); Check(list.Count == 0);
            MapEdit late = temporary.Copy(); late.Id = Guid.NewGuid();
            Check(!owner.Prepare(late, 22, 1000, 120, out _, out _) && View(owner).Strokes.Count == 1);
        });
        test("Out-of-order commands are rejected and rate limits recover", () =>
        {
            var guard = new MapEditorGuard();
            Check(!guard.Accept(2, 1000, out _)); Check(guard.Accept(1, 1000, out _));
            for (int i = 2; i <= 256; i++) Check(guard.Accept(i, 1000, out _));
            Check(!guard.Accept(257, 1000, out _));
            Check(guard.Accept(258, 1001, out _));
            Check(!guard.Accept(258, 1001, out _));
        });
        test("Erasing blank space does not add a claim over another editor's erasure", () =>
        {
            var owner = new SharedMap(); MapEdit drawing = Apply(owner, Draw());
            Apply(owner, Erase(drawing.Id, -3, 3), 22);
            Check(owner.Targets(P(0), P(0), 0.5f, 1000).Length == 0);
        });
        test("Rejected predictions retain recoverable geometry", () =>
        {
            var replica = new MapReplica { Actor = 11, Ready = true };
            MapEdit request = Draw(); replica.Propose(request, 1000);
            replica.Reject(request.Id, "Rejected by owner", 1000);
            Check(replica.Pending.Count == 0 && View(replica.Predicted).Strokes.Count == 0 && replica.Recovery.Count == 1);
            Check(replica.Recovery[0].Points.Length == 2);
        });
        test("Shared undo retains a bounded window of fifty own gestures", () =>
        {
            var owner = new SharedMap();
            for (int i = 0; i < 60; i++) Apply(owner, Draw(i, i + 1));
            for (int i = 0; i < 50; i++) Apply(owner, new MapEdit { Kind = MapEditKind.Undo });
            Check(owner.LastUndo(11) == Guid.Empty && View(owner).Strokes.Count == 10);
        });
        test("Canonical fingerprints survive binary snapshot replay", () =>
        {
            var owner = new SharedMap(); MapEdit drawing = Apply(owner, Draw());
            Apply(owner, Erase(drawing.Id, -1, 1), 22);
            SharedMap restored = MapWire.ReadSnapshot(MapWire.Snapshot(owner));
            Same(owner, restored);
            restored.Digest[0] ^= 255;
            Same(owner, restored);
        });
        test("Malformed wire lengths and trailing bytes are rejected", () =>
        {
            byte[] bytes = MapWire.EncodeEdit(Draw());
            BitConverter.GetBytes(int.MaxValue).CopyTo(bytes, 89);
            bool rejected = false;
            try { MapWire.DecodeEdit(bytes); } catch (InvalidDataException) { rejected = true; }
            Check(rejected);
            bytes = MapWire.EncodeEdit(Draw()).Concat(new byte[] { 1 }).ToArray();
            rejected = false;
            try { MapWire.DecodeEdit(bytes); } catch (InvalidDataException) { rejected = true; }
            Check(rejected);
        });
        test("Two delayed clients converge through concurrent drawing, erasing and undo", () =>
        {
            var owner = new SharedMap();
            var clients = new[] { new MapReplica { Actor = 11, Ready = true }, new MapReplica { Actor = 22, Ready = true } };
            var transit = new List<(int Due, int Sender, MapEdit Edit)>();
            var replies = new List<(int Due, MapEdit Edit)>();
            var random = new Random(2026);
            for (int tick = 0; tick < 160; tick++)
            {
                if (tick < 80 && tick % 4 == 0)
                    for (int c = 0; c < clients.Length; c++)
                    {
                        MapEdit request;
                        if (tick > 12 && tick % 12 == 0 && clients[c].Predicted.LastUndo(clients[c].Actor) != Guid.Empty)
                            request = new MapEdit { Kind = MapEditKind.Undo };
                        else if (tick > 8 && tick % 8 == 0 && clients[c].Predicted.Events.Any(e => e.Kind == MapEditKind.Draw))
                            request = Erase(clients[c].Predicted.Events.First(e => e.Kind == MapEditKind.Draw).Id, -3, 3);
                        else request = Draw(-10 + c, 10 + c);
                        if (clients[c].Propose(request, 1000 + tick)) transit.Add((tick + 3 + random.Next(3), c, request));
                    }
                foreach (var packet in transit.Where(p => p.Due <= tick).ToArray())
                {
                    transit.Remove(packet);
                    if (owner.Prepare(MapWire.DecodeEdit(MapWire.EncodeEdit(packet.Edit)), clients[packet.Sender].Actor, 1000 + tick, 120, out MapEdit edit, out string error))
                    { owner.Apply(edit); replies.Add((tick + 4, edit)); }
                    else clients[packet.Sender].Reject(packet.Edit.Id, error, 1000 + tick);
                }
                foreach (var packet in replies.Where(p => p.Due <= tick).OrderBy(p => p.Edit.Revision).ToArray())
                {
                    replies.Remove(packet);
                    foreach (MapReplica client in clients) client.Accept(MapWire.DecodeEdit(MapWire.EncodeEdit(packet.Edit)), 1000 + tick);
                }
            }
            Check(transit.Count == 0 && replies.Count == 0);
            foreach (MapReplica client in clients) { Same(client.Confirmed, owner); Check(client.Pending.Count == 0); }
        });

        string folder = Path.Combine(Path.GetTempPath(), "SharedMapTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            test("Owner journal persists accepted ink, undo and world identity", () =>
            {
                string path = Path.Combine(folder, "world.journal"); var owner = new SharedMap();
                using (var journal = new MapJournal(path, 123))
                {
                    MapEdit drawing = Apply(owner, Draw()); journal.Append(owner.ForDisk(drawing));
                    MapEdit erase = Apply(owner, Erase(drawing.Id, 0, 0), 22); journal.Append(owner.ForDisk(erase));
                    MapEdit undo = Apply(owner, new MapEdit { Kind = MapEditKind.Undo }, 22); journal.Append(owner.ForDisk(undo));
                }
                using (var journal = new MapJournal(path, 123)) { Same(owner, journal.Loaded); Check(journal.Durable == 3 && Visible(journal.Loaded, 0)); }
                bool failed = false;
                try { using var wrong = new MapJournal(path, 999); } catch (InvalidDataException) { failed = true; }
                Check(failed);
            });
            test("Owner restart drops temporary annotations without losing permanent erasures", () =>
            {
                string path = Path.Combine(folder, "temporary.journal"); var owner = new SharedMap();
                using (var journal = new MapJournal(path, 123))
                {
                    MapEdit drawing = Apply(owner, Draw()); journal.Append(owner.ForDisk(drawing));
                    MapEdit annotation = Draw(); annotation.Kind = MapEditKind.Annotate;
                    MapEdit temp = Apply(owner, annotation, 22); journal.Append(owner.ForDisk(temp));
                    MapEdit erase = Erase(drawing.Id, 0, 0); erase.Targets = new[] { drawing.Id, temp.Id };
                    MapEdit erased = Apply(owner, erase, 33); journal.Append(owner.ForDisk(erased));
                    MapEdit end = Apply(owner, new MapEdit { Kind = MapEditKind.End, Gesture = temp.Gesture }, 22); journal.Append(owner.ForDisk(end));
                }
                using (var journal = new MapJournal(path, 123))
                {
                    var annotations = new List<TemporaryInk>(); journal.Loaded.Fill(new ChartDocument(), annotations, 1000);
                    Check(annotations.Count == 0 && !Visible(journal.Loaded, 0));
                }
            });
            test("Truncated owner journal loads only valid prefix and remains read-only", () =>
            {
                string path = Path.Combine(folder, "damaged.journal"); var owner = new SharedMap();
                using (var journal = new MapJournal(path, 123))
                { journal.Append(Apply(owner, Draw())); journal.Append(Apply(owner, Draw(20, 30))); }
                byte[] bytes = File.ReadAllBytes(path); File.WriteAllBytes(path, bytes.Take(bytes.Length - 3).ToArray());
                byte[] original = File.ReadAllBytes(path);
                using (var journal = new MapJournal(path, 123))
                {
                    Check(journal.Corrupt && journal.Loaded.Revision == 1);
                    bool failed = false;
                    try { journal.Append(Draw()); } catch (IOException) { failed = true; }
                    Check(failed && original.SequenceEqual(File.ReadAllBytes(path)));
                }
            });
        }
        finally
        {
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
    }
}
