using System;
using System.IO;

namespace Hand_Drawn_Map
{
    internal static class ChartStore
    {
        private const int Magic = 0x48444D31;
        private const int Version = 1;

        public static ChartDocument Load(string path)
        {
            var document = new ChartDocument();
            if (!File.Exists(path)) return document;
            using (var reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version)
                    throw new InvalidDataException("Unsupported chart file.");
                document.Center = ReadPoint(reader);
                document.Zoom = ReadFinite(reader);
                if (document.Zoom <= 0f || document.Zoom > 10f) throw new InvalidDataException("Invalid zoom.");
                int count = ReadCount(reader, ChartDocument.MaxStrokes);
                for (int i = 0; i < count; i++)
                {
                    float width = ReadFinite(reader);
                    if (width <= 0f || width > 100000f) throw new InvalidDataException("Invalid stroke width.");
                    var stroke = new ChartStroke(width);
                    int points = ReadCount(reader, ChartDocument.MaxPoints);
                    if (points == 0 || document.PointCount + points > ChartDocument.MaxPoints)
                        throw new InvalidDataException("Invalid point count.");
                    for (int j = 0; j < points; j++) stroke.Points.Add(ReadPoint(reader));
                    document.PointCount += points;
                    document.Strokes.Add(stroke);
                }
                if (reader.BaseStream.Position != reader.BaseStream.Length)
                    throw new InvalidDataException("Unexpected chart data.");
            }
            return document;
        }

        public static void Save(string path, ChartDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    WritePoint(writer, document.Center);
                    writer.Write(document.Zoom);
                    writer.Write(document.Strokes.Count);
                    foreach (ChartStroke stroke in document.Strokes)
                    {
                        writer.Write(stroke.Width);
                        writer.Write(stroke.Points.Count);
                        foreach (ChartPoint point in stroke.Points) WritePoint(writer, point);
                    }
                }
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }

        private static int ReadCount(BinaryReader reader, int maximum)
        {
            int value = reader.ReadInt32();
            if (value < 0 || value > maximum) throw new InvalidDataException("Chart exceeds size limits.");
            return value;
        }
        private static float ReadFinite(BinaryReader reader)
        {
            float value = reader.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10000000f)
                throw new InvalidDataException("Invalid chart coordinate.");
            return value;
        }
        private static ChartPoint ReadPoint(BinaryReader reader) => new ChartPoint(ReadFinite(reader), ReadFinite(reader));
        private static void WritePoint(BinaryWriter writer, ChartPoint point) { writer.Write(point.X); writer.Write(point.Z); }
    }
}
