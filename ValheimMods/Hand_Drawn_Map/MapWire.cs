using System;
using System.IO;
using System.Text;

namespace Hand_Drawn_Map
{
    public static class MapWire
    {
        public const int Protocol = 1;
        public const int MaxPacket = 32768;
        public const int MaxSnapshot = 64 * 1024 * 1024;
        public const int PartSize = 24000;

        public static byte[] EncodeEdit(MapEdit edit)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            { WriteEdit(writer, edit); return stream.ToArray(); }
        }

        public static void WriteEdit(BinaryWriter writer, MapEdit edit)
        {
            writer.Write(edit.Id.ToByteArray()); writer.Write(edit.Gesture.ToByteArray()); writer.Write(edit.UndoGesture.ToByteArray());
            writer.Write(edit.Actor); writer.Write(edit.Sequence); writer.Write(edit.Revision); writer.Write((byte)edit.Kind);
            writer.Write(edit.Width); writer.Write(edit.Time); writer.Write(edit.Lifetime);
            writer.Write(edit.Points.Length);
            foreach (ChartPoint point in edit.Points) { writer.Write(point.X); writer.Write(point.Z); }
            writer.Write(edit.Targets.Length);
            foreach (Guid target in edit.Targets) writer.Write(target.ToByteArray());
        }

        public static MapEdit ReadEdit(BinaryReader reader)
        {
            var edit = new MapEdit
            {
                Id = ReadGuid(reader), Gesture = ReadGuid(reader), UndoGesture = ReadGuid(reader),
                Actor = reader.ReadInt64(), Sequence = reader.ReadInt64(), Revision = reader.ReadInt64(),
                Kind = (MapEditKind)reader.ReadByte(), Width = reader.ReadSingle(), Time = reader.ReadDouble(), Lifetime = reader.ReadSingle()
            };
            if ((byte)edit.Kind > (byte)MapEditKind.ClearAnnotations || !Finite(edit.Width) ||
                double.IsNaN(edit.Time) || double.IsInfinity(edit.Time) || !Finite(edit.Lifetime)) throw new InvalidDataException("Invalid map values");
            edit.Points = new ChartPoint[Count(reader, SharedMap.MaxChunkPoints)];
            for (int i = 0; i < edit.Points.Length; i++)
            {
                float x = reader.ReadSingle(), z = reader.ReadSingle();
                if (!Finite(x) || !Finite(z) || Math.Abs(x) > 100000 || Math.Abs(z) > 100000) throw new InvalidDataException("Invalid map coordinate");
                edit.Points[i] = new ChartPoint(x, z);
            }
            edit.Targets = new Guid[Count(reader, SharedMap.MaxTargets)];
            for (int i = 0; i < edit.Targets.Length; i++) edit.Targets[i] = ReadGuid(reader);
            if (edit.Id == Guid.Empty || (edit.Kind == MapEditKind.Draw || edit.Kind == MapEditKind.Annotate) && edit.Points.Length == 0
                || edit.Kind == MapEditKind.Erase && edit.Points.Length != 2) throw new InvalidDataException("Invalid edit shape");
            return edit;
        }

        public static MapEdit DecodeEdit(byte[] bytes)
        {
            if (bytes.Length > MaxPacket) throw new InvalidDataException("Map edit too large");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                MapEdit edit = ReadEdit(reader);
                End(reader); return edit;
            }
        }

        public static byte[] Snapshot(SharedMap map)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Protocol); writer.Write(map.Events.Count);
                foreach (MapEdit edit in map.Events) WriteEdit(writer, edit);
                if (stream.Length > MaxSnapshot) throw new InvalidDataException("Map snapshot limit exceeded");
                return stream.ToArray();
            }
        }

        public static SharedMap ReadSnapshot(byte[] bytes)
        {
            if (bytes.Length > MaxSnapshot) throw new InvalidDataException("Snapshot too large");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                if (reader.ReadInt32() != Protocol) throw new InvalidDataException("Incompatible map protocol");
                int count = Count(reader, SharedMap.MaxEvents);
                var result = new SharedMap();
                for (int i = 0; i < count; i++) result.Apply(ReadEdit(reader));
                End(reader); return result;
            }
        }

        public static Guid ReadGuid(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(16);
            if (bytes.Length != 16) throw new EndOfStreamException();
            return new Guid(bytes);
        }
        public static int Count(BinaryReader reader, int maximum)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum) throw new InvalidDataException("Invalid map length");
            return value;
        }
        public static void End(BinaryReader reader)
        { if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing map data"); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
