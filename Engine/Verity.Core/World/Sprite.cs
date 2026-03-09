namespace Verity.Core;

public struct Sprite
{
    public string Path { get; set; }

    public Sprite(string path)
    {
        Path = path;
    }

    public static implicit operator Sprite(string path) => new Sprite(path);
    public static implicit operator string(Sprite sprite) => sprite.Path ?? string.Empty;

    public override string ToString() => Path ?? "None";
}
