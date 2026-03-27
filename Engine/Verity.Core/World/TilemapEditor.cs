using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.World;

/// <summary>
/// 타일맵 편집을 위한 각종 그리기 도구 로직을 제공하는 클래스
/// </summary>
public static class TilemapEditor
{
    public enum Tool { Brush, Eraser, BoxFill, FloodFill, Picker }
    public enum BrushShape { Rectangle, Circle }

    public static void Paint(Tilemap tilemap, int x, int y, TileBase? tile)
    {
        tilemap.SetTile(x, y, tile);
        NotifyShapeUpdate(tilemap);
    }

    public static void Eraser(Tilemap tilemap, int x, int y)
    {
        tilemap.SetTile(x, y, null);
        NotifyShapeUpdate(tilemap);
    }

    public static void PaintBrush(Tilemap tilemap, int centerX, int centerY, TileBase? tile, int size, BrushShape shape)
    {
        foreach (var (x, y) in GetBrushCells(centerX, centerY, size, shape))
        {
            tilemap.SetTile(x, y, tile);
        }
        NotifyShapeUpdate(tilemap);
    }

    public static void EraseBrush(Tilemap tilemap, int centerX, int centerY, int size, BrushShape shape)
    {
        foreach (var (x, y) in GetBrushCells(centerX, centerY, size, shape))
        {
            tilemap.SetTile(x, y, null);
        }
        NotifyShapeUpdate(tilemap);
    }

    public static void BoxFill(Tilemap tilemap, int startX, int startY, int endX, int endY, TileBase? tile)
    {
        int minX = Math.Min(startX, endX);
        int maxX = Math.Max(startX, endX);
        int minY = Math.Min(startY, endY);
        int maxY = Math.Max(startY, endY);

        long count = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (count > 2500)
        {
            Verity.Core.Debug.LogWarning($"[TilemapEditor] BoxFill cancelled: Area is too large ({count} tiles). Limit is 2500.");
            return;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                tilemap.SetTile(x, y, tile);
            }
        }
        NotifyShapeUpdate(tilemap);
    }

    public static void FloodFill(Tilemap tilemap, int x, int y, TileBase? newTile)
    {
        TileBase? targetTile = tilemap.GetTile(x, y);
        if (targetTile == newTile) return;

        // "Closed area" check: if filling empty space, we must ensure it doesn't leak to infinity.
        // We use a Breadth-First Search to find all connected tiles of the same type.
        // If the number of tiles exceeds a certain limit (e.g., 1000), we assume it's an "open" space.
        
        List<(int x, int y)> toFill = new();
        Queue<(int x, int y)> queue = new();
        queue.Enqueue((x, y));
        
        HashSet<(int x, int y)> visited = new();
        visited.Add((x, y));

        const int MaxTiles = 2000;

        while (queue.Count > 0)
        {
            if (toFill.Count > MaxTiles)
            {
                // Area is too large (likely open background)
                Verity.Core.Debug.LogWarning("[TilemapEditor] FloodFill cancelled: Area is too large or not closed.");
                return;
            }

            var (currX, currY) = queue.Dequeue();
            toFill.Add((currX, currY));

            // 4-way adjacent
            (int dx, int dy)[] neighbors = { (1, 0), (-1, 0), (0, 1), (0, -1) };
            foreach (var (dx, dy) in neighbors)
            {
                int nx = currX + dx;
                int ny = currY + dy;
                
                if (!visited.Contains((nx, ny)) && tilemap.GetTile(nx, ny) == targetTile)
                {
                    visited.Add((nx, ny));
                    queue.Enqueue((nx, ny));
                }
            }
        }

        // Only fill if bounded
        foreach (var pos in toFill)
        {
            tilemap.SetTile(pos.x, pos.y, newTile);
        }
        
        NotifyShapeUpdate(tilemap);
    }

    public static TileBase? Picker(Tilemap tilemap, int x, int y)
    {
        return tilemap.GetTile(x, y);
    }

    public static List<(int x, int y)> GetBrushCells(int centerX, int centerY, int size, BrushShape shape)
    {
        size = Math.Max(1, size);

        int minOffset = -((size - 1) / 2);
        int maxOffset = size / 2;
        float centerOffset = (minOffset + maxOffset) * 0.5f;
        float radius = Math.Max(0.5f, size * 0.5f);
        float radiusSq = radius * radius;

        var cells = new List<(int x, int y)>(size * size);
        for (int y = minOffset; y <= maxOffset; y++)
        {
            for (int x = minOffset; x <= maxOffset; x++)
            {
                if (shape == BrushShape.Circle)
                {
                    float dx = x - centerOffset;
                    float dy = y - centerOffset;
                    if ((dx * dx) + (dy * dy) > radiusSq)
                    {
                        continue;
                    }
                }

                cells.Add((centerX + x, centerY + y));
            }
        }

        return cells;
    }

    private static void NotifyShapeUpdate(Tilemap tilemap)
    {
        // Dirty flags are updated inside Tilemap.SetTile/Clear.
    }
}
