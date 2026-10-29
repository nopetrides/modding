using System;
using System.IO;
using System.Linq;
using BepInEx;
using Hand_Drawn_Map;
using UnityEngine;

internal static class MapSessionTests
{
    private static void Check(bool value, string message = "Check failed")
    { if (!value) throw new Exception(message); }

    private static void Deliver(ZRpc source, ZRpc target, Client recipient)
    {
        var messages = source.Sent.ToArray(); source.Sent.Clear(); recipient.Activate();
        foreach (var message in messages) target.Deliver(message.Name, (ZPackage)message.Args[0]);
    }

    private sealed class Client : IDisposable
    {
        public readonly MapPlugin Plugin = new MapPlugin();
        public readonly ZNet Net = new ZNet();
        public readonly Player Player = new Player();
        public readonly ChartDocument View = new ChartDocument();
        public readonly MapSession Session;
        public Client(bool personal = false, bool host = false)
        {
            Plugin.PersonalMapOnly.Value = personal;
            Net.Server = host;
            if (!host) { Net.ServerPeer = new ZNetPeer(); Net.Peers.Add(Net.ServerPeer); }
            Session = new MapSession(Plugin);
            if (!host) Session.Register(Net.ServerPeer);
            Session.Bind(View, () => { });
        }
        public void Activate() { ZNet.instance = Net; global::Player.m_localPlayer = Player; }
        public void Tick(float time)
        {
            Activate(); Time.frameCount++; Time.unscaledTime = time; Session.Tick();
        }
        public void Draw()
        {
            Activate(); Guid gesture = Guid.NewGuid();
            Check(Session.Draw(gesture, new[] { new ChartPoint(-10, 0), new ChartPoint(10, 0) }, 1, false));
            Session.End(gesture);
        }
        public void Dispose() { Activate(); Session.Dispose(); }
    }

