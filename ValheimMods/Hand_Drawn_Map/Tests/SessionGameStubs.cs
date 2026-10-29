using System;
using System.Collections.Generic;

namespace UnityEngine
{
    internal static class Time
    {
        public static int frameCount;
        public static float unscaledTime;
    }
}

namespace BepInEx
{
    internal static class Paths { public static string ConfigPath; }
}

namespace Hand_Drawn_Map
{
    internal sealed class TestSetting<T>
    {
        public T Value;
        public TestSetting(T value) { Value = value; }
    }
    internal sealed class MapPlugin
    {
        public const string Guid = "com.nopetrides.valheim.hand-drawn-map";
        public const string Version = "0.4.1";
        public bool Headless = false;
        public readonly TestSetting<bool> PersonalMapOnly = new TestSetting<bool>(false);
        public readonly TestSetting<float> AnnotationLifetime = new TestSetting<float>(120);
        public TestWindow Window = new TestWindow();
        public readonly List<Exception> Errors = new List<Exception>();
        public void LogToolError(Exception error) => Errors.Add(error);
        public void LogToolWarning(string message) { }
        public void LogMapInfo(string message) { }
    }
    internal sealed class TestWindow { public void FinishSharedGesture() { } }
}

internal sealed class Player
{
    public static Player m_localPlayer;
    public long Id = 100;
    public long GetPlayerID() => Id;
}

internal sealed class ZNet
{
    public static ZNet instance;
    public bool Server, Dedicated = false;
    public long World = 200;
    public ZNetPeer ServerPeer;
    public readonly List<ZNetPeer> Peers = new List<ZNetPeer>();
    public bool IsServer() => Server;
    public bool IsDedicated() => Dedicated;
    public ZNetPeer GetServerPeer() => ServerPeer;
    public ZRpc GetServerRPC() => ServerPeer?.m_rpc;
    public object GetWorld() => World == 0 ? null : this;
    public long GetWorldUID() => World;
    public List<ZNetPeer> GetPeers() => Peers;
}

internal sealed class ZNetPeer
{
    public long m_uid = 1;
    public ZRpc m_rpc = new ZRpc();
}

internal sealed class ZRpc
{
    public readonly List<(string Name, object[] Args)> Sent = new List<(string, object[])>();
    private readonly Dictionary<string, Action<ZRpc, ZPackage>> handlers = new Dictionary<string, Action<ZRpc, ZPackage>>();
    public void Register<T>(string name, Action<ZRpc, T> handler)
        => handlers[name] = (rpc, package) => handler(rpc, (T)(object)package);
    public void Invoke(string name, params object[] args) => Sent.Add((name, args));
    public void Deliver(string name, ZPackage package) => handlers[name](this, package);
    public TestSocket GetSocket() => new TestSocket();
}

internal sealed class TestSocket { public int GetSendQueueSize() => 0; }

internal sealed class ZPackage
{
    private readonly byte[] bytes;
    public ZPackage(byte[] bytes) { this.bytes = bytes; }
    public int Size() => bytes.Length;
    public byte[] GetArray() => bytes;
}
