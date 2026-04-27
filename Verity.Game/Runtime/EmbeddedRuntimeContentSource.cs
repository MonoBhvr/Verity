using System.Reflection;

namespace Verity.Game.Runtime;

public sealed class EmbeddedRuntimeContentSource : IRuntimeContentSource
{
    private const string RuntimeContentDirectoryName = "RuntimeContent";
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
        string stagingRoot = GetStagingRoot();
        if (HasLooseRuntimeContent(stagingRoot))
            return stagingRoot;

        if (HasLooseRuntimeContent(_baseDir))
            return _baseDir;

        ExtractEmbeddedAssetsToBaseDir(stagingRoot);
        return stagingRoot;
    }

    public string GetLoosePath(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        return Path.Combine(GetStagingRoot(), normalizedPath.Replace('/', Path.DirectorySeparatorChar));
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

        string legacyLoosePath = GetLegacyLoosePath(relativePath);
        if (File.Exists(legacyLoosePath))
            return File.ReadAllBytes(legacyLoosePath);

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
        yield return $"{_assemblyName}.{RuntimeContentDirectoryName}.{normalizedPath.Replace('/', '.')}";
        yield return $"{_assemblyName}.{normalizedPath.Replace('/', '.')}";

        if (normalizedPath.Equals("Assets/BuildSettings.json", StringComparison.OrdinalIgnoreCase))
            yield return $"{_assemblyName}.BuildSettings.json";
    }

    private void ExtractEmbeddedAssetsToBaseDir(string contentRoot)
    {
        string[] resourceNames = _assembly.GetManifestResourceNames();
        if (!resourceNames.Any(name =>
                name.StartsWith($"{_assemblyName}.{RuntimeContentDirectoryName}.", StringComparison.Ordinal) ||
                name.StartsWith($"{_assemblyName}.Assets.", StringComparison.Ordinal)))
            return;

        foreach (string resourceName in resourceNames)
        {
            if (!TryMapResourceToRelativePath(resourceName, out string relativePath))
                continue;

            string outputPath = Path.Combine(contentRoot, relativePath);

            using Stream? resourceStream = _assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
                continue;

            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            if (File.Exists(outputPath))
                File.Delete(outputPath);

            using FileStream fileStream = File.Create(outputPath);
            resourceStream.CopyTo(fileStream);
        }
    }

    private bool TryMapResourceToRelativePath(string resourceName, out string relativePath)
    {
        relativePath = string.Empty;

        if (resourceName.Equals($"{_assemblyName}.{RuntimeContentDirectoryName}.scene.json", StringComparison.Ordinal))
        {
            relativePath = "scene.json";
            return true;
        }

        if (resourceName.Equals($"{_assemblyName}.{RuntimeContentDirectoryName}.UserScripts.dll", StringComparison.Ordinal))
        {
            relativePath = "UserScripts.dll";
            return true;
        }

        string runtimeAssetsPrefix = $"{_assemblyName}.{RuntimeContentDirectoryName}.Assets.";
        if (resourceName.StartsWith(runtimeAssetsPrefix, StringComparison.Ordinal))
        {
            string runtimeSuffix = resourceName[runtimeAssetsPrefix.Length..];
            if (!RuntimeContentPathMapper.TryConvertManifestSuffixToAssetPath(runtimeSuffix, out string runtimeAssetRelativePath))
                return false;

            relativePath = Path.Combine("Assets", runtimeAssetRelativePath);
            return true;
        }

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
        if (!RuntimeContentPathMapper.TryConvertManifestSuffixToAssetPath(suffix, out string assetRelativePath))
            return false;

        relativePath = Path.Combine("Assets", assetRelativePath);
        return true;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private string GetStagingRoot() => Path.Combine(_baseDir, RuntimeContentDirectoryName);

    private string GetLegacyLoosePath(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        return Path.Combine(_baseDir, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool HasLooseRuntimeContent(string baseDir)
    {
        return Directory.Exists(Path.Combine(baseDir, "Assets")) ||
               File.Exists(Path.Combine(baseDir, "scene.json")) ||
               File.Exists(Path.Combine(baseDir, "UserScripts.dll"));
    }
}
