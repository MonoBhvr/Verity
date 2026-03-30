using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.Physics;

public static class PhysicsMath
{
    public struct CollisionResult
    {
        public bool IsColliding;
        public Vector2 Normal;
        public float Depth;
        public List<Vector2> Contacts;
    }

    public struct RaycastHit
    {
        public bool IsHit;
        public Entity Entity;
        public Vector2 Point;
        public Vector2 Normal;
        public float Distance;
    }

    public static CollisionResult TestSAT(PhysicalShape shapeA, PhysicalShape shapeB)
    {
        CollisionResult res = new() { Contacts = new List<Vector2>() };

        if (shapeA is CircleShape circleA && shapeB is CircleShape circleB)
        {
            res = TestCircleVsCircle(circleA, circleB);
        }
        else if (shapeA is CircleShape c && shapeB is not CircleShape)
        {
            res = TestCircleVsPolygon(c, shapeB.GetVertices());
        }
        else if (shapeB is CircleShape c2 && shapeA is not CircleShape)
        {
            res = TestCircleVsPolygon(c2, shapeA.GetVertices());
            if (res.IsColliding) res.Normal = -res.Normal;
        }
        else
        {
            res = TestPolygonVsPolygon(shapeA.GetVertices(), shapeB.GetVertices());
        }

        return res;
    }

    public static CollisionResult TestSAT(Vector2[] verticesA, Vector2[] verticesB)
    {
        return TestPolygonVsPolygon(verticesA, verticesB);
    }

    public static CollisionResult TestSAT(CircleShape circle, Vector2[] polygonVertices)
    {
        return TestCircleVsPolygon(circle, polygonVertices);
    }

