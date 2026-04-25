using System.Reflection;

namespace Verity.Game.Runtime;

public sealed class EmbeddedRuntimeContentSource : IRuntimeContentSource
{
    private readonly Assembly _assembly;
    private readonly string _assemblyName;
    private readonly string _baseDir;

    public EmbeddedRuntimeContentSource(Assembly assembly, string assemblyName, string baseDir)
    {
        _assembly = assembly;
        _assemblyName = assemblyName;
        _baseDir = baseDir;
    }

    public string PrepareContentRoot()
    {
        ExtractEmbeddedAssetsToBaseDir();
        return _baseDir;
    }

    public string GetLoosePath(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        return Path.Combine(_baseDir, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
    }

    public string? TryReadText(string relativePath)
    {
        byte[]? bytes = TryReadBytes(relativePath);
        return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public byte[]? TryReadBytes(string relativePath)
    {
        string loosePath = GetLoosePath(relativePath);
        if (File.Exists(loosePath))
            return File.ReadAllBytes(loosePath);

        foreach (string resourceName in GetCandidateResourceNames(relativePath))
        {
            using Stream? stream = _assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                continue;

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        return null;
    }

    private IEnumerable<string> GetCandidateResourceNames(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        yield return $"{_assemblyName}.{normalizedPath.Replace('/', '.')}";

        if (normalizedPath.Equals("Assets/BuildSettings.json", StringComparison.OrdinalIgnoreCase))
            yield return $"{_assemblyName}.BuildSettings.json";
    }

    private void ExtractEmbeddedAssetsToBaseDir()
    {
        string[] resourceNames = _assembly.GetManifestResourceNames();
        if (!resourceNames.Any(name => name.StartsWith($"{_assemblyName}.Assets.", StringComparison.Ordinal)))
            return;

        foreach (string resourceName in resourceNames)
        {
            if (!TryMapResourceToRelativePath(resourceName, out string relativePath))
                continue;

            string outputPath = Path.Combine(_baseDir, relativePath);
            if (File.Exists(outputPath))
                continue;

            using Stream? resourceStream = _assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
                continue;

            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            using FileStream fileStream = File.Create(outputPath);
            resourceStream.CopyTo(fileStream);
        }
    }

    private bool TryMapResourceToRelativePath(string resourceName, out string relativePath)
    {
        relativePath = string.Empty;

        if (resourceName.Equals($"{_assemblyName}.scene.json", StringComparison.Ordinal))
        {
            relativePath = "scene.json";
            return true;
        }

        if (resourceName.Equals($"{_assemblyName}.UserScripts.dll", StringComparison.Ordinal))
        {
            relativePath = "UserScripts.dll";
            return true;
        }

        if (resourceName.Equals($"{_assemblyName}.BuildSettings.json", StringComparison.Ordinal))
        {
            relativePath = Path.Combine("Assets", "BuildSettings.json");
            return true;
        }

        string assetsPrefix = $"{_assemblyName}.Assets.";
        if (!resourceName.StartsWith(assetsPrefix, StringComparison.Ordinal))
            return false;

        string suffix = resourceName[assetsPrefix.Length..];
        if (!TryConvertManifestSuffixToAssetPath(suffix, out string assetRelativePath))
            return false;

        relativePath = Path.Combine("Assets", assetRelativePath);
        return true;
    }

    private static bool TryConvertManifestSuffixToAssetPath(string suffix, out string assetRelativePath)
    {
        string[] knownExtensions =
        [
            ".fontasset.meta", ".uiprefab.meta", ".uistyle.meta", ".animtile.meta", ".ruletile.meta", ".blueprint.meta",
            ".controller.meta", ".shader.meta", ".style.meta", ".verity.meta", ".json.meta", ".png.meta", ".jpg.meta",
            ".jpeg.meta", ".bmp.meta", ".wav.meta", ".ogg.meta", ".mp3.meta", ".ttf.meta", ".otf.meta", ".tile.meta",
            ".ui.meta", ".fontasset", ".uiprefab", ".uistyle", ".animtile", ".ruletile", ".blueprint", ".controller",
            ".shader", ".style", ".verity", ".json", ".png", ".jpg", ".jpeg", ".bmp", ".wav", ".ogg", ".mp3",
            ".ttf", ".otf", ".tile", ".ui"
        ];

        foreach (string extension in knownExtensions)
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

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }
}
