using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core;
using Verity.Graphics;

namespace Verity.Core.Physics;

public class Fracture : Component
{
    [SerializeField]
    public int FragmentCount { get; set; } = 10;

    [SerializeField]
    public float SizeVariance { get; set; } = 0.5f;

    [SerializeField]
    public bool UsePhysics { get; set; } = true;

    [SerializeField]
    public bool AutoPolygonShape { get; set; } = true;

    [SerializeField]
    public float ExplosionForce { get; set; } = 5.0f;

    [SerializeField]
    public float MassPerArea { get; set; } = 1.0f;

    [SerializeField]
    public float FadeOutDelay { get; set; } = 2.0f;

    [SerializeField]
    public float FadeOutDuration { get; set; } = 1.0f;

    
    [Button("Trigger Fracture")]
    public void Trigger()
    {
        var shape = Owner.GetComponent<PhysicalShape>();
        if (shape == null) return;

        var vertices = shape.GetVertices(); // World space vertices
        if (vertices == null || vertices.Length < 3) return;

        // Calculate center for explosion
        Vector2 center = Vector2.Zero;
        foreach (var v in vertices) center += v;
        center /= vertices.Length;

        // Get color from renderer if exists
        var renderer = Owner.GetComponent<PolygonRenderer>();
        var color = renderer?.Color ?? Verity.Core.Color.White;
        var sortingLayer = renderer?.SortingLayerName ?? "Default";
        var orderInLayer = renderer?.OrderInLayer ?? 0;

        GenerateFragments(vertices, center, color, sortingLayer, orderInLayer);

        // Destroy the original
        Entity.Destroy(Owner);
    }

    private void GenerateFragments(Vector2[] originalVertices, Vector2 center, Verity.Core.Color color, string sortingLayer, int orderInLayer)
    {
        float polygonArea = MathF.Abs(CalculateSignedArea(originalVertices));
        if (polygonArea < 0.0001f)
            return;

        int targetCount = Math.Max(1, FragmentCount);
        List<Vector2> sites = GenerateVoronoiSites(originalVertices, targetCount, center);
        if (sites.Count == 0)
            return;

        foreach (var site in sites)
        {
            List<Vector2> cell = new(originalVertices);

            foreach (var otherSite in sites)
            {
                if (site == otherSite)
                    continue;

                cell = ClipPolygonToVoronoiHalfPlane(cell, site, otherSite);
                if (cell.Count < 3)
                    break;
            }

            if (cell.Count < 3)
                continue;

            float cellArea = MathF.Abs(CalculateSignedArea(cell));
            if (cellArea < 0.0005f)
                continue;

            CreateFragmentEntity(cell.ToArray(), color, sortingLayer, orderInLayer);
        }
    }

    private void CreateFragmentEntity(Vector2[] vertices, Verity.Core.Color color, string sortingLayer, int orderInLayer)
    {
        Vector2 centroid = CalculateCentroid(vertices);
        if (!IsFinite(centroid))
            return;

        List<Vector2> localVertices = new List<Vector2>();
        foreach (var v in vertices)
            localVertices.Add(v - centroid);

        var fragmentEntity = Entity.Instantiate(Owner.Name + "_Fragment");
        fragmentEntity.Transform.SetParent(Owner.Transform.Parent, preserveWorldPosition: false);
        fragmentEntity.Transform.WorldPosition = centroid;
        fragmentEntity.Transform.WorldRotation = 0.0f;
        fragmentEntity.Transform.WorldScale = Vector2.One;

        var polyRenderer = fragmentEntity.AddComponent<PolygonRenderer>();
        polyRenderer.Vertices = localVertices;
        polyRenderer.Color = color;
        polyRenderer.Fill = true;
        polyRenderer.SortingLayerName = sortingLayer;
        polyRenderer.OrderInLayer = orderInLayer;

        if (UsePhysics)
        {
            var physical = fragmentEntity.AddComponent<Physical>();
            float area = MathF.Abs(CalculateSignedArea(vertices));
            physical.Mass = MathF.Max(0.1f, area * MassPerArea);

            if (AutoPolygonShape)
            {
                var polyShape = fragmentEntity.AddComponent<PolygonShape>();
                polyShape.Vertices = localVertices;
            }

            Vector2 direction = centroid - Owner.Transform.WorldPosition;
            if (direction.LengthSquared() < 0.0001f)
                direction = RandomUnitVector();
            else
                direction = Vector2.Normalize(direction);

            physical.Push(direction * ExplosionForce);
        }

        var fragmentScript = fragmentEntity.AddComponent<Fragment>();
        fragmentScript.FadeOutDelay = FadeOutDelay;
        fragmentScript.FadeOutDuration = FadeOutDuration;
    }

