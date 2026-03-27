namespace Verity.Core;

public struct Sprite : IPathAsset
{
    public string Path { get; set; }
    public string Guid { get; set; }
    public string SpriteId { get; set; }

    public Sprite(string path)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = System.IO.Path.IsPathRooted(path) ? AssetPathUtility.EnsureMetaAndGetGuid(path) : string.Empty;
        SpriteId = string.Empty;
    }

    public Sprite(string path, string guid)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = guid ?? string.Empty;
        SpriteId = string.Empty;
    }

    public Sprite(string path, string guid, string spriteId)
    {
        Path = AssetPathUtility.Normalize(path);
        Guid = guid ?? string.Empty;
        SpriteId = spriteId ?? string.Empty;
    }

    public static implicit operator Sprite(string path) => new Sprite(path);
    public static implicit operator string(Sprite sprite) => sprite.Path ?? string.Empty;

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return "None";

        return string.IsNullOrWhiteSpace(SpriteId) ? Path : $"{Path} [{SpriteId}]";
    }
}
