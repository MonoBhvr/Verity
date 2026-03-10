using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.Physics;

public class BoxShape : PhysicalShape
{
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

        Vector2 worldScale = transform.Scale;
        float rotationRad = transform.Rotation * MathF.PI / 180.0f;
        Vector2 pos = transform.Position;
        
        Vector2 halfSize = (Size * worldScale) / 2.0f;
        Vector2 rotatedOffset = RotateVector(Offset * worldScale, rotationRad);
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
        var transform = Owner.GetComponent<Transform>();
        Vector2 s = Size * (transform?.Scale ?? Vector2.One);
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
    public float Radius { get; set; } = 0.5f;

    public override AABB GetAABB()
    {
        var transform = Owner.GetComponent<Transform>();
        if (transform == null) return new AABB();

        Vector2 worldScale = transform.Scale;
        float scaledRadius = Radius * Math.Max(worldScale.X, worldScale.Y);
        float rotationRad = transform.Rotation * MathF.PI / 180.0f;
        Vector2 pos = transform.Position + RotateVector(Offset * worldScale, rotationRad);
        
        return new AABB(pos - new Vector2(scaledRadius), pos + new Vector2(scaledRadius));
    }

    public override Vector2[] GetVertices() => Array.Empty<Vector2>();

    public override float CalculateInertiaCoefficient()
    {
        var transform = Owner.GetComponent<Transform>();
        float r = Radius * (transform != null ? Math.Max(transform.Scale.X, transform.Scale.Y) : 1.0f);
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
    public List<Vector2> Vertices { get; set; } = new()
    {
        new Vector2(0, 0.5f),
        new Vector2(-0.433f, -0.25f),
        new Vector2(0.433f, -0.25f)
    };

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
        if (transform == null || Vertices.Count == 0) return Array.Empty<Vector2>();

        Vector2 worldScale = transform.Scale;
        float rotationRad = transform.Rotation * MathF.PI / 180.0f;
        Vector2 pos = transform.Position;

        // Owner's world matrix = T * R * S
        // Polygon vertex world position = T * R * S * (Vertex_local + Offset)
        Vector2[] result = new Vector2[Vertices.Count];
        for (int i = 0; i < Vertices.Count; i++)
        {
            Vector2 localPos = (Vertices[i] + Offset) * worldScale;
            result[i] = pos + RotateVector(localPos, rotationRad);
        }
        return result;
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
