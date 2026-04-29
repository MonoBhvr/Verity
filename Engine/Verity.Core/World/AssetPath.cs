using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Numerics;
using Verity.Core.Collections;
using Verity.Core.Serialization;
using Verity.Core.World;

namespace Verity.Core;

public interface IPathAsset
{
    string Path { get; set; }
    string Guid { get; set; }
}

public sealed class AssetMeta
{
    public string Guid { get; set; } = string.Empty;
    public SpriteImportSettings? SpriteImport { get; set; }
}

public readonly record struct AssetReferenceData(string Path, string Guid)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Path) && string.IsNullOrWhiteSpace(Guid);
}

public static class AssetPathUtility
{
    private readonly record struct CachedMeta(AssetMeta Meta, long LastWriteTicks);
    private readonly record struct CachedSpriteSlice(SpriteSlice Slice, long LastWriteTicks);

    private static readonly JsonSerializerOptions MetaOptions = new()
    {
        WriteIndented = true,
        Converters = { new Vector2Converter() }
    };
    private static readonly ConcurrentLruCache<string, ConcurrentDictionary<string, string>> GuidCache = new(1024);
    private static readonly ConcurrentLruCache<string, CachedMeta> MetaCache = new(1024);
    private static readonly ConcurrentLruCache<string, CachedSpriteSlice> SpriteSliceCache = new(1024);

    public static string Normalize(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            return string.Empty;

        int assetsIndex = fullPath.IndexOf("Assets", StringComparison.OrdinalIgnoreCase);
        string normalized = assetsIndex >= 0 ? fullPath[assetsIndex..] : fullPath;
        return normalized.Replace("\\", "/");
    }

    public static string DisplayName(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? "None" : System.IO.Path.GetFileName(path);
    }

