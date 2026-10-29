using UnityEngine;
using UnityEngine.UI;

namespace Hand_Drawn_Map
{
    public sealed class BrushGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int sides = 48;
            float radius = rectTransform.rect.width * 0.5f;
            float innerRadius = Mathf.Max(0f, radius - 1f);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                Vector2 first = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector2 second = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                int index = mesh.currentVertCount;
                Add(mesh, Vector2.zero, color);
                Add(mesh, first * innerRadius, color);
                Add(mesh, second * innerRadius, color);
                mesh.AddTriangle(index, index + 1, index + 2);
                Color edge = new Color(color.r, color.g, color.b, Mathf.Min(1f, color.a * 3f));
                index = mesh.currentVertCount;
                Add(mesh, first * innerRadius, edge);
                Add(mesh, first * radius, edge);
                Add(mesh, second * radius, edge);
                Add(mesh, second * innerRadius, edge);
                mesh.AddTriangle(index, index + 1, index + 2);
                mesh.AddTriangle(index, index + 2, index + 3);
            }
        }

        private static void Add(VertexHelper mesh, Vector2 position, Color color)
        {
            UIVertex vertex = UIVertex.simpleVert;
            vertex.position = position; vertex.color = color; vertex.uv0 = Vector2.zero;
            mesh.AddVert(vertex);
        }
    }
}
