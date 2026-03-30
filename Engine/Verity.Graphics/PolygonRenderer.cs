using System.Numerics;
using Verity.Core;
using Verity.Core.ECS;
using SystemNumericsVector3 = System.Numerics.Vector3;

namespace Verity.Graphics;

public class PolygonRenderer : Component
{
    [SerializeField]
    public List<Vector2> Vertices { get; set; } = new()
    {
        new Vector2(0, 0.5f),
        new Vector2(-0.5f, -0.5f),
        new Vector2(0.5f, -0.5f)
    };

    [SerializeField]
    public Verity.Core.Color Color { get; set; } = Verity.Core.Color.White;

    [SerializeField]
    public float Thickness { get; set; } = 0.05f;

    [SerializeField]
    public bool IsClosed { get; set; } = true;

    [SerializeField]
    public bool Fill { get; set; } = true;

    [SerializeField]
    public string SortingLayerName { get; set; } = "Default";

    [SerializeField]
    public int OrderInLayer { get; set; } = 0;

    [SerializeField]
    public bool CastShadows { get; set; } = true;

    [SerializeField]
    public ShadowCasterSourceMode ShadowSourceMode { get; set; } = ShadowCasterSourceMode.PreferRenderer;

    [SerializeField]
    public ShadowSelfMode ShadowSelfMode { get; set; } = ShadowSelfMode.ExcludeSelf;

    internal int ResolvedLayerIndex => SortingLayer.GetLayerIndex(SortingLayerName);

    public Vector2[] GetWorldVertices()
    {
        var transform = Owner.Transform;
        if (transform == null || Vertices.Count == 0) return Array.Empty<Vector2>();

        var worldMatrix = transform.GetWorldMatrix();
        Vector2[] result = new Vector2[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            var v3 = SystemNumericsVector3.Transform(new SystemNumericsVector3(Vertices[i], 0), worldMatrix);
            result[i] = new Vector2(v3.X, v3.Y);
        }
        return result;
    }

    [Button("Sync With Shape")]
    public void SyncWithShape()
    {
        var shape = Owner.GetComponent<Verity.Core.Physics.PolygonShape>();
        if (shape != null)
        {
            Vertices = new List<Vector2>(shape.Vertices);
            // 만약 쉐이프에 오프셋이 있다면 렌더러의 정점에 적용 (렌더러는 오프셋이 없음)
            for (int i = 0; i < Vertices.Count; i++) Vertices[i] += shape.Offset;
        }
    }

    public bool IsSelfIntersecting()
    {
        if (Vertices.Count < 4) return false;
        for (int i = 0; i < Vertices.Count; i++)
        {
            for (int j = i + 2; j < Vertices.Count; j++)
            {
                if (i == 0 && j == Vertices.Count - 1) continue;
                if (Intersect(Vertices[i], Vertices[(i + 1) % Vertices.Count], Vertices[j], Vertices[(j + 1) % Vertices.Count]))
                    return true;
            }
        }
        return false;
    }

    private bool Intersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float Cross(Vector2 v1, Vector2 v2) => v1.X * v2.Y - v1.Y * v2.X;
        float Side(Vector2 p, Vector2 q, Vector2 r) => Cross(q - p, r - p);
        return Side(a, b, c) * Side(a, b, d) < -0.0001f && Side(c, d, a) * Side(c, d, b) < -0.0001f;
    }

    public int[] Triangulate()
    {
        if (Vertices.Count < 3 || IsSelfIntersecting()) return Array.Empty<int>();

        List<int> indices = new List<int>();
        List<int> V = new List<int>();
        for (int i = 0; i < Vertices.Count; i++) V.Add(i);

        // 점들이 시계방향인지 반시계방향인지 확인 (Shoelace formula)
        float area = 0;
        for (int i = 0; i < Vertices.Count; i++)
        {
            Vector2 p1 = Vertices[i];
            Vector2 p2 = Vertices[(i + 1) % Vertices.Count];
            area += (p1.X * p2.Y) - (p2.X * p1.Y);
        }

        // 알고리즘이 시계방향 기준이므로 시계방향으로 정렬 (Shoelace formula 결과가 양수면 반시계, 음수면 시계)
        if (area > 0) V.Reverse();

        int iterations = 0;
        while (V.Count > 3 && iterations < 1000)
        {
            iterations++;
            bool earFound = false;
            for (int i = 0; i < V.Count; i++)
            {
                int prev = V[(i + V.Count - 1) % V.Count];
                int curr = V[i];
                int next = V[(i + 1) % V.Count];

                if (IsEar(prev, curr, next, V))
                {
                    indices.Add(prev); indices.Add(curr); indices.Add(next);
                    V.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }
            if (!earFound) break;
        }
        if (V.Count == 3)
        {
            indices.Add(V[0]); indices.Add(V[1]); indices.Add(V[2]);
        }

        return indices.ToArray();
    }

    private bool IsEar(int p, int c, int n, List<int> V)
    {
        Vector2 a = Vertices[p]; Vector2 b = Vertices[c]; Vector2 d = Vertices[n];

        // 1. 볼록한 꼭짓점인지 확인 (Cross product)
        float cross = (b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X);
        if (cross >= 0) return false; // 시계방향 정렬 시 음수여야 볼록함

        // 2. 삼각형 안에 다른 점이 있는지 확인
        for (int i = 0; i < V.Count; i++)
        {
            int idx = V[i];
            if (idx == p || idx == c || idx == n) continue;
            if (PointInTriangle(Vertices[idx], a, b, d)) return false;
        }
        return true;
    }

    private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        float alpha = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / det;
        float beta = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / det;
        float gamma = 1.0f - alpha - beta;
        return alpha >= 0 && beta >= 0 && gamma >= 0;
    }
}