    public static CollisionResult TestSAT(AABB box, CircleShape circle)
    {
        Vector2 center = circle.GetWorldCenter();
        Vector2 scale = circle.GetBaseScale();
        float radius = circle.Radius * Math.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y));

        Vector2 closest = System.Numerics.Vector2.Clamp(center, box.Min, box.Max);
        float distance = Vector2.Distance(center, closest);

        if (distance >= radius) return new CollisionResult { IsColliding = false };

        Vector2 normal = distance > 0.0001f ? Vector2.Normalize(center - closest) : new Vector2(0, 1);
        return new CollisionResult { IsColliding = true, Normal = normal, Depth = radius - distance, Contacts = new List<Vector2> { closest } };
    }

    public static CollisionResult TestSAT(AABB box, Vector2[] vertices)
    {
        // Convert AABB to vertices and use Polygon vs Polygon
        Vector2[] boxVertices = new[] {
            box.Min,
            new Vector2(box.Max.X, box.Min.Y),
            box.Max,
            new Vector2(box.Min.X, box.Max.Y)
        };
        return TestPolygonVsPolygon(boxVertices, vertices);
    }

    public static RaycastHit TestRay(Vector2 origin, Vector2 direction, float distance, PhysicalShape shape)
    {
        if (shape is CircleShape circle)
        {
            return TestRayVsCircle(origin, direction, distance, circle);
        }

        if (shape is TilemapShape tilemapShape)
        {
            RaycastHit closestHit = new() { IsHit = false, Distance = float.MaxValue };
            foreach (var polygon in tilemapShape.GetWorldPolygons())
            {
                var hit = TestRayVsPolygon(origin, direction, distance, shape.Owner, polygon);
                if (hit.IsHit && hit.Distance < closestHit.Distance)
                {
                    closestHit = hit;
                }
            }

            return closestHit.IsHit ? closestHit : new RaycastHit { IsHit = false };
        }

        return TestRayVsPolygon(origin, direction, distance, shape.Owner, shape.GetVertices());
    }

    private static CollisionResult TestCircleVsCircle(CircleShape a, CircleShape b)
    {
        Vector2 posA = a.GetWorldCenter();
        Vector2 posB = b.GetWorldCenter();
        float dist = Vector2.Distance(posA, posB);
        
        Vector2 scaleA = a.GetBaseScale();
        Vector2 scaleB = b.GetBaseScale();
        float radiusA = a.Radius * Math.Max(MathF.Abs(scaleA.X), MathF.Abs(scaleA.Y));
        float radiusB = b.Radius * Math.Max(MathF.Abs(scaleB.X), MathF.Abs(scaleB.Y));
        float radiusSum = radiusA + radiusB;

        if (dist >= radiusSum) return new CollisionResult { IsColliding = false };

        Vector2 normal = dist > 0.0001f ? Vector2.Normalize(posB - posA) : new Vector2(0, 1);
        Vector2 contact = posA + normal * radiusA;
        return new CollisionResult { IsColliding = true, Normal = normal, Depth = radiusSum - dist, Contacts = new List<Vector2> { contact } };
    }

    private static CollisionResult TestCircleVsPolygon(CircleShape circle, Vector2[] vertices)
    {
        if (vertices == null || vertices.Length == 0) return new CollisionResult { IsColliding = false };

        Vector2 center = circle.GetWorldCenter();
        Vector2 scale = circle.GetBaseScale();
        float radius = circle.Radius * Math.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y));
        
        float minDepth = float.MaxValue;
        Vector2 minNormal = Vector2.Zero;

        // 1. Test polygon face normals
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 p1 = vertices[i], p2 = vertices[(i + 1) % vertices.Length];
            Vector2 edge = p2 - p1;
            Vector2 axis = Vector2.Normalize(new Vector2(-edge.Y, edge.X));

            var projPoly = Project(vertices, axis);
            var projCircle = ProjectCircle(center, radius, axis);

            if (!Overlaps(projPoly, projCircle)) return new CollisionResult { IsColliding = false };

            float depth = GetOverlapDepth(projPoly, projCircle);
            if (depth < minDepth) {
                minDepth = depth;
                float polyMid = (projPoly.Min + projPoly.Max) * 0.5f;
                float circleMid = (projCircle.Min + projCircle.Max) * 0.5f;
                minNormal = (polyMid < circleMid) ? -axis : axis;
            }
        }

        // 2. Test axis from closest vertex to circle center
        Vector2 closestVertexAxis = GetClosestPointAxis(center, vertices);
        if (closestVertexAxis != Vector2.Zero)
        {
            var projPoly = Project(vertices, closestVertexAxis);
            var projCircle = ProjectCircle(center, radius, closestVertexAxis);

            if (!Overlaps(projPoly, projCircle)) return new CollisionResult { IsColliding = false };

            float depth = GetOverlapDepth(projPoly, projCircle);
            if (depth < minDepth) {
                minDepth = depth;
                float polyMid = (projPoly.Min + projPoly.Max) * 0.5f;
                float circleMid = (projCircle.Min + projCircle.Max) * 0.5f;
                minNormal = (polyMid < circleMid) ? -closestVertexAxis : closestVertexAxis;
            }
        }

        Vector2 contact = center + minNormal * radius;
        return new CollisionResult { IsColliding = true, Normal = minNormal, Depth = minDepth, Contacts = new List<Vector2> { contact } };
    }

    private static CollisionResult TestPolygonVsPolygon(Vector2[] verticesA, Vector2[] verticesB)
    {
        if (verticesA == null || verticesA.Length == 0 || verticesB == null || verticesB.Length == 0) return new CollisionResult { IsColliding = false };

        float minDepth = float.MaxValue;
        Vector2 minNormal = Vector2.Zero;

        var axes = GetAxes(verticesA).Concat(GetAxes(verticesB));
        foreach (var axis in axes)
        {
            var projA = Project(verticesA, axis);
            var projB = Project(verticesB, axis);

            if (!Overlaps(projA, projB)) return new CollisionResult { IsColliding = false };

            float depth = GetOverlapDepth(projA, projB);
            if (depth < minDepth) {
                minDepth = depth;
                float midA = (projA.Min + projA.Max) * 0.5f;
                float midB = (projB.Min + projB.Max) * 0.5f;
                minNormal = (midB < midA) ? -axis : axis;
            }
        }

        const float snapThreshold = 0.01f;
        if (MathF.Abs(minNormal.X) < snapThreshold) minNormal = new Vector2(0, MathF.Sign(minNormal.Y));
        else if (MathF.Abs(minNormal.Y) < snapThreshold) minNormal = new Vector2(MathF.Sign(minNormal.X), 0);
        else minNormal = Vector2.Normalize(minNormal);

        return new CollisionResult { IsColliding = true, Normal = minNormal, Depth = minDepth, Contacts = FindContactPoints(verticesA, verticesB) };
    }

    private static RaycastHit TestRayVsCircle(Vector2 origin, Vector2 direction, float distance, CircleShape circle)
    {
        Vector2 center = circle.GetWorldCenter();
        Vector2 scale = circle.GetBaseScale();
        float radius = circle.Radius * Math.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y));
        Vector2 L = center - origin;
        float tca = Vector2.Dot(L, direction);
        float d2 = Vector2.Dot(L, L) - tca * tca;
        if (d2 > radius * radius) return new RaycastHit { IsHit = false };
        float thc = MathF.Sqrt(radius * radius - d2);
        float t0 = tca - thc, t1 = tca + thc;
        if (t0 > distance || (t0 < 0 && t1 < 0)) return new RaycastHit { IsHit = false };
        float t = t0 < 0 ? t1 : t0;
        if (t > distance) return new RaycastHit { IsHit = false };
        Vector2 hitPoint = origin + direction * t;
        return new RaycastHit { IsHit = true, Entity = circle.Owner, Point = hitPoint, Normal = Vector2.Normalize(hitPoint - center), Distance = t };
    }

    private static RaycastHit TestRayVsPolygon(Vector2 origin, Vector2 direction, float distance, Entity entity, Vector2[] vertices)
    {
        float minT = float.MaxValue; Vector2 minNormal = Vector2.Zero; bool hit = false;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector2 p1 = vertices[i], p2 = vertices[(i + 1) % vertices.Length];
            if (RayVsSegment(origin, direction, p1, p2, out float t, out Vector2 normal))
            {
                if (t >= 0 && t <= distance && t < minT) { minT = t; minNormal = normal; hit = true; }
            }
        }
        if (!hit) return new RaycastHit { IsHit = false };
        return new RaycastHit { IsHit = true, Entity = entity, Point = origin + direction * minT, Normal = minNormal, Distance = minT };
    }

    private static bool RayVsSegment(Vector2 origin, Vector2 direction, Vector2 p1, Vector2 p2, out float t, out Vector2 normal)
    {
        t = 0; normal = Vector2.Zero;
        Vector2 v1 = origin - p1, v2 = p2 - p1, v3 = new Vector2(-direction.Y, direction.X);
        float dot = Vector2.Dot(v2, v3);
        if (Math.Abs(dot) < 0.000001f) return false;
        t = (v2.X * v1.Y - v2.Y * v1.X) / dot;
        float u = Vector2.Dot(v1, v3) / dot;
        if (t >= 0 && u >= 0 && u <= 1)
        {
            Vector2 edge = p2 - p1;
            if (edge.LengthSquared() < 0.000001f) return false;
            normal = Vector2.Normalize(new Vector2(-edge.Y, edge.X));
            if (Vector2.Dot(normal, direction) > 0) normal = -normal;
            return true;
        }
        return false;
    }

    private static List<Vector2> FindContactPoints(Vector2[] vA, Vector2[] vB)
    {
        var contacts = new List<Vector2>();
        const float slop = 0.01f, epsilon = 0.005f;
        foreach (var p in vA) if (IsPointInPolygon(p, vB, epsilon)) contacts.Add(p);
        foreach (var p in vB) if (IsPointInPolygon(p, vA, epsilon)) if (!contacts.Any(c => Vector2.DistanceSquared(c, p) < slop * slop)) contacts.Add(p);
        if (contacts.Count == 0) contacts.Add(GetAverage(vA));
        return contacts;
    }

    private static bool IsPointInPolygon(Vector2 p, Vector2[] poly, float epsilon = 0f)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            float div = (poly[j].Y - poly[i].Y);
            if (((poly[i].Y > p.Y) != (poly[j].Y > p.Y)) && (MathF.Abs(div) > 0.000001f && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / div + poly[i].X)) inside = !inside;
            if (epsilon > 0)
            {
                Vector2 edge = poly[i] - poly[j], toPoint = p - poly[j];
                float edgeLenSq = edge.LengthSquared();
                if (edgeLenSq > 0.000001f)
                {
                    float projection = Vector2.Dot(toPoint, edge) / edgeLenSq;
                    if (projection >= 0 && projection <= 1)
                    {
                        Vector2 closest = poly[j] + edge * projection;
                        if (Vector2.DistanceSquared(p, closest) < epsilon * epsilon) return true;
                    }
                }
            }
        }
        return inside;
    }

    private static Vector2 GetAverage(Vector2[] v) { if (v.Length == 0) return Vector2.Zero; Vector2 sum = Vector2.Zero; foreach (var p in v) sum += p; return sum / (float)v.Length; }
    private static Vector2 GetClosestPointAxis(Vector2 center, Vector2[] vertices) { 
        if (vertices == null || vertices.Length == 0) return Vector2.Zero; 
        Vector2 closest = vertices[0]; float minDist = Vector2.DistanceSquared(center, vertices[0]); 
        for (int i = 1; i < vertices.Length; i++) { float d = Vector2.DistanceSquared(center, vertices[i]); if (d < minDist) { minDist = d; closest = vertices[i]; } } 
        if (minDist < 0.000001f) return Vector2.Zero;
        return Vector2.Normalize(closest - center); 
    }
    private static IEnumerable<Vector2> GetAxes(Vector2[] vertices) { for (int i = 0; i < vertices.Length; i++) { Vector2 edge = vertices[(i + 1) % vertices.Length] - vertices[i]; yield return Vector2.Normalize(new Vector2(-edge.Y, edge.X)); } }
    private static (float Min, float Max) Project(Vector2[] vertices, Vector2 axis) { float min = Vector2.Dot(vertices[0], axis), max = min; for (int i = 1; i < vertices.Length; i++) { float p = Vector2.Dot(vertices[i], axis); min = Math.Min(min, p); max = Math.Max(max, p); } return (min, max); }
    private static (float Min, float Max) ProjectCircle(Vector2 center, float radius, Vector2 axis) { float p = Vector2.Dot(center, axis); return (p - radius, p + radius); }
    private static bool Overlaps((float Min, float Max) a, (float Min, float Max) b) => a.Max > b.Min && b.Max > a.Min;
    private static float GetOverlapDepth((float Min, float Max) a, (float Min, float Max) b) => Math.Min(a.Max - b.Min, b.Max - a.Min);
}
