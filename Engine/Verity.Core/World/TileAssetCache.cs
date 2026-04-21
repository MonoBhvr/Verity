using System.Text.Json;
using Verity.Core.Collections;
using Verity.Core.Serialization;

namespace Verity.Core.World;

public static class TileAssetCache
{
    private static readonly object Sync = new();
    private static readonly LruCache<string, TileBase> Cache = new(512);
    private static readonly JsonSerializerOptions TileOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new Vector2Converter(),
            new Vector3Converter(),
            new Vector4Converter(),
            new SpriteConverter(),
            new StyleAssetConverter(),
            new ShaderAssetConverter(),
            new ColorConverter(),
            new TileBaseConverter(),
            new TilemapTilesConverter()
        }
    };

    public static TileBase? Load(string? assetPath, string? guid = null, string? assetRootPath = null)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        string resolvedPath = AssetPathUtility.ResolvePath(assetRootPath ?? SceneSerializer.AssetRootPath, assetPath, guid);
        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            return null;

        lock (Sync)
        {
            string cacheKey = NormalizeCacheKey(resolvedPath);
            if (Cache.TryGetValue(cacheKey, out TileBase? cached))
                return cached;

            string json = File.ReadAllText(resolvedPath);
            TileBase? loaded = JsonSerializer.Deserialize<TileBase>(json, TileOptions);
            if (loaded == null)
                return null;

            loaded.AssetPath = AssetPathUtility.Normalize(resolvedPath);
            loaded.AssetGuid = string.IsNullOrWhiteSpace(guid) ? AssetPathUtility.TryGetGuid(resolvedPath) : guid;
            Cache.Set(cacheKey, loaded);
            return loaded;
        }
    }

    public static TileBase? Load(AssetReferenceData assetReference, string? assetRootPath = null)
        => Load(assetReference.Path, assetReference.Guid, assetRootPath);

    public static void Invalidate(string? assetPath, string? assetRootPath = null)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        string resolvedPath = AssetPathUtility.ResolvePath(assetRootPath ?? SceneSerializer.AssetRootPath, assetPath);
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return;

        lock (Sync)
        {
            Cache.Remove(NormalizeCacheKey(resolvedPath));
        }
    }

    public static void Clear()
    {
        lock (Sync)
        {
            Cache.Clear();
        }
    }

    private static string NormalizeCacheKey(string path) => Path.GetFullPath(path).ToUpperInvariant();
}