    public static bool IsMetaFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);

    public static string GetMetaPath(string assetPath) => assetPath + ".meta";

    public static string EnsureMetaAndGetGuid(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || IsMetaFile(assetPath))
            return string.Empty;

        string fullPath = Path.GetFullPath(assetPath);
        if (!File.Exists(fullPath))
            return string.Empty;

        var meta = LoadMeta(fullPath);
        if (!string.IsNullOrWhiteSpace(meta.Guid))
        {
            UpdateGuidCacheForAsset(fullPath, meta.Guid);
            return meta.Guid;
        }

        meta.Guid = System.Guid.NewGuid().ToString("N");
        SaveMeta(fullPath, meta);
        UpdateGuidCacheForAsset(fullPath, meta.Guid);
        return meta.Guid;
    }

    public static string TryGetGuid(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || IsMetaFile(assetPath))
            return string.Empty;

        string fullPath = Path.GetFullPath(assetPath);
        if (!File.Exists(fullPath))
            return string.Empty;

        string guid = LoadMeta(fullPath).Guid;
        if (!string.IsNullOrWhiteSpace(guid))
            UpdateGuidCacheForAsset(fullPath, guid);
        return guid;
    }

    public static AssetReferenceData CreateReference(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return default;

        string normalized = Normalize(assetPath);
        string guid = Path.IsPathRooted(assetPath) ? EnsureMetaAndGetGuid(assetPath) : string.Empty;
        return new AssetReferenceData(normalized, guid);
    }

    public static JsonObject ToJsonNode(string? path, string? guid = null)
    {
        AssetReferenceData data = string.IsNullOrWhiteSpace(guid)
            ? CreateReference(path)
            : new AssetReferenceData(Normalize(path), guid ?? string.Empty);

        return new JsonObject
        {
            ["Path"] = data.Path,
            ["Guid"] = data.Guid
        };
    }

    public static JsonObject ToSpriteJsonNode(Sprite sprite)
    {
        JsonObject node = ToJsonNode(sprite.Path, sprite.Guid);
        if (!string.IsNullOrWhiteSpace(sprite.SpriteId))
            node["SpriteId"] = sprite.SpriteId;
        return node;
    }

    public static AssetReferenceData FromJsonNode(JsonNode? node)
    {
        if (node == null)
            return default;

        if (node is JsonValue value && value.TryGetValue<string>(out var rawPath))
            return new AssetReferenceData(Normalize(rawPath), string.Empty);

        string path = Normalize((string?)node["Path"]);
        string guid = (string?)node["Guid"] ?? string.Empty;
        return new AssetReferenceData(path, guid);
    }

    public static Sprite FromSpriteJsonNode(JsonNode? node)
    {
        AssetReferenceData data = FromJsonNode(node);
        string spriteId = (string?)node?["SpriteId"] ?? string.Empty;
        return new Sprite(data.Path, data.Guid, spriteId);
    }

    public static string ResolvePath(string? projectRootOrAssetsPath, string? path, string? guid = null)
    {
        string normalizedPath = Normalize(path);

        if (!string.IsNullOrWhiteSpace(guid))
        {
            string? resolvedByGuid = TryResolveByGuid(projectRootOrAssetsPath, guid);
            if (!string.IsNullOrWhiteSpace(resolvedByGuid))
                return resolvedByGuid;
        }

        if (string.IsNullOrWhiteSpace(normalizedPath))
            return string.Empty;

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        string? assetsRoot = GetAssetsRoot(projectRootOrAssetsPath);
        string basePath = assetsRoot ?? projectRootOrAssetsPath ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(assetsRoot) &&
            string.Equals(Path.GetFileName(basePath), "Assets", StringComparison.OrdinalIgnoreCase) &&
            normalizedPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedPath = normalizedPath["Assets/".Length..];
        }

        return Path.GetFullPath(Path.Combine(basePath, normalizedPath));
    }

    public static void InvalidateCache(string? projectRootOrAssetsPath = null)
    {
        if (string.IsNullOrWhiteSpace(projectRootOrAssetsPath))
        {
            GuidCache.Clear();
            MetaCache.Clear();
            SpriteSliceCache.Clear();
            return;
        }

        string? assetsRoot = GetAssetsRoot(projectRootOrAssetsPath);
        if (!string.IsNullOrWhiteSpace(assetsRoot))
        {
            GuidCache.Remove(NormalizeCacheKey(Path.GetFullPath(assetsRoot)));
            InvalidateAssetCachesUnderRoot(assetsRoot);
        }
    }

    public static void InvalidateAssetCache(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        string normalizedPath = IsMetaFile(assetPath)
            ? assetPath[..^5]
            : assetPath;
        string fullPath = Path.GetFullPath(normalizedPath);
        MetaCache.Remove(NormalizeCacheKey(fullPath));
        InvalidateSpriteSliceCacheForAsset(fullPath);
        RemoveGuidCacheEntriesForAsset(fullPath);
    }

    public static AssetMeta LoadMeta(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return new AssetMeta();

        string fullPath = Path.GetFullPath(assetPath);
        string metaPath = GetMetaPath(fullPath);
        string metaCacheKey = NormalizeCacheKey(fullPath);

        try
        {
            if (!File.Exists(metaPath))
            {
                MetaCache.Remove(metaCacheKey);
                return new AssetMeta();
            }

            long lastWriteTicks = File.GetLastWriteTimeUtc(metaPath).Ticks;
            if (MetaCache.TryGetValue(metaCacheKey, out var cached) && cached.LastWriteTicks == lastWriteTicks)
                return CloneMeta(cached.Meta);

            var meta = JsonSerializer.Deserialize<AssetMeta>(File.ReadAllText(metaPath), MetaOptions) ?? new AssetMeta();
            MetaCache.Set(metaCacheKey, new CachedMeta(CloneMeta(meta), lastWriteTicks));
            return CloneMeta(meta);
        }
        catch
        {
            MetaCache.Remove(metaCacheKey);
            return new AssetMeta();
        }
    }

    public static void SaveMeta(string? assetPath, AssetMeta meta)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || IsMetaFile(assetPath) || meta == null)
            return;

        string fullPath = Path.GetFullPath(assetPath);
        if (!File.Exists(fullPath))
            return;

        string metaPath = GetMetaPath(fullPath);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(meta, MetaOptions));
        long lastWriteTicks = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath).Ticks : 0;
        MetaCache.Set(NormalizeCacheKey(fullPath), new CachedMeta(CloneMeta(meta), lastWriteTicks));
        InvalidateSpriteSliceCacheForAsset(fullPath);
        if (!string.IsNullOrWhiteSpace(meta.Guid))
            UpdateGuidCacheForAsset(fullPath, meta.Guid);
    }

    public static SpriteImportSettings? TryGetSpriteImportSettings(string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return null;

        return LoadMeta(assetPath).SpriteImport;
    }

    public static void SaveSpriteImportSettings(string? assetPath, SpriteImportSettings settings)
    {
        if (string.IsNullOrWhiteSpace(assetPath) || settings == null)
            return;

        var meta = LoadMeta(assetPath);
        if (string.IsNullOrWhiteSpace(meta.Guid))
            meta.Guid = EnsureMetaAndGetGuid(assetPath);

        meta.SpriteImport = settings;
        SaveMeta(assetPath, meta);
    }

    public static SpriteSlice ResolveSpriteSlice(string? assetPath, Sprite sprite, int textureWidth, int textureHeight)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, new Vector2(0.5f, 0.5f));

        string fullPath = Path.GetFullPath(assetPath);
        string metaPath = GetMetaPath(fullPath);
        long lastWriteTicks = File.Exists(metaPath) ? File.GetLastWriteTimeUtc(metaPath).Ticks : 0;
        string cacheKey = NormalizeCacheKey($"{fullPath}|{sprite.SpriteId}|{textureWidth}|{textureHeight}");
        if (SpriteSliceCache.TryGetValue(cacheKey, out var cached) && cached.LastWriteTicks == lastWriteTicks)
            return cached.Slice.Clone();

        SpriteImportSettings? import = TryGetSpriteImportSettings(assetPath);
        SpriteSlice resolvedSlice;
        if (import == null)
        {
            resolvedSlice = SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, new Vector2(0.5f, 0.5f));
            SpriteSliceCache.Set(cacheKey, new CachedSpriteSlice(resolvedSlice.Clone(), lastWriteTicks));
            return resolvedSlice;
        }

        import.Normalize(textureWidth, textureHeight);

        if (!string.IsNullOrWhiteSpace(sprite.SpriteId))
        {
            var matched = import.Slices.FirstOrDefault(slice => string.Equals(slice.Id, sprite.SpriteId, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                resolvedSlice = ClampSlice(matched.Clone(), textureWidth, textureHeight, import.DefaultPivot);
                SpriteSliceCache.Set(cacheKey, new CachedSpriteSlice(resolvedSlice.Clone(), lastWriteTicks));
                return resolvedSlice;
            }
        }

        if (import.SpriteMode == SpriteImportMode.Single)
            resolvedSlice = ClampSlice(import.Slices.First(), textureWidth, textureHeight, import.DefaultPivot);
        else if (import.Slices.Count > 0)
            resolvedSlice = ClampSlice(import.Slices[0], textureWidth, textureHeight, import.DefaultPivot);
        else
            resolvedSlice = SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, import.DefaultPivot);

        SpriteSliceCache.Set(cacheKey, new CachedSpriteSlice(resolvedSlice.Clone(), lastWriteTicks));
        return resolvedSlice;
    }

    public static SpriteSlice ClampSlice(SpriteSlice slice, int textureWidth, int textureHeight, Vector2 defaultPivot)
    {
        int maxWidth = Math.Max(1, textureWidth);
        int maxHeight = Math.Max(1, textureHeight);

        var clamped = slice.Clone();
        clamped.EnsureId();
        clamped.X = Math.Clamp(clamped.X, 0, Math.Max(0, maxWidth - 1));
        clamped.Y = Math.Clamp(clamped.Y, 0, Math.Max(0, maxHeight - 1));
        clamped.Width = Math.Clamp(clamped.Width, 1, maxWidth - clamped.X);
        clamped.Height = Math.Clamp(clamped.Height, 1, maxHeight - clamped.Y);
        clamped.Pivot = SpriteImportUtility.ClampPivot(clamped.Pivot);
        return clamped;
    }

    private static string? TryResolveByGuid(string? projectRootOrAssetsPath, string guid)
    {
        string? assetsRoot = GetAssetsRoot(projectRootOrAssetsPath);
        if (string.IsNullOrWhiteSpace(assetsRoot) || !Directory.Exists(assetsRoot))
            return null;

        string normalizedRoot = NormalizeCacheKey(Path.GetFullPath(assetsRoot));
        bool cacheWasBuilt = false;
        if (!GuidCache.TryGetValue(normalizedRoot, out var cache))
        {
            cache = BuildGuidCache(assetsRoot);
            GuidCache.Set(normalizedRoot, cache);
            cacheWasBuilt = true;
        }

        if (cache.TryGetValue(guid, out string? cachedPath) && File.Exists(cachedPath))
            return cachedPath;

        if (cacheWasBuilt)
            return null;

        cache = BuildGuidCache(assetsRoot);
        GuidCache.Set(normalizedRoot, cache);
        return cache.TryGetValue(guid, out cachedPath) && File.Exists(cachedPath) ? cachedPath : null;
    }

    private static ConcurrentDictionary<string, string> BuildGuidCache(string assetsRoot)
    {
        var cache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(assetsRoot))
            return cache;

        foreach (string metaPath in Directory.GetFiles(assetsRoot, "*.meta", SearchOption.AllDirectories))
        {
            string assetPath = metaPath[..^5];
            if (!File.Exists(assetPath))
                continue;

            string guid = TryReadGuid(metaPath);
            if (!string.IsNullOrWhiteSpace(guid))
                cache[guid] = Path.GetFullPath(assetPath);
        }

        return cache;
    }

    private static void RemoveGuidCacheEntriesForAsset(string fullPath)
    {
        foreach (var rootCache in GuidCache.Values)
        {
            foreach (var pair in rootCache)
            {
                if (string.Equals(pair.Value, fullPath, StringComparison.OrdinalIgnoreCase))
                    rootCache.TryRemove(pair.Key, out _);
            }
        }
    }

    private static string? GetAssetsRoot(string? projectRootOrAssetsPath)
    {
        if (string.IsNullOrWhiteSpace(projectRootOrAssetsPath))
            return null;

        if (Directory.Exists(projectRootOrAssetsPath) &&
            string.Equals(Path.GetFileName(projectRootOrAssetsPath), "Assets", StringComparison.OrdinalIgnoreCase))
            return projectRootOrAssetsPath;

        string assetsPath = Path.Combine(projectRootOrAssetsPath, "Assets");
        return Directory.Exists(assetsPath) ? assetsPath : projectRootOrAssetsPath;
    }

    private static string TryReadGuid(string metaPath)
    {
        try
        {
            if (!File.Exists(metaPath))
                return string.Empty;

            var meta = JsonSerializer.Deserialize<AssetMeta>(File.ReadAllText(metaPath), MetaOptions);
            return meta?.Guid ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void UpdateGuidCacheForAsset(string assetPath, string guid)
    {
        string? assetsRoot = GetAssetsRoot(Path.GetDirectoryName(assetPath));
        if (string.IsNullOrWhiteSpace(assetsRoot))
            return;

        string normalizedRoot = NormalizeCacheKey(Path.GetFullPath(assetsRoot));
        if (!GuidCache.TryGetValue(normalizedRoot, out var cache))
        {
            cache = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            GuidCache.Set(normalizedRoot, cache);
        }

        cache[guid] = Path.GetFullPath(assetPath);
    }

    private static AssetMeta CloneMeta(AssetMeta meta)
    {
        return new AssetMeta
        {
            Guid = meta.Guid,
            SpriteImport = meta.SpriteImport?.Clone()
        };
    }

    private static void InvalidateAssetCachesUnderRoot(string assetsRoot)
    {
        string normalizedRoot = NormalizeCacheKey(Path.GetFullPath(assetsRoot));

        foreach (string key in MetaCache.Keys.Where(path => path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)))
            MetaCache.Remove(key);

        foreach (string key in SpriteSliceCache.Keys.Where(path => path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)))
            SpriteSliceCache.Remove(key);
    }

    private static void InvalidateSpriteSliceCacheForAsset(string assetPath)
    {
        string prefix = NormalizeCacheKey($"{Path.GetFullPath(assetPath)}|");
        foreach (string key in SpriteSliceCache.Keys.Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            SpriteSliceCache.Remove(key);
    }

    private static string NormalizeCacheKey(string key) => key.ToUpperInvariant();
}
