using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.Physics;
using Verity.Core;

namespace Verity.Graphics.Physics;

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

        // Generate fragments
        // Simplified approach: Triangulate from a few random points within the polygon
        GenerateFragments(vertices, center, color, sortingLayer, orderInLayer);

        // Destroy the original
        Entity.Destroy(Owner);
    }

    private void GenerateFragments(Vector2[] originalVertices, Vector2 center, Verity.Core.Color color, string sortingLayer, int orderInLayer)
    {
        // Simple fragmentation: create triangles from center to each edge
        // More advanced: create random sites and split edges
        
        int count = originalVertices.Length;
        for (int i = 0; i < count; i++)
        {
            Vector2 v1 = originalVertices[i];
            Vector2 v2 = originalVertices[(i + 1) % count];

            // Create a fragment entity for this triangle (center, v1, v2)
            CreateFragmentEntity(new Vector2[] { center, v1, v2 }, color, sortingLayer, orderInLayer);
        }

        // If we want more fragments, we could split the edges further or add internal points.
        // For a basic implementation, this provides a radial fracture.
    }

    private void CreateFragmentEntity(Vector2[] vertices, Verity.Core.Color color, string sortingLayer, int orderInLayer)
    {
        // Calculate fragment center (centroid)
        Vector2 centroid = Vector2.Zero;
        foreach (var v in vertices) centroid += v;
        centroid /= vertices.Length;

        // Localize vertices relative to centroid
        List<Vector2> localVertices = new List<Vector2>();
        foreach (var v in vertices) localVertices.Add(v - centroid);

        var fragmentEntity = Entity.Instantiate(Owner.Name + "_Fragment");
        fragmentEntity.Transform.Position = centroid;
        fragmentEntity.Transform.Rotation = Owner.Transform.Rotation;

        // Add Renderer
        var polyRenderer = fragmentEntity.AddComponent<PolygonRenderer>();
        polyRenderer.Vertices = localVertices;
        polyRenderer.Color = color;
        polyRenderer.Fill = true;
        polyRenderer.SortingLayerName = sortingLayer;
        polyRenderer.OrderInLayer = orderInLayer;

        // Add Physics
        if (UsePhysics)
        {
            var physical = fragmentEntity.AddComponent<Physical>();
            
            // Calculate area for mass (approximate for triangle)
            float area = MathF.Abs((vertices[0].X * (vertices[1].Y - vertices[2].Y) + 
                                    vertices[1].X * (vertices[2].Y - vertices[0].Y) + 
                                    vertices[2].X * (vertices[0].Y - vertices[1].Y)) / 2.0f);
            physical.Mass = MathF.Max(0.1f, area * MassPerArea);

            if (AutoPolygonShape)
            {
                var polyShape = fragmentEntity.AddComponent<PolygonShape>();
                polyShape.Vertices = localVertices;
            }

            // Explosion Force
            Vector2 direction = Vector2.Normalize(centroid - Owner.Transform.Position);
            if (direction == Vector2.Zero) direction = new Vector2((float)Random.Shared.NextDouble() - 0.5f, (float)Random.Shared.NextDouble() - 0.5f);
            physical.Push(direction * ExplosionForce);
        }

        // Add Fragment Script for FadeOut/Destruction
        var fragmentScript = fragmentEntity.AddComponent<Fragment>();
        fragmentScript.FadeOutDelay = FadeOutDelay;
        fragmentScript.FadeOutDuration = FadeOutDuration;
    }
}
