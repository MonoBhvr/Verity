using System.Text.Json;
using Verity.Core;

namespace Verity.Graphics;

public class CameraTextureAsset : Verity.Core.TextureAsset
{
    public CameraTextureAsset()
    {
    }

    public CameraTextureAsset(string path) : base(path)
    {
    }

    public CameraTextureAsset(string path, string guid) : base(path, guid)
    {
    }

    public static implicit operator CameraTextureAsset(string path) => new(path);

    public CameraTextureAssetData LoadSettings(string? assetRoot = null)
        => CameraTextureAssetData.Load(Path, Guid, assetRoot);

    public void SaveSettings(CameraTextureAssetData settings, string? assetRoot = null)
        => settings.Save(Path, assetRoot);

    public void Resize(int width, int height, string? assetRoot = null)
    {
        var settings = LoadSettings(assetRoot);
        settings.Width = Math.Max(1, width);
        settings.Height = Math.Max(1, height);
        SaveSettings(settings, assetRoot);
    }
}

public sealed class CameraTextureAssetData
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public int Width { get; set; } = 512;
    public int Height { get; set; } = 512;

    public static CameraTextureAssetData Load(string path, string? guid = null, string? assetRoot = null)
    {
        string resolvedPath = AssetPathUtility.ResolvePath(assetRoot, path, guid);
        if (!File.Exists(resolvedPath))
            return new CameraTextureAssetData();

        try
        {
            return JsonSerializer.Deserialize<CameraTextureAssetData>(File.ReadAllText(resolvedPath), Options)
                ?? new CameraTextureAssetData();
        }
        catch
        {
            return new CameraTextureAssetData();
        }
    }

    public void Save(string path, string? assetRoot = null)
    {
        string resolvedPath = AssetPathUtility.ResolvePath(assetRoot, path);
        string? directory = System.IO.Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        Width = Math.Max(1, Width);
        Height = Math.Max(1, Height);
        File.WriteAllText(resolvedPath, JsonSerializer.Serialize(this, Options));
        AssetPathUtility.EnsureMetaAndGetGuid(resolvedPath);
    }
}
