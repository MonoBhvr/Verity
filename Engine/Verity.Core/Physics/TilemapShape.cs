using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Core.Physics;

/// <summary>
/// Tilemap의 타일들을 기반으로 충돌체를 생성하는 컴포넌트
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class TilemapShape : PhysicalShape
{
    private Tilemap? _tilemap;
    private List<AABB> _mergedBoxes = new();

    protected override void OnEnable()
    {
        _tilemap = Owner.GetComponent<Tilemap>();
        if (_tilemap != null)
        {
            // Tile collision data is derived runtime state and must be rebuilt after scene load.
            _tilemap.PhysicsDirty = true;
        }
    }

    private void RebuildShapes()
    {
        if (_tilemap == null) _tilemap = Owner.GetComponent<Tilemap>();
        if (_tilemap == null) return;

        _mergedBoxes.Clear();
        
        var collidableCells = new HashSet<(int x, int y)>();
        bool hasCollidable = false;
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        foreach (var pair in _tilemap.GetAllTiles())
        {
            if (!pair.Value.IsCollidable) continue;

            collidableCells.Add(pair.Key);
            if (!hasCollidable)
            {
                minX = maxX = pair.Key.x;
                minY = maxY = pair.Key.y;
                hasCollidable = true;
            }
            else
            {
                minX = Math.Min(minX, pair.Key.x);
                minY = Math.Min(minY, pair.Key.y);
                maxX = Math.Max(maxX, pair.Key.x);
                maxY = Math.Max(maxY, pair.Key.y);
            }
        }

        if (!hasCollidable) 
        {
            _tilemap.PhysicsDirty = false;
            return;
        }

        var visited = new HashSet<(int x, int y)>();
        float tileWidth = MathF.Max(0.0001f, MathF.Abs(_tilemap.TileSize.X));
        float tileHeight = MathF.Max(0.0001f, MathF.Abs(_tilemap.TileSize.Y));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (!collidableCells.Contains((x, y)) || visited.Contains((x, y))) continue;

                // Expand width
                int width = 1;
                while (collidableCells.Contains((x + width, y)) && !visited.Contains((x + width, y))) width++;

                // Expand height
                int height = 1;
                while (true)
                {
                    bool canExtendHeight = true;
                    for (int k = 0; k < width; k++)
                    {
                        if (!collidableCells.Contains((x + k, y + height)) || visited.Contains((x + k, y + height)))
                        {
                            canExtendHeight = false;
                            break;
                        }
                    }
                    if (!canExtendHeight) break;
                    height++;
                }

                // Mark as visited
                for (int i = 0; i < width; i++)
                    for (int j = 0; j < height; j++)
                        visited.Add((x + i, y + j));

                Vector2 minPos = new Vector2(x * tileWidth, y * tileHeight);
                Vector2 maxPos = minPos + new Vector2(width * tileWidth, height * tileHeight);
                _mergedBoxes.Add(new AABB(minPos, maxPos));
            }
        }

        _tilemap.PhysicsDirty = false;
    }

    private void EnsureShapesReady()
    {
        if (_tilemap == null)
        {
            _tilemap = Owner.GetComponent<Tilemap>();
            if (_tilemap != null)
            {
                _tilemap.PhysicsDirty = true;
            }
        }

        if (_tilemap != null && (_tilemap.PhysicsDirty || _mergedBoxes.Count == 0))
        {
            RebuildShapes();
        }
    }

    public override AABB GetAABB()
    {
        EnsureShapesReady();
        if (_mergedBoxes.Count == 0) return new AABB();

        Vector2 min = new Vector2(float.MaxValue);
        Vector2 max = new Vector2(float.MinValue);
        
        var worldMatrix = Owner.Transform.GetWorldMatrix();

        foreach (var b in _mergedBoxes)
        {
            // Transform each local box corner to world space to find the global AABB
            Vector2[] corners = {
                b.Min,
                new Vector2(b.Max.X, b.Min.Y),
                b.Max,
                new Vector2(b.Min.X, b.Max.Y)
            };

            foreach (var c in corners)
            {
                var wp3 = Vector3.Transform(new Vector3(c, 0), worldMatrix);
                Vector2 wp = new Vector2(wp3.X, wp3.Y);
                min = Vector2.Min(min, wp);
                max = Vector2.Max(max, wp);
            }
        }

        return new AABB(min, max);
    }

    public override Vector2[] GetVertices() => Array.Empty<Vector2>();

    /// <summary>
    /// Returns each merged box as a world-space polygon (4 vertices).
    /// </summary>
    public List<Vector2[]> GetWorldPolygons()
    {
        EnsureShapesReady();
        
        var worldMatrix = Owner.Transform.GetWorldMatrix();
        var polygons = new List<Vector2[]>();

        foreach (var b in _mergedBoxes)
        {
            Vector2[] corners = {
                b.Min,
                new Vector2(b.Max.X, b.Min.Y),
                b.Max,
                new Vector2(b.Min.X, b.Max.Y)
            };

            Vector2[] worldCorners = new Vector2[4];
            for (int i = 0; i < 4; i++)
            {
                var wp3 = Vector3.Transform(new Vector3(corners[i], 0), worldMatrix);
                worldCorners[i] = new Vector2(wp3.X, wp3.Y);
            }
            polygons.Add(worldCorners);
        }
        return polygons;
    }

    public override float CalculateInertiaCoefficient() => 1.0f;

    public void DrawGizmos(Verity.Core.Color color)
    {
        foreach (var poly in GetWorldPolygons())
        {
            for (int i = 0; i < poly.Length; i++)
            {
                Verity.Core.Debug.DrawLine(poly[i], poly[(i + 1) % poly.Length], color, 0.02f);
            }
        }
    }
}
