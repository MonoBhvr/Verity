namespace Verity.Core;

public struct UiAsset : IPathAsset
{
    public string Path { get; set; }
    public string Guid { get; set; }

    public UiAsset(string path)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty;
    }

    public UiAsset(string path, string guid)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = guid ?? string.Empty;
    }

    public static implicit operator UiAsset(string path) => new(path);
    public static implicit operator string(UiAsset asset) => asset.Path ?? string.Empty;

    public override string ToString() => string.IsNullOrWhiteSpace(Path) ? "None" : Path;
}
