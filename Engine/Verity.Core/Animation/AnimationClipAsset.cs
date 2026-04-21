using System.Text.Json;
using System.Text.Json.Nodes;
using Verity.Core.Serialization;

namespace Verity.Core.Animation;

public static class AnimationClipAsset
{
    private static readonly JsonSerializerOptions Options = new()
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
            new ColorConverter()
        }
    };

    public static string ToJson(AnimationClipBase clip)
    {
        if (clip is SpriteAnimationClip spriteClip)
            spriteClip.SyncFramesFromTrack();
        clip.PostLoad();
        var node = new JsonObject
        {
            ["Type"] = clip is SpriteAnimationClip ? nameof(SpriteAnimationClip) : nameof(AnimationClip)
        };

        foreach (var pair in JsonSerializer.SerializeToNode(clip, clip.GetType(), Options)!.AsObject())
            node[pair.Key] = pair.Value?.DeepClone();

        return node.ToJsonString(Options);
    }

    public static AnimationClipBase? FromJson(string json, string? assetPath = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is not JsonObject obj)
                return null;

            string type = (string?)obj["Type"] ?? nameof(AnimationClip);
            Type targetType = string.Equals(type, nameof(SpriteAnimationClip), StringComparison.Ordinal)
                ? typeof(SpriteAnimationClip)
                : typeof(AnimationClip);

            AnimationClipBase? clip = (AnimationClipBase?)JsonSerializer.Deserialize(obj.ToJsonString(), targetType, Options);
            if (clip == null)
                return null;

            clip.AssetPath = AssetPathUtility.Normalize(assetPath);
            clip.AssetGuid = string.IsNullOrWhiteSpace(assetPath) ? string.Empty : AssetPathUtility.TryGetGuid(assetPath);
            clip.PostLoad();
            return clip;
        }
        catch
        {
            return null;
        }
    }

    public static bool SaveToFile(string fullPath, AnimationClipBase clip)
    {
        try
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, ToJson(clip));
            clip.AssetPath = AssetPathUtility.Normalize(fullPath);
            clip.AssetGuid = AssetPathUtility.EnsureMetaAndGetGuid(fullPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static AnimationClipBase? LoadFromFile(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null;

        try
        {
            return FromJson(File.ReadAllText(fullPath), fullPath);
        }
        catch
        {
            return null;
        }
    }
}
