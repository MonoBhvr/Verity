using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.World;

/// <summary>
/// 격자 기반의 타일 데이터를 저장하고 관리하는 컴포넌트
/// </summary>
public class Tilemap : Component
{
    private Dictionary<(int x, int y), TileBase> _tiles = new();
    private bool _renderDirty = true;
    private bool _physicsDirty = true;
    private bool _boundsDirty = true;
    private bool _hasTileBounds;
    private (int x, int y) _minTile;
    private (int x, int y) _maxTile;

    [SerializeField]
    public Dictionary<(int x, int y), TileBase> Tiles 
    { 
        get => _tiles; 
        set 
        { 
            _tiles = value ?? new(); 
            _hasTileBounds = false;
            _boundsDirty = true;
            _renderDirty = true; 
            _physicsDirty = true; 
        }
    }

    public bool RenderDirty { get => _renderDirty; set => _renderDirty = value; }
    public bool PhysicsDirty { get => _physicsDirty; set => _physicsDirty = value; }

    [SerializeField]
    public Vector2 TileSize { get; set; } = Vector2.One;

    public void SetTile(int x, int y, TileBase? tile)
    {
        var key = (x, y);
        if (tile == null)
        {
            if (_tiles.Remove(key) && _hasTileBounds && (key == _minTile || key == _maxTile || x == _minTile.x || x == _maxTile.x || y == _minTile.y || y == _maxTile.y))
            {
                _boundsDirty = true;
            }
        }
        else
        {
            _tiles[key] = tile;
            IncludeInBounds(x, y);
        }

        _renderDirty = true;
        _physicsDirty = true;
    }

    public TileBase? GetTile(int x, int y)
    {
        if (_tiles.TryGetValue((x, y), out var tile)) return tile;
        return null;
    }

    public bool HasTile(int x, int y) => _tiles.ContainsKey((x, y));

    public void Clear() 
    { 
        _tiles.Clear(); 
        _hasTileBounds = false;
        _boundsDirty = false;
        _renderDirty = true; 
        _physicsDirty = true; 
    }

    public IEnumerable<KeyValuePair<(int x, int y), TileBase>> GetAllTiles() => _tiles;

    public IEnumerable<KeyValuePair<(int x, int y), TileBase>> GetTilesInRegion(int minX, int minY, int maxX, int maxY)
    {
        if (_tiles.Count == 0 || minX > maxX || minY > maxY) yield break;

        long area = (long)(maxX - minX + 1) * (maxY - minY + 1);
        if (area > _tiles.Count * 2L)
        {
            foreach (var pair in _tiles)
            {
                var (x, y) = pair.Key;
                if (x >= minX && x <= maxX && y >= minY && y <= maxY)
                {
                    yield return pair;
                }
            }
            yield break;
        }

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (_tiles.TryGetValue((x, y), out var tile))
                {
                    yield return new KeyValuePair<(int x, int y), TileBase>((x, y), tile);
                }
            }
        }
    }

    public bool TryGetTileBounds(out int minX, out int minY, out int maxX, out int maxY)
    {
        RefreshBoundsIfNeeded();
        if (!_hasTileBounds)
        {
            minX = minY = maxX = maxY = 0;
            return false;
        }

        minX = _minTile.x;
        minY = _minTile.y;
        maxX = _maxTile.x;
        maxY = _maxTile.y;
        return true;
    }

    // --- Coordinate Transformations ---

    /// <summary>
    /// 월드 좌표를 타일맵의 그리드 좌표(Cell)로 변환합니다.
    /// </summary>
    public (int x, int y) WorldToCell(Vector2 worldPos)
    {
        var transform = Owner?.Transform;
        Vector2 localPos;
        
        if (transform == null)
        {
            localPos = worldPos;
        }
        else
        {
            if (Matrix4x4.Invert(transform.GetWorldMatrix(), out var invWorld))
            {
                var localPos3 = Vector3.Transform(new Vector3(worldPos, 0), invWorld);
                localPos = new Vector2(localPos3.X, localPos3.Y);
            }
            else
            {
                localPos = worldPos - transform.WorldPosition;
            }
        }

        float tx = MathF.Max(0.0001f, MathF.Abs(TileSize.X));
        float ty = MathF.Max(0.0001f, MathF.Abs(TileSize.Y));

        int x = (int)MathF.Floor(localPos.X / tx);
        int y = (int)MathF.Floor(localPos.Y / ty);
        return (x, y);
    }

    /// <summary>
    /// 타일맵의 그리드 좌표(Cell)를 월드 좌표로 변환합니다.
    /// </summary>
    public Vector2 CellToWorld(int x, int y)
    {
        var transform = Owner?.Transform;
        Vector2 localPos = new Vector2(x * TileSize.X, y * TileSize.Y);
        
        if (transform == null) return localPos;
        
        var worldMatrix = transform.GetWorldMatrix();
        var worldPos3 = Vector3.Transform(new Vector3(localPos, 0), worldMatrix);
        return new Vector2(worldPos3.X, worldPos3.Y);
    }

    /// <summary>
    /// 타일맵의 그리드 좌표(Cell)의 중심 월드 좌표를 반환합니다.
    /// </summary>
    public Vector2 GetCellCenterWorld(int x, int y)
    {
        var transform = Owner?.Transform;
        Vector2 localCenter = new Vector2((x + 0.5f) * TileSize.X, (y + 0.5f) * TileSize.Y);

        if (transform == null) return localCenter;

        var worldPos3 = Vector3.Transform(new Vector3(localCenter, 0), transform.GetWorldMatrix());
        return new Vector2(worldPos3.X, worldPos3.Y);
    }

    private void IncludeInBounds(int x, int y)
    {
        if (_boundsDirty)
        {
            return;
        }

        if (!_hasTileBounds)
        {
            _minTile = (x, y);
            _maxTile = (x, y);
            _hasTileBounds = true;
            return;
        }

        _minTile = (Math.Min(_minTile.x, x), Math.Min(_minTile.y, y));
        _maxTile = (Math.Max(_maxTile.x, x), Math.Max(_maxTile.y, y));
    }

    private void RefreshBoundsIfNeeded()
    {
        if (!_boundsDirty) return;

        if (_tiles.Count == 0)
        {
            _hasTileBounds = false;
            _boundsDirty = false;
            return;
        }

        bool first = true;
        int minX = 0, minY = 0, maxX = 0, maxY = 0;
        foreach (var key in _tiles.Keys)
        {
            if (first)
            {
                minX = maxX = key.x;
                minY = maxY = key.y;
                first = false;
                continue;
            }

            minX = Math.Min(minX, key.x);
            minY = Math.Min(minY, key.y);
            maxX = Math.Max(maxX, key.x);
            maxY = Math.Max(maxY, key.y);
        }

        _minTile = (minX, minY);
        _maxTile = (maxX, maxY);
        _hasTileBounds = true;
        _boundsDirty = false;
    }
}
