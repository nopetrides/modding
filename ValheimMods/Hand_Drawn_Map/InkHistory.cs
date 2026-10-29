using System;
using System.Collections.Generic;
using System.Linq;

namespace Hand_Drawn_Map
{
    public sealed class InkHistory
    {
        private const int MaxActions = 50;
        private const int MaxRetainedPoints = 1000000;
        private readonly List<Edit> edits = new List<Edit>();
        private List<ChartStroke> before;
        private int retainedPoints;
        public int Count => edits.Count;

        public void Begin(ChartDocument document)
        {
            if (before == null) before = new List<ChartStroke>(document.Strokes);
        }

        public void Commit(ChartDocument document)
        {
            if (before == null) return;
            var previous = new HashSet<ChartStroke>(before);
            var current = new HashSet<ChartStroke>(document.Strokes);
            var edit = new Edit();
            for (int i = 0; i < before.Count; i++)
                if (!current.Contains(before[i])) edit.Removed.Add(new SavedStroke(before[i], i));
            for (int i = 0; i < document.Strokes.Count; i++)
                if (!previous.Contains(document.Strokes[i])) edit.Added.Add(new SavedStroke(document.Strokes[i], i));
            before = null;
            if (edit.Added.Count == 0 && edit.Removed.Count == 0) return;
            edits.Add(edit);
            retainedPoints += edit.PointCount;
            while (edits.Count > MaxActions || retainedPoints > MaxRetainedPoints)
            {
                retainedPoints -= edits[0].PointCount;
                edits.RemoveAt(0);
            }
        }

        public bool Undo(ChartDocument document, out bool conflict)
        {
            conflict = false;
            if (before != null || edits.Count == 0) return false;
            Edit edit = edits[edits.Count - 1];
            var current = new HashSet<ChartStroke>(document.Strokes);
            foreach (SavedStroke added in edit.Added)
                if (!current.Contains(added.Stroke) || !added.Matches()) { conflict = true; return false; }
            foreach (SavedStroke removed in edit.Removed)
                if (current.Contains(removed.Stroke) || !removed.Matches()) { conflict = true; return false; }
            int points = document.PointCount - edit.AddedPoints + edit.RemovedPoints;
            int strokes = document.Strokes.Count - edit.Added.Count + edit.Removed.Count;
            if (points > ChartDocument.MaxPoints || strokes > ChartDocument.MaxStrokes)
            { conflict = true; return false; }
            foreach (SavedStroke added in edit.Added) document.Strokes.Remove(added.Stroke);
            foreach (SavedStroke removed in edit.Removed)
                document.Strokes.Insert(Math.Min(removed.Index, document.Strokes.Count), removed.Stroke);
            document.PointCount = points;
            retainedPoints -= edit.PointCount;
            edits.RemoveAt(edits.Count - 1);
            return true;
        }

        private sealed class Edit
        {
            public readonly List<SavedStroke> Added = new List<SavedStroke>();
            public readonly List<SavedStroke> Removed = new List<SavedStroke>();
            public int AddedPoints => Added.Sum(stroke => stroke.Points.Length);
            public int RemovedPoints => Removed.Sum(stroke => stroke.Points.Length);
            public int PointCount => AddedPoints + RemovedPoints;
        }

        private sealed class SavedStroke
        {
            public readonly ChartStroke Stroke;
            public readonly ChartPoint[] Points;
            public readonly int Index;
            private readonly float width;
            public SavedStroke(ChartStroke stroke, int index)
            { Stroke = stroke; Points = stroke.Points.ToArray(); Index = index; width = stroke.Width; }
            public bool Matches()
            {
                if (Stroke.Width != width || Stroke.Points.Count != Points.Length) return false;
                for (int i = 0; i < Points.Length; i++)
                    if (Stroke.Points[i].X != Points[i].X || Stroke.Points[i].Z != Points[i].Z) return false;
                return true;
            }
        }
    }

    public static class MetricScale
    {
        public static float Length(float worldPerPixel, float targetPixels)
        {
            double target = worldPerPixel * targetPixels;
            if (target <= 0 || double.IsInfinity(target) || double.IsNaN(target)) return 1f;
            double power = Math.Pow(10, Math.Floor(Math.Log10(target)));
            double fraction = target / power;
            return (float)((fraction >= 5 ? 5 : fraction >= 2 ? 2 : 1) * power);
        }

        public static string Label(float meters)
            => meters >= 1000f ? (meters / 1000f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " km"
                : meters.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " m";
    }
}
