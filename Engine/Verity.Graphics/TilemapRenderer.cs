using System.Numerics;
using SystemNumericsVector3 = System.Numerics.Vector3;
using Irodori.Framebuffer;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core;

namespace Verity.Graphics;

[RequireComponent(typeof(Tilemap))]
public class TilemapRenderer : Component
{
    private Tilemap? _tilemap;
    private readonly Dictionary<string, RenderTexture> _textureCache = new();
    
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

    public int ResolvedLayerIndex => SortingLayer.GetLayerIndex(SortingLayerName);

    protected override void OnEnable()
    {
        _tilemap = Owner.GetComponent<Tilemap>();
    }

    public void ClearTextureCache()
    {
        _textureCache.Clear();
    }

    public void Render(RenderPipeline pipeline, Camera camera, Matrix4x4 projection, Matrix4x4 view, RenderTarget? targetFbo)
    {
        if (camera == null || Owner == null) return;
        
        if (_tilemap == null) _tilemap = Owner.GetComponent<Tilemap>();
        if (_tilemap == null) return;
        if (!_tilemap.TryGetTileBounds(out int tileMinX, out int tileMinY, out int tileMaxX, out int tileMaxY)) return;

        float tileWidth = MathF.Max(0.0001f, MathF.Abs(_tilemap.TileSize.X));
        float tileHeight = MathF.Max(0.0001f, MathF.Abs(_tilemap.TileSize.Y));

        // 1. Calculate visible range (Culling) in Local Space
        float hH = camera.VisibleHalfHeight;
        float hW = camera.VisibleHalfWidth;
        
        Vector2 camPos = camera.Owner != null ? camera.Owner.Transform.WorldPosition : camera.Position;

        // Camera corners in world space
        Vector2[] corners = new Vector2[4] {
            camPos + new Vector2(-hW, -hH),
            camPos + new Vector2(hW, -hH),
            camPos + new Vector2(hW, hH),
            camPos + new Vector2(-hW, hH)
        };

        var transform = Owner.Transform;
        Matrix4x4.Invert(transform.GetWorldMatrix(), out var invWorld);

        float minLX = float.MaxValue, minLY = float.MaxValue;
        float maxLX = float.MinValue, maxLY = float.MinValue;

        foreach (var corner in corners)
        {
            var lp3 = SystemNumericsVector3.Transform(new SystemNumericsVector3(corner, 0), invWorld);
            minLX = MathF.Min(minLX, lp3.X); minLY = MathF.Min(minLY, lp3.Y);
            maxLX = MathF.Max(maxLX, lp3.X); maxLY = MathF.Max(maxLY, lp3.Y);
        }

        int minX = (int)MathF.Floor(minLX / tileWidth);
        int minY = (int)MathF.Floor(minLY / tileHeight);
        int maxX = (int)MathF.Floor(maxLX / tileWidth);
        int maxY = (int)MathF.Floor(maxLY / tileHeight);

        minX -= 1; minY -= 1;
        maxX += 1; maxY += 1;

        minX = Math.Max(minX, tileMinX);
        minY = Math.Max(minY, tileMinY);
        maxX = Math.Min(maxX, tileMaxX);
        maxY = Math.Min(maxY, tileMaxY);
        if (minX > maxX || minY > maxY) return;

        // 2. Draw visible tiles
        var worldMatrix = transform.GetWorldMatrix();

        foreach (var pair in _tilemap.GetTilesInRegion(minX, minY, maxX, maxY))
        {
            var (x, y) = pair.Key;
            var tile = pair.Value;

            var spriteOpt = tile.GetSprite(x, y, _tilemap);
            if (!spriteOpt.HasValue) continue;
            var sprite = spriteOpt.Value;

            RenderTexture? tex = null;
            if (!string.IsNullOrWhiteSpace(sprite.Path))
            {
                string cacheKey = string.IsNullOrWhiteSpace(sprite.Guid) ? sprite.Path : $"{sprite.Guid}:{sprite.Path}";
                if (!_textureCache.TryGetValue(cacheKey, out tex))
                {
                    try {
                        tex = pipeline.LoadTexture(sprite);
                        if (tex != null) _textureCache[cacheKey] = tex;
                    } catch { }
                }
            }

            tex ??= DefaultSprites.Square;
            if (tex == null) continue;

            Vector2 localPos = new Vector2(x * _tilemap.TileSize.X, y * _tilemap.TileSize.Y);
            Matrix4x4 tileMatrix = Matrix4x4.CreateScale(_tilemap.TileSize.X, _tilemap.TileSize.Y, 1.0f) * 
                                  Matrix4x4.CreateTranslation(localPos.X, localPos.Y, 0) * 
                                  worldMatrix;
            System.Numerics.Vector2 uvMin = System.Numerics.Vector2.Zero;
            System.Numerics.Vector2 uvMax = System.Numerics.Vector2.One;
            pipeline.TryGetSpriteUv(sprite, tex, out uvMin, out uvMax);

            pipeline.DrawTile(tex, tileMatrix, tile.Color, projection, view, targetFbo, Owner, SortingLayerName, uvMin, uvMax);
        }

        _tilemap.RenderDirty = false;
    }
}