    private List<Vector2> GenerateVoronoiSites(Vector2[] polygon, int targetCount, Vector2 fallbackCenter)
    {
        List<Vector2> sites = new(targetCount);
        Vector2 center = IsPointInPolygon(fallbackCenter, polygon) ? fallbackCenter : CalculateCentroid(polygon);
        if (!IsFinite(center) || !IsPointInPolygon(center, polygon))
            center = FindRandomPointInPolygon(polygon);

        sites.Add(center);
        if (targetCount == 1)
            return sites;

        float area = MathF.Abs(CalculateSignedArea(polygon));
        float spacing = MathF.Sqrt(area / targetCount);
        float uniformity = 1.0f - Math.Clamp(SizeVariance, 0.0f, 1.0f);
        float minDistance = spacing * (0.35f + (0.35f * uniformity));

        for (int i = 1; i < targetCount; i++)
        {
            Vector2 bestCandidate = center;
            float bestScore = float.NegativeInfinity;
            int candidateCount = 12 + (int)(uniformity * 20.0f);

            for (int candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                Vector2 candidate = FindRandomPointInPolygon(polygon);
                float nearestDistanceSquared = float.PositiveInfinity;

                foreach (var site in sites)
                    nearestDistanceSquared = MathF.Min(nearestDistanceSquared, Vector2.DistanceSquared(candidate, site));

                float randomness = 1.0f - uniformity;
                float score = nearestDistanceSquared * (1.0f + ((float)Random.Shared.NextDouble() - 0.5f) * randomness);

                if (nearestDistanceSquared < minDistance * minDistance && Random.Shared.NextDouble() < uniformity * 0.9f)
                    continue;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            sites.Add(bestCandidate);
        }

        return sites;
    }

    private static List<Vector2> ClipPolygonToVoronoiHalfPlane(List<Vector2> polygon, Vector2 site, Vector2 otherSite)
    {
        List<Vector2> result = new(polygon.Count + 2);
        if (polygon.Count < 3)
            return result;

        Vector2 normal = otherSite - site;
        if (normal.LengthSquared() < 0.000001f)
            return new List<Vector2>(polygon);

        Vector2 midpoint = (site + otherSite) * 0.5f;

        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            float currentDistance = SignedDistanceToBisector(current, midpoint, normal);
            float nextDistance = SignedDistanceToBisector(next, midpoint, normal);
            bool currentInside = currentDistance <= 0.0001f;
            bool nextInside = nextDistance <= 0.0001f;

            if (currentInside)
                AddUniquePoint(result, current);

            if (currentInside != nextInside)
            {
                float denominator = currentDistance - nextDistance;
                if (MathF.Abs(denominator) > 0.000001f)
                {
                    float t = currentDistance / denominator;
                    Vector2 intersection = current + ((next - current) * t);
                    AddUniquePoint(result, intersection);
                }
            }
        }

        if (result.Count > 1 && Vector2.DistanceSquared(result[0], result[^1]) < 0.000001f)
            result.RemoveAt(result.Count - 1);

        return result;
    }

    private static float SignedDistanceToBisector(Vector2 point, Vector2 midpoint, Vector2 normal)
        => Vector2.Dot(point - midpoint, normal);

    private static void AddUniquePoint(List<Vector2> points, Vector2 point)
    {
        if (points.Count == 0 || Vector2.DistanceSquared(points[^1], point) > 0.000001f)
            points.Add(point);
    }

    private static float CalculateSignedArea(IReadOnlyList<Vector2> vertices)
    {
        if (vertices.Count < 3)
            return 0.0f;

        float area = 0.0f;
        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2 current = vertices[i];
            Vector2 next = vertices[(i + 1) % vertices.Count];
            area += (current.X * next.Y) - (next.X * current.Y);
        }

        return area * 0.5f;
    }

    private static Vector2 CalculateCentroid(IReadOnlyList<Vector2> vertices)
    {
        if (vertices.Count == 0)
            return Vector2.Zero;

        float signedArea = CalculateSignedArea(vertices);
        if (MathF.Abs(signedArea) < 0.000001f)
        {
            Vector2 average = Vector2.Zero;
            foreach (var vertex in vertices)
                average += vertex;
            return average / vertices.Count;
        }

        float cx = 0.0f;
        float cy = 0.0f;

        for (int i = 0; i < vertices.Count; i++)
        {
            Vector2 current = vertices[i];
            Vector2 next = vertices[(i + 1) % vertices.Count];
            float cross = (current.X * next.Y) - (next.X * current.Y);
            cx += (current.X + next.X) * cross;
            cy += (current.Y + next.Y) * cross;
        }

        float factor = 1.0f / (6.0f * signedArea);
        return new Vector2(cx * factor, cy * factor);
    }

    private static Vector2 FindRandomPointInPolygon(Vector2[] polygon)
    {
        GetBounds(polygon, out Vector2 min, out Vector2 max);

        for (int attempt = 0; attempt < 128; attempt++)
        {
            Vector2 point = new(
                Lerp(min.X, max.X, (float)Random.Shared.NextDouble()),
                Lerp(min.Y, max.Y, (float)Random.Shared.NextDouble()));

            if (IsPointInPolygon(point, polygon))
                return point;
        }

        return CalculateCentroid(polygon);
    }

    private static void GetBounds(IReadOnlyList<Vector2> vertices, out Vector2 min, out Vector2 max)
    {
        min = vertices[0];
        max = vertices[0];

        for (int i = 1; i < vertices.Count; i++)
        {
            min = Vector2.Min(min, vertices[i]);
            max = Vector2.Max(max, vertices[i]);
        }
    }

    private static bool IsPointInPolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[j];

            bool intersects = ((a.Y > point.Y) != (b.Y > point.Y)) &&
                              (point.X < ((b.X - a.X) * (point.Y - a.Y) / ((b.Y - a.Y) + 0.000001f)) + a.X);
            if (intersects)
                inside = !inside;
        }

        return inside;
    }

    private static Vector2 RandomUnitVector()
    {
        float angle = (float)(Random.Shared.NextDouble() * Math.PI * 2.0);
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);

    private static float Lerp(float min, float max, float t)
        => min + ((max - min) * t);
}
