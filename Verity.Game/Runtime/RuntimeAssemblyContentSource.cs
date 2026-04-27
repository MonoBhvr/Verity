using System.Reflection;

namespace Verity.Game.Runtime;

public sealed class RuntimeAssemblyContentSource : IRuntimeContentSource
{
    private readonly Assembly _assembly;
    private readonly string _assemblyName;
    private readonly string _executableBaseDir;

    public RuntimeAssemblyContentSource(Assembly assembly, string assemblyName, string executableBaseDir)
    {
        _assembly = assembly;
        _assemblyName = assemblyName;
        _executableBaseDir = executableBaseDir;
    }

    public string PrepareContentRoot()
    {
        if (HasLooseRuntimeContent(_executableBaseDir))
            return _executableBaseDir;

        string[] resourceNames = _assembly.GetManifestResourceNames();
        if (!resourceNames.Any(name => name.StartsWith($"{_assemblyName}.Assets.", StringComparison.Ordinal)))
            return _executableBaseDir;

        string cacheRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Verity",
            "RuntimeCache",
            GetRuntimeContentVersion());
        string markerPath = Path.Combine(cacheRoot, ".verity-runtime-cache");

        if (Directory.Exists(cacheRoot) && File.Exists(markerPath))
            return cacheRoot;

        if (Directory.Exists(cacheRoot))
            Directory.Delete(cacheRoot, true);

        Directory.CreateDirectory(cacheRoot);

        foreach (string resourceName in resourceNames)
        {
            if (!TryMapResourceToRelativePath(resourceName, out string relativePath))
                continue;

            using Stream? resourceStream = _assembly.GetManifestResourceStream(resourceName);
            if (resourceStream == null)
                continue;

            string outputPath = Path.Combine(cacheRoot, relativePath);
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);

            using FileStream fileStream = File.Create(outputPath);
            resourceStream.CopyTo(fileStream);
        }

        File.WriteAllText(markerPath, _assembly.GetName().Version?.ToString() ?? "runtime");
        return cacheRoot;
    }

    public string GetLoosePath(string relativePath)
    {
        string normalizedPath = NormalizeRelativePath(relativePath);
        return Path.Combine(_executableBaseDir, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
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

    private bool HasLooseRuntimeContent(string baseDir)
    {
        return Directory.Exists(Path.Combine(baseDir, "Assets")) ||
               File.Exists(Path.Combine(baseDir, "scene.json")) ||
               File.Exists(Path.Combine(baseDir, "UserScripts.dll"));
    }

    private string GetRuntimeContentVersion()
    {
        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
            {
                var info = new FileInfo(processPath);
                return $"{info.Length}_{info.LastWriteTimeUtc.Ticks}";
            }
        }
        catch
        {
        }

        return _assembly.ManifestModule.ModuleVersionId.ToString("N");
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
        if (!RuntimeContentPathMapper.TryConvertManifestSuffixToAssetPath(suffix, out string assetRelativePath))
            return false;

        relativePath = Path.Combine("Assets", assetRelativePath);
        return true;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        return relativePath.Replace('\\', '/').TrimStart('/');
    }
}