    internal static void Run(Action<string, Action> test)
    {
        string previous = Paths.ConfigPath;
        string root = Path.Combine(Path.GetTempPath(), "hand-drawn-session-" + Guid.NewGuid());
        Paths.ConfigPath = root;
        Directory.CreateDirectory(root);
        try
        {
            test("Personal-only starts immediately without sending any map messages", () =>
            {
                using var client = new Client(personal: true);
                client.Tick(0);
                Check(client.Session.CanEdit && client.Session.Status == "Personal map");
                client.Draw(); client.Session.Flush(); client.Session.RequestResync(); client.Tick(100);
                Check(client.View.Strokes.Count == 1);
                Check(client.Net.ServerPeer.m_rpc.Sent.Count == 0, "Personal mode contacted the host");
                Check(client.Plugin.Errors.Count == 0);
            });
            test("Personal ink survives reconnect and resync without becoming shared ink", () =>
            {
                using var client = new Client(personal: true);
                client.Tick(0);
                Check(client.View.Strokes.Count == 1, "Personal drawing was not restored");
                client.Session.RequestResync();
                Check(client.View.Strokes.Count == 1);
                Check(!Directory.Exists(Path.Combine(root, MapPlugin.Guid, "shared")));
            });
            test("Missing host falls back after ten seconds without disconnecting gameplay", () =>
            {
                using var client = new Client(); client.Player.Id = 101;
                client.Tick(0); Check(!client.Session.CanEdit);
                client.Tick(9); Check(!client.Session.CanEdit);
                client.Tick(11); Check(client.Session.CanEdit && client.Session.Status == "Personal map");
                int sent = client.Net.ServerPeer.m_rpc.Sent.Count;
                client.Draw(); client.Session.Flush(); client.Session.RequestResync(); client.Tick(50);
                Check(client.Net.ServerPeer.m_rpc.Sent.Count == sent, "Fallback continued sending map messages");
                Check(client.View.Strokes.Count == 1 && client.Net.Peers.Count == 1);
            });
            test("Personal drawings are isolated by character and world", () =>
            {
                using var client = new Client(personal: true); client.Player.Id = 102;
                client.Tick(0); Check(client.View.Strokes.Count == 0);
                client.Draw(); client.Session.Flush();
                client.Net.World = 201; client.Tick(1); Check(client.View.Strokes.Count == 0);
                client.Net.World = 200; client.Tick(2); Check(client.View.Strokes.Count == 1);
                client.Player.Id = 103; client.Tick(3); Check(client.View.Strokes.Count == 0);
            });
            test("Shared hosts permit peers that never request a shared map", () =>
            {
                using var host = new Client(host: true);
                var peer = new ZNetPeer(); host.Net.Peers.Add(peer); host.Session.Register(peer);
                host.Tick(0); host.Tick(100);
                Check(host.Session.CanEdit && peer.m_rpc.Sent.Count == 0 && host.Net.Peers.Contains(peer));
                Check(host.Session.Status == "Shared map");
            });
            test("Personal hosting neither reads nor changes the shared map", () =>
            {
                using var host = new Client(personal: true, host: true); host.Player.Id = 104;
                host.Tick(0); Check(host.Session.Status == "Personal map" && host.View.Strokes.Count == 0);
                host.Draw(); host.Session.Flush();
                host.Plugin.PersonalMapOnly.Value = false; host.Tick(1);
                Check(host.Session.Status == "Shared map" && host.View.Strokes.Count == 0);
                host.Plugin.PersonalMapOnly.Value = true; host.Tick(2);
                Check(host.Session.Status == "Personal map" && host.View.Strokes.Count == 1);
            });
            test("Late host replies cannot replace a personal map", () =>
            {
                using var client = new Client(personal: true); client.Player.Id = 105; client.Tick(0);
                client.Draw();
                client.Net.ServerPeer.m_rpc.Deliver("NPR_SharedMap_V1", new ZPackage(new byte[] { 0 }));
                Check(client.Session.CanEdit && client.Session.Status == "Pending edits: 2" && client.View.Strokes.Count == 1);
            });
            test("Fallback imports legacy personal ink without modifying it", () =>
            {
                using var client = new Client(); client.Player.Id = 106;
                string file = Path.Combine(root, MapPlugin.Guid, "200_106.chart");
                var old = new ChartDocument(); var stroke = new ChartStroke(1);
                stroke.Points.Add(new ChartPoint(0, 0)); stroke.Points.Add(new ChartPoint(20, 0)); old.Strokes.Add(stroke);
                ChartStore.Save(file, old); byte[] before = File.ReadAllBytes(file);
                client.Tick(0); client.Tick(11);
                Check(client.View.Strokes.Count == 1 && before.SequenceEqual(File.ReadAllBytes(file)));
            });
            test("Matching shared peers still handshake, draw instantly and receive committed ink", () =>
            {
                using var host = new Client(host: true); host.Net.World = 300;
                using var client = new Client(); client.Net.World = 300;
                var peer = new ZNetPeer(); host.Net.Peers.Add(peer); host.Session.Register(peer);
                host.Tick(0); client.Tick(0);
                Deliver(client.Net.ServerPeer.m_rpc, peer.m_rpc, host);
                host.Tick(1); Deliver(peer.m_rpc, client.Net.ServerPeer.m_rpc, client);
                Check(client.Session.CanEdit && client.Session.Status == "Shared map");
                client.Draw(); Check(client.View.Strokes.Count == 1 && host.View.Strokes.Count == 0);
                client.Tick(2); Deliver(client.Net.ServerPeer.m_rpc, peer.m_rpc, host);
                host.Session.Flush(); host.Tick(3);
                Deliver(peer.m_rpc, client.Net.ServerPeer.m_rpc, client);
                client.Tick(30);
                Check(host.View.Strokes.Count == 1 && client.View.Strokes.Count == 1);
                Check(client.Session.Status == "Shared map", "Shared client fell back despite a valid handshake");
                Check(host.Plugin.Errors.Count == 0 && client.Plugin.Errors.Count == 0);
            });
            test("A delayed valid handshake cannot merge into an established fallback", () =>
            {
                using var host = new Client(host: true); host.Net.World = 301;
                using var client = new Client(); client.Net.World = 301;
                var peer = new ZNetPeer(); host.Net.Peers.Add(peer); host.Session.Register(peer);
                host.Tick(0); client.Tick(0);
                Deliver(client.Net.ServerPeer.m_rpc, peer.m_rpc, host);
                client.Tick(11); Check(client.Session.Status == "Personal map");
                client.Draw(); client.Session.Flush(); client.Session.RequestResync();
                host.Tick(12); Deliver(peer.m_rpc, client.Net.ServerPeer.m_rpc, client);
                Check(client.Session.Status == "Personal map" && client.View.Strokes.Count == 1);
                Check(host.View.Strokes.Count == 0, "Personal ink leaked to the host");
            });
            test("Dedicated owners remain shared without a local player", () =>
            {
                using var host = new Client(host: true); host.Net.World = 302;
                host.Net.Dedicated = true; host.Plugin.Headless = true;
                using var client = new Client(); client.Net.World = 302;
                var peer = new ZNetPeer(); host.Net.Peers.Add(peer); host.Session.Register(peer);
                host.Activate(); Player.m_localPlayer = null; Time.frameCount++; Time.unscaledTime = 0; host.Session.Tick();
                client.Tick(0); Deliver(client.Net.ServerPeer.m_rpc, peer.m_rpc, host);
                host.Activate(); Player.m_localPlayer = null; Time.frameCount++; Time.unscaledTime = 1; host.Session.Tick();
                Deliver(peer.m_rpc, client.Net.ServerPeer.m_rpc, client);
                Check(client.Session.CanEdit && client.Session.Status == "Shared map");
                Check(host.Plugin.Errors.Count == 0);
            });
            test("Personal erasing and undo persist without involving the host", () =>
            {
                using var client = new Client(personal: true); client.Player.Id = 107;
                client.Tick(0); client.Draw();
                var erase = Guid.NewGuid();
                Check(client.Session.Erase(erase, new ChartPoint(0, 0), new ChartPoint(0, 0), 1));
                client.Session.End(erase);
                Check(client.View.Strokes.Count == 2);
                Check(client.Session.Undo() && client.View.Strokes.Count == 1);
                client.Session.Flush(); client.Session.RequestResync();
                Check(client.View.Strokes.Count == 1 && client.Net.ServerPeer.m_rpc.Sent.Count == 0);
            });
        }
        finally
        {
            ZNet.instance = null; Player.m_localPlayer = null; Paths.ConfigPath = previous;
            Directory.Delete(root, true);
        }
    }
}
