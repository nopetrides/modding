using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Hand_Drawn_Map
{
    public sealed class MapJournal : IDisposable
    {
        private const int Magic = 0x48444D32;
        private readonly string path;
        private readonly ConcurrentQueue<Record> queue = new ConcurrentQueue<Record>();
        private readonly object gate = new object();
        private Task worker = Task.CompletedTask;
        private long durable;
        private Exception failure;
        private long bytes;
        public readonly Guid Generation;
        public readonly SharedMap Loaded = new SharedMap();
        public bool Corrupt { get; private set; }
        public long Durable { get { lock (gate) return durable; } }
        public Exception Failure { get { lock (gate) return failure; } }
        public bool HasRoom(int count) => bytes + count + 36 < MapWire.MaxSnapshot - 2 * 1024 * 1024;

        private sealed class Record
        {
            public long Revision;
            public byte[] Bytes;
        }

        public MapJournal(string path, long world)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > MapWire.MaxSnapshot) throw new InvalidDataException("Shared map journal too large");
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    if (reader.ReadInt32() != Magic || reader.ReadInt32() != MapWire.Protocol || reader.ReadInt64() != world)
                        throw new InvalidDataException("Invalid shared map journal header");
                    Generation = MapWire.ReadGuid(reader);
                    while (reader.BaseStream.Position < reader.BaseStream.Length)
                    {
                        try
                        {
                            int length = MapWire.Count(reader, MapWire.MaxPacket);
                            if (length == 0) throw new InvalidDataException();
                            byte[] expected = reader.ReadBytes(32), data = reader.ReadBytes(length);
                            if (expected.Length != 32 || data.Length != length) throw new EndOfStreamException();
                            using (var hash = SHA256.Create())
                                if (!hash.ComputeHash(data).SequenceEqual(expected)) throw new InvalidDataException("Journal checksum mismatch");
                            Loaded.Apply(MapWire.DecodeEdit(data));
                        }
                        catch (Exception exception) when (exception is IOException || exception is InvalidOperationException)
                        { Corrupt = true; break; }
                    }
                }
                bytes = new FileInfo(path).Length;
                if (!Corrupt) File.Copy(path, path + ".bak", true);
            }
            else
            {
                Generation = Guid.NewGuid();
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(stream))
                { writer.Write(Magic); writer.Write(MapWire.Protocol); writer.Write(world); writer.Write(Generation.ToByteArray()); writer.Flush(); stream.Flush(true); }
                bytes = new FileInfo(path).Length;
            }
            durable = Loaded.Revision;
        }

        public void Append(MapEdit edit)
        {
            byte[] data = MapWire.EncodeEdit(edit);
            lock (gate)
            {
                if (Corrupt || failure != null) throw new IOException("Shared map storage is read-only", failure);
                if (!HasRoom(data.Length)) throw new IOException("Shared map storage limit reached");
                bytes += data.Length + 36;
                queue.Enqueue(new Record { Revision = edit.Revision, Bytes = data });
                worker = worker.ContinueWith(_ => Drain(), TaskScheduler.Default);
            }
        }

        private void Drain()
        {
            lock (gate) if (failure != null) return;
            if (queue.IsEmpty) return;
            try
            {
                long last = 0;
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var writer = new BinaryWriter(stream))
                using (var hash = SHA256.Create())
                {
                    while (queue.TryDequeue(out Record record))
                    { writer.Write(record.Bytes.Length); writer.Write(hash.ComputeHash(record.Bytes)); writer.Write(record.Bytes); last = record.Revision; }
                    writer.Flush(); stream.Flush(true);
                }
                lock (gate) durable = Math.Max(durable, last);
            }
            catch (Exception exception) { lock (gate) failure = exception; }
        }

        public void Flush()
        {
            Task task;
            lock (gate) task = worker;
            task.GetAwaiter().GetResult();
        }
        public void Dispose() => Flush();
    }
}
