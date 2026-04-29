namespace Verity.Game.Runtime;

internal static class RuntimeContentPathMapper
{
    private static readonly string[] KnownExtensions =
    [
        ".fontasset.meta", ".uiprefab.meta", ".uistyle.meta", ".animtile.meta", ".ruletile.meta", ".blueprint.meta",
        ".controller.meta", ".rendertexture.meta", ".shader.meta", ".style.meta", ".verity.meta", ".json.meta",
        ".png.meta", ".jpg.meta", ".jpeg.meta", ".bmp.meta", ".wav.meta", ".ogg.meta", ".mp3.meta", ".ttf.meta", ".otf.meta", ".tile.meta",
        ".ui.meta", ".lua.meta", ".cs.meta", ".fontasset", ".uiprefab", ".uistyle", ".animtile", ".ruletile",
        ".blueprint", ".controller", ".rendertexture", ".shader", ".style", ".verity", ".json", ".png", ".jpg",
        ".jpeg", ".bmp", ".wav", ".ogg", ".mp3", ".ttf", ".otf", ".tile", ".ui", ".lua", ".cs"
    ];

    public static bool TryConvertManifestSuffixToAssetPath(string suffix, out string assetRelativePath)
    {
        foreach (string extension in KnownExtensions)
        {
            if (!suffix.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                continue;

            string stem = suffix[..^extension.Length];
            assetRelativePath = stem.Replace('.', Path.DirectorySeparatorChar) + extension;
            return true;
        }

        assetRelativePath = suffix.Replace('.', Path.DirectorySeparatorChar);
        return true;
    }
}
