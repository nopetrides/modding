using System;
using System.Collections.Generic;

namespace Hand_Drawn_Map
{
    public readonly struct ChartPoint
    {
        public readonly float X;
        public readonly float Z;
        public ChartPoint(float x, float z) { X = x; Z = z; }
        public static ChartPoint operator +(ChartPoint a, ChartPoint b) => new ChartPoint(a.X + b.X, a.Z + b.Z);
        public static ChartPoint operator -(ChartPoint a, ChartPoint b) => new ChartPoint(a.X - b.X, a.Z - b.Z);
        public static ChartPoint operator *(ChartPoint a, float b) => new ChartPoint(a.X * b, a.Z * b);
        public float LengthSquared => X * X + Z * Z;
        public static float Dot(ChartPoint a, ChartPoint b) => a.X * b.X + a.Z * b.Z;
        public static ChartPoint Lerp(ChartPoint a, ChartPoint b, float t) => a + (b - a) * t;
    }

    public sealed class ChartStroke
    {
        public float Width;
        public readonly List<ChartPoint> Points = new List<ChartPoint>();
        public ChartStroke(float width) { Width = width; }
    }

    public sealed class ChartDocument
    {
        public const int MaxPoints = 200000;
        public const int MaxStrokes = 20000;
        public const int MaxStrokePoints = 4096;
        public readonly List<ChartStroke> Strokes = new List<ChartStroke>();
        public ChartPoint Center;
        public float Zoom = 0.1f;
        public int PointCount;

        public bool Erase(ChartPoint from, ChartPoint to, float radius)
        {
            var result = new List<ChartStroke>();
            bool changed = false;
            foreach (ChartStroke stroke in Strokes)
            {
                List<ChartStroke> pieces = ChartGeometry.Erase(stroke, from, to, radius, out bool cut);
                changed |= cut;
                result.AddRange(pieces);
            }
            int count = 0;
            foreach (ChartStroke stroke in result) count += stroke.Points.Count;
            if (!changed || result.Count > MaxStrokes || count > MaxPoints) return false;
            Strokes.Clear();
            Strokes.AddRange(result);
            PointCount = count;
            return true;
        }
    }

    public static class ChartGeometry
    {
        public static ChartPoint ToWorld(ChartPoint screen, ChartPoint center, float worldPerPixel)
            => center + screen * worldPerPixel;
        public static ChartPoint ToScreen(ChartPoint world, ChartPoint center, float worldPerPixel)
            => (world - center) * (1f / worldPerPixel);

        public static float DistanceSquared(ChartPoint point, ChartPoint a, ChartPoint b)
        {
            ChartPoint d = b - a;
            float t = d.LengthSquared < 1e-12f ? 0f : Clamp(ChartPoint.Dot(point - a, d) / d.LengthSquared, 0f, 1f);
            return (point - ChartPoint.Lerp(a, b, t)).LengthSquared;
        }

        public static List<ChartStroke> Erase(ChartStroke stroke, ChartPoint from, ChartPoint to, float radius, out bool changed)
        {
            changed = false;
            float r = radius + stroke.Width * 0.5f;
            float r2 = r * r;
            var result = new List<ChartStroke>();
            if (stroke.Points.Count == 1)
            {
                changed = DistanceSquared(stroke.Points[0], from, to) <= r2;
                if (!changed) result.Add(stroke);
                return result;
            }
            ChartStroke current = null;
            for (int i = 1; i < stroke.Points.Count; i++)
            {
                ChartPoint a = stroke.Points[i - 1];
                ChartPoint b = stroke.Points[i];
                ChartPoint d = b - a;
                var cuts = new List<float> { 0f, 1f };
                CircleCuts(a, d, from, r, cuts);
                CircleCuts(a, d, to, r, cuts);
                ChartPoint axis = to - from;
                if (axis.LengthSquared > 1e-12f)
                {
                    var normal = new ChartPoint(-axis.Z, axis.X) * (1f / (float)Math.Sqrt(axis.LengthSquared));
                    float denominator = ChartPoint.Dot(d, normal);
                    if (Math.Abs(denominator) > 1e-9f)
                    {
                        float offset = ChartPoint.Dot(a - from, normal);
                        AddCut((r - offset) / denominator, cuts);
                        AddCut((-r - offset) / denominator, cuts);
                    }
                }
                cuts.Sort();
                for (int j = 1; j < cuts.Count; j++)
                {
                    float lo = cuts[j - 1], hi = cuts[j];
                    if (hi - lo < 1e-7f) continue;
                    if (DistanceSquared(ChartPoint.Lerp(a, b, (lo + hi) * 0.5f), from, to) < r2)
                    {
                        changed = true;
                        current = null;
                        continue;
                    }
                    ChartPoint start = ChartPoint.Lerp(a, b, lo);
                    ChartPoint end = ChartPoint.Lerp(a, b, hi);
                    if (current == null)
                    {
                        current = new ChartStroke(stroke.Width);
                        current.Points.Add(start);
                        result.Add(current);
                    }
                    current.Points.Add(end);
                }
            }
            if (!changed) return new List<ChartStroke> { stroke };
            return result;
        }

        private static void CircleCuts(ChartPoint a, ChartPoint d, ChartPoint center, float radius, List<float> cuts)
        {
            double aa = (double)d.X * d.X + (double)d.Z * d.Z;
            if (aa < 1e-12) return;
            ChartPoint offset = a - center;
            double bb = 2.0 * ((double)offset.X * d.X + (double)offset.Z * d.Z);
            double cc = (double)offset.X * offset.X + (double)offset.Z * offset.Z - (double)radius * radius;
            double discriminant = bb * bb - 4 * aa * cc;
            if (discriminant < 0) return;
            double root = Math.Sqrt(discriminant);
            AddCut((float)((-bb - root) / (2 * aa)), cuts);
            AddCut((float)((-bb + root) / (2 * aa)), cuts);
        }

        private static void AddCut(float t, List<float> cuts)
        {
            if (t > 0f && t < 1f) cuts.Add(t);
        }

        public static bool ClipLine(ref ChartPoint a, ref ChartPoint b, float halfWidth, float halfHeight)
        {
            ChartPoint d = b - a;
            float lo = 0f, hi = 1f;
            if (!Clip(-d.X, a.X + halfWidth, ref lo, ref hi) ||
                !Clip(d.X, halfWidth - a.X, ref lo, ref hi) ||
                !Clip(-d.Z, a.Z + halfHeight, ref lo, ref hi) ||
                !Clip(d.Z, halfHeight - a.Z, ref lo, ref hi)) return false;
            b = a + d * hi;
            a += d * lo;
            return true;
        }

        private static bool Clip(float p, float q, ref float lo, ref float hi)
        {
            if (Math.Abs(p) < 1e-12f) return q >= 0f;
            float r = q / p;
            if (p < 0f) { if (r > hi) return false; lo = Math.Max(lo, r); }
            else { if (r < lo) return false; hi = Math.Min(hi, r); }
            return true;
        }

        private static float Clamp(float value, float lo, float hi) => Math.Max(lo, Math.Min(hi, value));
    }
}
