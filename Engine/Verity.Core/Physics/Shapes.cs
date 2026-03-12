using System.Numerics;
using System.Reflection;
using Verity.Core.ECS;
using Verity.Core;

namespace Verity.Core.Physics;

public class BoxShape : PhysicalShape
{
    [SerializeField]
    public Vector2 Size { get; set; } = Vector2.One;

    public override AABB GetAABB()
    {
        var vertices = GetVertices();
        if (vertices.Length == 0) return new AABB();

        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        foreach (var v in vertices)
        {
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
        return new AABB(min, max);
    }

    public override Vector2[] GetVertices()
    {
        var transform = Owner.GetComponent<Transform>();
        if (transform == null) return Array.Empty<Vector2>();

        Vector2 baseScale = GetBaseScale();
        Vector2 effSize = Size * baseScale;
        if (MathF.Abs(effSize.X) < 0.0001f || MathF.Abs(effSize.Y) < 0.0001f) return Array.Empty<Vector2>();

        float rotationRad = transform.WorldRotation * MathF.PI / 180.0f;
        Vector2 pos = transform.WorldPosition;
        
        Vector2 halfSize = effSize / 2.0f;
        Vector2 rotatedOffset = RotateVector(Offset * baseScale, rotationRad);
        Vector2 center = pos + rotatedOffset;

        Vector2[] localPoints = new[]
        {
            new Vector2(-halfSize.X, -halfSize.Y),
            new Vector2(halfSize.X, -halfSize.Y),
            new Vector2(halfSize.X, halfSize.Y),
            new Vector2(-halfSize.X, halfSize.Y)
        };

        Vector2[] result = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            result[i] = center + RotateVector(localPoints[i], rotationRad);
        }
        return result;
    }

    public override float CalculateInertiaCoefficient()
    {
        Vector2 s = Size * GetBaseScale();
        return (s.X * s.X + s.Y * s.Y) / 12.0f;
    }

    private static Vector2 RotateVector(Vector2 v, float rad)
    {
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}

public class CircleShape : PhysicalShape
{
    [SerializeField]
    public float Radius { get; set; } = 0.5f;

    public override AABB GetAABB()
    {
        var transform = Owner.GetComponent<Transform>();
        if (transform == null) return new AABB();

        Vector2 baseScale = GetBaseScale();
        float scaledRadius = Radius * Math.Max(baseScale.X, baseScale.Y);
        float rotationRad = transform.WorldRotation * MathF.PI / 180.0f;
        Vector2 pos = transform.WorldPosition + RotateVector(Offset * baseScale, rotationRad);
        
        return new AABB(pos - new Vector2(scaledRadius), pos + new Vector2(scaledRadius));
    }

    public override Vector2[] GetVertices() => Array.Empty<Vector2>();

    public override float CalculateInertiaCoefficient()
    {
        Vector2 baseScale = GetBaseScale();
        float r = Radius * Math.Max(baseScale.X, baseScale.Y);
        return (r * r) / 2.0f;
    }

    private static Vector2 RotateVector(Vector2 v, float rad)
    {
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}

public class PolygonShape : PhysicalShape
{
    [SerializeField]
    public List<Vector2> Vertices { get; set; } = new()
    {
        new Vector2(0, 0.5f),
        new Vector2(-0.433f, -0.25f),
        new Vector2(0.433f, -0.25f)
    };

    public PolygonShape()
    {
        // 생성 시점에 Owner를 알 수 없으므로, 초기화 로직은 지연 처리되거나 
        // 외부(Entity.AddComponent)에서 처리해야 함. 
        // 하지만 [Button]을 통한 수동 동기화를 먼저 제공.
    }

