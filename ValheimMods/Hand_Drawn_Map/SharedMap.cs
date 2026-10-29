using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Hand_Drawn_Map
{
    public enum MapEditKind : byte { Empty, Draw, Annotate, Erase, End, Undo, ClearAnnotations }

    public sealed class MapEdit
    {
        public Guid Id = Guid.NewGuid();
        public Guid Gesture;
        public Guid UndoGesture;
        public long Actor;
        public long Sequence;
        public long Revision;
        public MapEditKind Kind;
        public float Width;
        public double Time;
        public float Lifetime;
        public ChartPoint[] Points = Array.Empty<ChartPoint>();
        public Guid[] Targets = Array.Empty<Guid>();
        public MapEdit Copy() => (MapEdit)MemberwiseClone();
    }

    public sealed class SharedMap
    {
        public const int MaxEvents = 100000;
        public const int MaxChunkPoints = 128;
        public const int MaxTargets = 1024;
        public const int MaxStoredPoints = 400000;
        public const int MaxChunks = 40000;
        private const int MaxVisiblePoints = 800000;
        private const int MaxVisiblePieces = 100000;
        private readonly Dictionary<Guid, MapEdit> chunks = new Dictionary<Guid, MapEdit>();
        private readonly Dictionary<Guid, Group> groups = new Dictionary<Guid, Group>();
        private readonly Dictionary<Guid, List<MapEdit>> masks = new Dictionary<Guid, List<MapEdit>>();
        private readonly Dictionary<Guid, List<ChartStroke>> visible = new Dictionary<Guid, List<ChartStroke>>();
        private readonly Dictionary<Guid, MapEdit> operations = new Dictionary<Guid, MapEdit>();
        private readonly List<MapEdit> events = new List<MapEdit>();
        private int points;
        private int visiblePoints, visiblePieces;
        private byte[] digest = new byte[32];
        public byte[] Digest => (byte[])digest.Clone();
        public long Revision { get; private set; }
        public IReadOnlyList<MapEdit> Events => events;

        private struct Group
        {
            public long Actor;
            public MapEditKind Kind;
            public bool Closed, Undone;
            public double Expires;
        }

        public SharedMap Fork()
        {
            var copy = new SharedMap { points = points, Revision = Revision, visiblePoints = visiblePoints, visiblePieces = visiblePieces };
            copy.digest = digest;
            foreach (var pair in chunks) copy.chunks.Add(pair.Key, pair.Value);
            foreach (var pair in groups) copy.groups.Add(pair.Key, pair.Value);
            foreach (var pair in masks) copy.masks.Add(pair.Key, pair.Value);
            foreach (var pair in visible) copy.visible.Add(pair.Key, pair.Value);
            foreach (var pair in operations) copy.operations.Add(pair.Key, pair.Value);
            copy.events.AddRange(events);
            return copy;
        }

        public bool Prepare(MapEdit request, long actor, double now, float annotationLifetime, out MapEdit edit, out string error)
        {
            edit = request.Copy();
            edit.Actor = actor;
            edit.Time = now;
            edit.Lifetime = annotationLifetime;
            edit.Revision = Revision + 1;
            error = null;
            if (operations.ContainsKey(edit.Id)) { error = "Duplicate operation"; return false; }
            if (events.Count >= MaxEvents) { error = "Shared map edit limit reached"; return false; }
            if (edit.Id == Guid.Empty || edit.Points.Length > MaxChunkPoints || edit.Targets.Length > MaxTargets)
            { error = "Invalid edit size"; return false; }
            foreach (ChartPoint point in edit.Points)
                if (!Finite(point.X) || !Finite(point.Z) || Math.Abs(point.X) > 100000 || Math.Abs(point.Z) > 100000)
                { error = "Invalid coordinate"; return false; }
            if (edit.Kind == MapEditKind.Draw || edit.Kind == MapEditKind.Annotate || edit.Kind == MapEditKind.Erase)
            {
                if (edit.Gesture == Guid.Empty) { error = "Missing gesture"; return false; }
                if (groups.TryGetValue(edit.Gesture, out Group group) &&
                    (group.Actor != actor || group.Kind != edit.Kind || group.Closed || group.Undone))
                { error = "Gesture is closed or belongs to another editor"; return false; }
                if (!Finite(edit.Width) || edit.Width <= 0 || edit.Width > 10000)
                { error = "Invalid brush size"; return false; }
                if (edit.Kind == MapEditKind.Erase)
                {
                    if (edit.Points.Length != 2 || edit.Targets.Length == 0)
                    { error = "Invalid erase"; return false; }
                    foreach (Guid target in edit.Targets)
                        if (!chunks.ContainsKey(target)) { error = "Unknown erase target; resync required"; return false; }
                    edit.Targets = edit.Targets.Distinct().ToArray();
                    int projectedPoints = visiblePoints, projectedPieces = visiblePieces;
                    foreach (Guid target in edit.Targets)
                        foreach (ChartStroke stroke in visible[target])
                        {
                            var result = ChartGeometry.Erase(stroke, edit.Points[0], edit.Points[1], edit.Width, out _);
                            projectedPieces += result.Count - 1;
                            projectedPoints += result.Sum(piece => piece.Points.Count) - stroke.Points.Count;
                        }
                    if (projectedPoints > MaxVisiblePoints || projectedPieces > MaxVisiblePieces)
                    { error = "Map fragmentation limit reached"; return false; }
                }
                else if (edit.Points.Length == 0 || edit.Targets.Length != 0 || chunks.Count >= MaxChunks || points + edit.Points.Length > MaxStoredPoints
                    || visiblePieces + 1 > MaxVisiblePieces || visiblePoints + edit.Points.Length > MaxVisiblePoints)
                { error = "Shared drawing limit reached"; return false; }
            }
            else if (edit.Kind == MapEditKind.End)
            {
                if (!groups.TryGetValue(edit.Gesture, out Group group) || group.Actor != actor || group.Closed || group.Undone)
                { error = "Invalid gesture end"; return false; }
            }
            else if (edit.Kind == MapEditKind.Undo)
            {
                Guid target = LastUndo(actor);
                if (target == Guid.Empty) { error = "Nothing to undo"; return false; }
                if (edit.UndoGesture != Guid.Empty && edit.UndoGesture != target)
                { error = "Undo target has changed"; return false; }
                edit.UndoGesture = target;
                if (groups[target].Kind == MapEditKind.Erase)
                {
                    SharedMap projected = Fork(); projected.Apply(edit);
                    if (projected.visiblePoints > MaxVisiblePoints || projected.visiblePieces > MaxVisiblePieces)
                    { error = "Undo exceeds map complexity limits"; return false; }
                }
            }
            else if (edit.Kind != MapEditKind.ClearAnnotations)
            { error = "Invalid edit type"; return false; }
            return true;
        }

        public Guid LastUndo(long actor)
        {
            var seen = new HashSet<Guid>();
            for (int i = events.Count - 1; i >= 0; i--)
            {
                MapEdit edit = events[i];
                if (edit.Actor != actor || edit.Kind != MapEditKind.Draw && edit.Kind != MapEditKind.Erase || !seen.Add(edit.Gesture)) continue;
                if (seen.Count > 50) break;
                if (groups.TryGetValue(edit.Gesture, out Group group) && !group.Undone)
                    return edit.Gesture;
            }
            return Guid.Empty;
        }

        public bool Contains(Guid id) => operations.ContainsKey(id);
        public bool Find(Guid id, out MapEdit edit) => operations.TryGetValue(id, out edit);

        public void Apply(MapEdit edit)
        {
            if (operations.ContainsKey(edit.Id)) return;
            if (edit.Revision != Revision + 1) throw new InvalidOperationException("Map revision gap");
            operations.Add(edit.Id, edit);
            events.Add(edit);
            Revision = edit.Revision;
            byte[] encoded = MapWire.EncodeEdit(edit);
            byte[] input = new byte[32 + encoded.Length];
            Buffer.BlockCopy(digest, 0, input, 0, 32); Buffer.BlockCopy(encoded, 0, input, 32, encoded.Length);
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(input);
            if (edit.Kind == MapEditKind.Draw || edit.Kind == MapEditKind.Annotate || edit.Kind == MapEditKind.Erase)
            {
                if (!groups.TryGetValue(edit.Gesture, out Group group))
                    group = new Group { Actor = edit.Actor, Kind = edit.Kind };
                if (edit.Kind == MapEditKind.Annotate) group.Expires = edit.Time + edit.Lifetime;
                groups[edit.Gesture] = group;
                if (edit.Kind == MapEditKind.Erase)
                {
                    foreach (Guid target in edit.Targets.Distinct())
                    {
                        if (!chunks.ContainsKey(target)) continue;
                        var list = masks.TryGetValue(target, out var old) ? new List<MapEdit>(old) : new List<MapEdit>();
                        list.Add(edit); masks[target] = list;
                        ApplyMask(target, edit);
                    }
                }
                else
                {
                    chunks.Add(edit.Id, edit);
                    points += edit.Points.Length;
                    Rebuild(edit.Id);
                }
            }
            else if (edit.Kind == MapEditKind.End && groups.TryGetValue(edit.Gesture, out Group ended))
            {
                ended.Closed = true;
                if (ended.Kind == MapEditKind.Annotate) ended.Expires = edit.Time + edit.Lifetime;
                groups[edit.Gesture] = ended;
            }
            else if (edit.Kind == MapEditKind.Undo && groups.TryGetValue(edit.UndoGesture, out Group undone))
            {
                undone.Undone = true; undone.Closed = true;
                groups[edit.UndoGesture] = undone;
                foreach (var chunk in chunks)
                    if (chunk.Value.Gesture == edit.UndoGesture || masks.TryGetValue(chunk.Key, out var erases) && erases.Any(mask => mask.Gesture == edit.UndoGesture))
                        Rebuild(chunk.Key);
            }
            else if (edit.Kind == MapEditKind.ClearAnnotations)
            {
                foreach (Guid id in groups.Keys.ToArray())
                {
                    Group group = groups[id];
                    if (group.Kind != MapEditKind.Annotate) continue;
                    group.Undone = true; group.Closed = true; groups[id] = group;
                }
                foreach (var chunk in chunks)
                    if (chunk.Value.Kind == MapEditKind.Annotate) Rebuild(chunk.Key);
            }
        }

        private void Rebuild(Guid id)
        {
            MapEdit chunk = chunks[id];
            if (groups[chunk.Gesture].Undone) { SetVisible(id, new List<ChartStroke>()); return; }
            var stroke = new ChartStroke(chunk.Width);
            stroke.Points.AddRange(chunk.Points);
            var result = new List<ChartStroke> { stroke };
            if (masks.TryGetValue(id, out var erases))
                foreach (MapEdit erase in erases)
                {
                    if (groups[erase.Gesture].Undone) continue;
                    var next = new List<ChartStroke>();
                    foreach (ChartStroke piece in result)
                        next.AddRange(ChartGeometry.Erase(piece, erase.Points[0], erase.Points[1], erase.Width, out _));
                    result = next;
                }
            SetVisible(id, result);
        }

        private void ApplyMask(Guid id, MapEdit erase)
        {
            var result = new List<ChartStroke>();
            foreach (ChartStroke stroke in visible[id])
                result.AddRange(ChartGeometry.Erase(stroke, erase.Points[0], erase.Points[1], erase.Width, out _));
            SetVisible(id, result);
        }

        private void SetVisible(Guid id, List<ChartStroke> pieces)
        {
            if (visible.TryGetValue(id, out var previous))
            { visiblePieces -= previous.Count; visiblePoints -= previous.Sum(stroke => stroke.Points.Count); }
            visible[id] = pieces;
            visiblePieces += pieces.Count; visiblePoints += pieces.Sum(stroke => stroke.Points.Count);
        }

        public void Fill(ChartDocument document, List<TemporaryInk> annotations, double now)
        {
            document.Strokes.Clear(); document.PointCount = 0; annotations.Clear();
            foreach (var chunk in chunks)
            {
                Group group = groups[chunk.Value.Gesture];
                if (chunk.Value.Kind == MapEditKind.Annotate)
                {
                    if (group.Expires <= now) continue;
                    float opacity = (float)Math.Max(0, Math.Min(1, (group.Expires - now) / Math.Min(30, chunk.Value.Lifetime)));
                    foreach (ChartStroke stroke in visible[chunk.Key]) annotations.Add(new TemporaryInk(stroke, opacity));
                }
                else
                    foreach (ChartStroke stroke in visible[chunk.Key])
                    { document.Strokes.Add(stroke); document.PointCount += stroke.Points.Count; }
            }
        }

        public Guid[] Targets(ChartPoint from, ChartPoint to, float radius, double now)
        {
            var result = new List<Guid>();
            foreach (var chunk in chunks)
            {
                if (chunk.Value.Kind == MapEditKind.Annotate && groups[chunk.Value.Gesture].Expires <= now) continue;
                float r = radius + chunk.Value.Width * 0.5f;
                foreach (ChartStroke stroke in visible[chunk.Key])
                {
                    bool hit = false;
                    foreach (ChartPoint p in stroke.Points)
                        if (ChartGeometry.DistanceSquared(p, from, to) <= r * r) { hit = true; break; }
                    if (!hit)
                        for (int i = 1; i < stroke.Points.Count; i++)
                        {
                            ChartPoint a = stroke.Points[i - 1], b = stroke.Points[i];
                            float halfX = Math.Abs(to.X - from.X) * 0.5f + r;
                            float halfZ = Math.Abs(to.Z - from.Z) * 0.5f + r;
                            ChartPoint center = (from + to) * 0.5f;
                            a -= center; b -= center;
                            if (ChartGeometry.ClipLine(ref a, ref b, halfX, halfZ)) { hit = true; break; }
                        }
                    if (hit)
                    {
                        ChartGeometry.Erase(stroke, from, to, radius, out bool touched);
                        if (touched) { result.Add(chunk.Key); break; }
                    }
                }
            }
            return result.ToArray();
        }

        public MapEdit ForDisk(MapEdit edit)
        {
            MapEdit copy = edit.Copy();
            if (edit.Kind == MapEditKind.Annotate || edit.Kind == MapEditKind.ClearAnnotations ||
                edit.Kind == MapEditKind.End && groups.TryGetValue(edit.Gesture, out Group group) && group.Kind == MapEditKind.Annotate)
            { copy.Kind = MapEditKind.Empty; copy.Points = Array.Empty<ChartPoint>(); copy.Targets = Array.Empty<Guid>(); }
            else if (edit.Kind == MapEditKind.Erase)
                copy.Targets = edit.Targets.Where(id => chunks.ContainsKey(id) && chunks[id].Kind != MapEditKind.Annotate).ToArray();
            return copy;
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct TemporaryInk
    {
        public readonly ChartStroke Stroke;
        public readonly float Opacity;
        public TemporaryInk(ChartStroke stroke, float opacity) { Stroke = stroke; Opacity = opacity; }
    }

    public sealed class MapReplica
    {
        public SharedMap Confirmed { get; private set; } = new SharedMap();
        public SharedMap Predicted { get; private set; } = new SharedMap();
        public readonly List<MapEdit> Pending = new List<MapEdit>();
        public readonly List<MapEdit> Recovery = new List<MapEdit>();
        public long Actor;
        public float AnnotationLifetime = 120;
        public bool Ready;
        public string Error;

        public bool Propose(MapEdit request, double now)
        {
            if (!Ready || Pending.Count >= 256) { Error = "Map is syncing or waiting for the owner"; return false; }
            if (!Predicted.Prepare(request, Actor, now, AnnotationLifetime, out MapEdit edit, out string error))
            { Error = error; return false; }
            Pending.Add(request);
            Error = null;
            Predicted.Apply(edit);
            return true;
        }

        public void Accept(MapEdit edit, double now)
        {
            if (edit.Revision > Confirmed.Revision) Confirmed.Apply(edit);
            Pending.RemoveAll(request => request.Id == edit.Id);
            Rebase(now);
        }

        public void Reject(Guid id, string reason, double now)
        {
            foreach (MapEdit edit in Pending.Where(e => e.Id == id)) Preserve(edit);
            Pending.RemoveAll(request => request.Id == id); Error = reason; Rebase(now);
        }

        public void Snapshot(SharedMap snapshot, double now)
        {
            Confirmed = snapshot;
            Error = null;
            Pending.RemoveAll(request => Confirmed.Contains(request.Id));
            Ready = true;
            Rebase(now);
        }

        private void Rebase(double now)
        {
            Predicted = Confirmed.Fork();
            foreach (MapEdit request in Pending.ToArray())
            {
                if (Predicted.Prepare(request, Actor, now, AnnotationLifetime, out MapEdit edit, out _)) Predicted.Apply(edit);
                else { Preserve(request); Pending.Remove(request); Error = "A pending edit conflicted with the owner map"; }
            }
        }
        private void Preserve(MapEdit edit)
        {
            if ((edit.Kind == MapEditKind.Draw || edit.Kind == MapEditKind.Erase) && Recovery.Count < 256) Recovery.Add(edit);
        }
    }

    public sealed class MapEditorGuard
    {
        private long nextSequence = 1;
        private double lastTime;
        private double tokens = 256;
        public bool Accept(long sequence, double now, out string error)
        {
            error = null;
            if (sequence != nextSequence) { error = "Editor sequence gap; resync required"; return false; }
            nextSequence++;
            return Replay(now, out error);
        }
        public bool Replay(double now, out string error)
        {
            error = null;
            tokens = Math.Min(256, tokens + Math.Max(0, now - lastTime) * 120);
            lastTime = now;
            if (tokens < 1) { error = "Map edit rate exceeded"; return false; }
            tokens--;
            return true;
        }
    }
}
