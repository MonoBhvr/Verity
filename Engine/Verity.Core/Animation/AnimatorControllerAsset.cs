using System.Text.Json;
using Verity.Core;
using Verity.Core.Serialization;

namespace Verity.Core.Animation;

public static class AnimatorControllerAsset
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new Vector2Converter(), new Vector3Converter(), new Vector4Converter(), new SpriteConverter(), new StyleAssetConverter(), new ShaderAssetConverter(), new ColorConverter() }
    };

    public static string ToJson(AnimatorController controller)
    {
        controller.PostLoad();
        return JsonSerializer.Serialize(controller, Options);
    }

    public static AnimatorController? FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var controller = JsonSerializer.Deserialize<AnimatorController>(json, Options);
            controller?.PostLoad();
            return controller;
        }
        catch
        {
            return null;
        }
    }

    public static bool SaveToFile(string fullPath, AnimatorController controller)
    {
        try
        {
            string? directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, ToJson(controller));
            AssetPathUtility.EnsureMetaAndGetGuid(fullPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static AnimatorController? LoadFromFile(string fullPath)
    {
        if (!File.Exists(fullPath))
            return null;

        try
        {
            return FromJson(File.ReadAllText(fullPath));
        }
        catch
        {
            return null;
        }
    }
}