    [Button("Sync With Renderer")]
    public void SyncWithRenderer()
    {
        if (Owner == null) return;

        // PolygonRenderer is in Verity.Graphics project.
        // To avoid circular dependency, we use reflection to find any component with 'Vertices' property.
        foreach (var comp in Owner.GetAllComponents())
        {
            var type = comp.GetType();
            if (type.Name == "PolygonRenderer" || type.GetProperty("Vertices") != null)
            {
                var prop = type.GetProperty("Vertices");
                if (prop != null)
                {
                    try
                    {
                        var value = prop.GetValue(comp);
                        if (value is IEnumerable<Vector2> vertices)
                        {
                            Vertices = new List<Vector2>();
                            foreach (var v in vertices)
                            {
                                Vertices.Add(v - Offset);
                            }
                            return;
                        }
                    }
                    catch
                    {
                        // Ignore errors during reflection
                    }
                }
            }
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

    public override AABB GetAABB()
    {
        var vertices = GetVertices();
        if (vertices.Length == 0) return new AABB();

        Vector2 min = vertices[0];
        Vector2 max = vertices[0];
        foreach (var v in vertices)
        {
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }
        return new AABB(min, max);
    }

    public override Vector2[] GetVertices()
    {
        if (IsSelfIntersecting()) return Array.Empty<Vector2>();

        var transform = Owner?.GetComponent<Transform>();
        if (transform == null || Vertices.Count == 0) return Array.Empty<Vector2>();

        Vector2 baseScale = GetBaseScale();
        float rotationRad = transform.WorldRotation * MathF.PI / 180.0f;
        Vector2 pos = transform.WorldPosition;

        Vector2[] result = new Vector2[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            Vector2 localPos = (Vertices[i] + Offset) * baseScale;
            result[i] = pos + RotateVector(localPos, rotationRad);
        }
        return result;
    }

    public List<Vector2[]> GetConvexSubShapes()
    {
        var worldVertices = GetVertices();
        if (worldVertices.Length < 3) return new List<Vector2[]>();

        // 이미 볼록한지 확인
        if (IsConvex(worldVertices)) return new List<Vector2[]> { worldVertices };

        // 오목하다면 삼각형으로 분할 (삼각형은 무조건 볼록함)
        var indices = Triangulate();
        var subShapes = new List<Vector2[]>();
        for (int i = 0; i < indices.Length; i += 3)
        {
            subShapes.Add(new[] { worldVertices[indices[i]], worldVertices[indices[i + 1]], worldVertices[indices[i + 2]] });
        }
        return subShapes;
    }

    private bool IsConvex(Vector2[] v)
    {
        if (v.Length < 3) return false;
        bool? gotNegative = null;
        for (int i = 0; i < v.Length; i++)
        {
            Vector2 a = v[i];
            Vector2 b = v[(i + 1) % v.Length];
            Vector2 c = v[(i + 2) % v.Length];
            float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross == 0) continue;
            bool isNegative = cross < 0;
            if (gotNegative == null) gotNegative = isNegative;
            else if (gotNegative != isNegative) return false;
        }
        return true;
    }

    public int[] Triangulate()
    {
        if (Vertices.Count < 3) return Array.Empty<int>();
        List<int> indices = new List<int>();
        List<int> V = new List<int>();
        for (int i = 0; i < Vertices.Count; i++) V.Add(i);

        float area = 0;
        for (int i = 0; i < Vertices.Count; i++) {
            Vector2 p1 = Vertices[i]; Vector2 p2 = Vertices[(i + 1) % Vertices.Count];
            area += (p1.X * p2.Y) - (p2.X * p1.Y);
        }
        if (area > 0) V.Reverse(); 

        int iterations = 0;
        while (V.Count > 3 && iterations < 1000) {
            iterations++;
            bool earFound = false;
            for (int i = 0; i < V.Count; i++) {
                int prev = V[(i + V.Count - 1) % V.Count];
                int curr = V[i];
                int next = V[(i + 1) % V.Count];
                if (IsEar(prev, curr, next, V)) {
                    indices.Add(prev); indices.Add(curr); indices.Add(next);
                    V.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }
            if (!earFound) break; 
        }
        if (V.Count == 3) { indices.Add(V[0]); indices.Add(V[1]); indices.Add(V[2]); }
        return indices.ToArray();
    }

    private bool IsEar(int p, int c, int n, List<int> V)
    {
        Vector2 a = Vertices[p]; Vector2 b = Vertices[c]; Vector2 d = Vertices[n];
        // Shoelace Area / Cross product check for convexity
        float cross = (b.X - a.X) * (d.Y - a.Y) - (b.Y - a.Y) * (d.X - a.X);
        if (cross >= 0) return false;
        
        for (int i = 0; i < V.Count; i++) {
            int idx = V[i];
            if (idx == p || idx == c || idx == n) continue;
            if (PointInTriangle(Vertices[idx], a, b, d)) return false;
        }
        return true;
    }

    private bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float det = (b.Y - c.Y) * (a.X - c.X) + (c.X - b.X) * (a.Y - c.Y);
        if (Math.Abs(det) < 0.000001f) return false;
        float alpha = ((b.Y - c.Y) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Y)) / det;
        float beta = ((c.Y - a.Y) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Y)) / det;
        float gamma = 1.0f - alpha - beta;
        return alpha >= -0.0001f && beta >= -0.0001f && gamma >= -0.0001f;
    }

    public override float CalculateInertiaCoefficient()
    {
        // Simple approximation for polygon inertia
        var vertices = GetVertices();
        if (vertices.Length < 3) return 1.0f;
        
        float sum1 = 0;
        float sum2 = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 p1 = vertices[i];
            Vector2 p2 = vertices[(i + 1) % vertices.Length];
            float cross = Math.Abs(p1.X * p2.Y - p2.X * p1.Y);
            sum1 += cross * (Vector2.Dot(p1, p1) + Vector2.Dot(p1, p2) + Vector2.Dot(p2, p2));
            sum2 += cross;
        }
        return sum1 / (6.0f * sum2);
    }

    private static Vector2 RotateVector(Vector2 v, float rad)
    {
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
