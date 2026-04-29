namespace Verity.Core;

public class TextureAsset : IPathAsset
{
    public string Path { get; set; } = string.Empty;
    public string Guid { get; set; } = string.Empty;

    public TextureAsset()
    {
    }

    public TextureAsset(string path)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty;
    }

    public TextureAsset(string path, string guid)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = guid ?? string.Empty;
    }

    public static implicit operator TextureAsset(string path) => new(path);
    public static implicit operator string(TextureAsset asset) => asset.Path ?? string.Empty;
    public override string ToString() => Path ?? "None";
}
