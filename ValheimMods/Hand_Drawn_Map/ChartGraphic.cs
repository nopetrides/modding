using UnityEngine;
using UnityEngine.UI;

namespace Hand_Drawn_Map
{
    internal readonly struct InkSegment
    {
        public readonly Vector2 A, B;
        public readonly float Width;
        public readonly Color Color;
        public InkSegment(Vector2 a, Vector2 b, float width, Color color)
        { A = a; B = b; Width = width; Color = color; }
    }

    public sealed class ChartGraphic : MaskableGraphic
    {
        internal const int BatchSize = 6000;
        internal CanvasWindow Window;
        internal int Batch;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (Window == null) return;
            int start = Batch * BatchSize;
            int end = Mathf.Min(start + BatchSize, Window.Segments.Count);
            for (int i = start; i < end; i++)
            {
                InkSegment segment = Window.Segments[i];
                Vector2 delta = segment.B - segment.A;
                Vector2 normal = delta.sqrMagnitude > 0.000001f
                    ? new Vector2(-delta.y, delta.x).normalized * (segment.Width * 0.5f)
                    : new Vector2(0f, segment.Width * 0.5f);
                Vector2 cap = delta.sqrMagnitude > 0.000001f
                    ? delta.normalized * (segment.Width * 0.5f) : new Vector2(segment.Width * 0.5f, 0f);
                int index = mesh.currentVertCount;
                Add(mesh, segment.A - cap - normal, segment.Color);
                Add(mesh, segment.A - cap + normal, segment.Color);
                Add(mesh, segment.B + cap + normal, segment.Color);
                Add(mesh, segment.B + cap - normal, segment.Color);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index, index + 2, index + 3);
            }
        }

        private static void Add(VertexHelper mesh, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position;
            vertex.color = color;
            vertex.uv0 = Vector2.zero;
            mesh.AddVert(vertex);
        }
    }
}
