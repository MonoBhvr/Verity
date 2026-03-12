using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.Physics;

public class SpatialHashGrid
{
    private readonly float _cellSize;
    private readonly Dictionary<long, List<Physical>> _grid = new();

    public SpatialHashGrid(float cellSize = 2.0f)
    {
        _cellSize = cellSize;
    }

    private long GetKey(int x, int y) => ((long)x << 32) | (uint)y;

    public void Add(Physical physical, List<PhysicalShape> shapes)
    {
        var aabb = GetCombinedAABB(shapes);
        if (aabb.IsDefault()) return;

        int minX = (int)Math.Floor(aabb.Min.X / _cellSize);
        int minY = (int)Math.Floor(aabb.Min.Y / _cellSize);
        int maxX = (int)Math.Floor(aabb.Max.X / _cellSize);
        int maxY = (int)Math.Floor(aabb.Max.Y / _cellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                long key = GetKey(x, y);
                if (!_grid.TryGetValue(key, out var list))
                {
                    list = new List<Physical>();
                    _grid[key] = list;
                }
                list.Add(physical);
            }
        }
    }

    public IEnumerable<Physical> GetPotentialCollisions(Physical physical, List<PhysicalShape> shapes)
    {
        var aabb = GetCombinedAABB(shapes);
        if (aabb.IsDefault()) return Enumerable.Empty<Physical>();

        int minX = (int)Math.Floor(aabb.Min.X / _cellSize);
        int minY = (int)Math.Floor(aabb.Min.Y / _cellSize);
        int maxX = (int)Math.Floor(aabb.Max.X / _cellSize);
        int maxY = (int)Math.Floor(aabb.Max.Y / _cellSize);

        var potentials = new HashSet<Physical>();
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                long key = GetKey(x, y);
                if (_grid.TryGetValue(key, out var list))
                {
                    foreach (var other in list)
                    {
                        if (other != physical) potentials.Add(other);
                    }
                }
            }
        }
        return potentials;
    }

    private AABB GetCombinedAABB(List<PhysicalShape> shapes)
    {
        if (shapes == null || shapes.Count == 0) return new AABB(Vector2.Zero, Vector2.Zero);
        
        Vector2 min = new Vector2(float.MaxValue);
        Vector2 max = new Vector2(float.MinValue);
        bool any = false;

        foreach (var shape in shapes)
        {
            var aabb = shape.GetAABB();
            min = Vector2.Min(min, aabb.Min);
            max = Vector2.Max(max, aabb.Max);
            any = true;
        }

        return any ? new AABB(min, max) : new AABB(Vector2.Zero, Vector2.Zero);
    }

    public void Clear() => _grid.Clear();
}
