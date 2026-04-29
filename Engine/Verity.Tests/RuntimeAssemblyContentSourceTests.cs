using System.Reflection;
using Verity.Game.Runtime;

namespace Verity.Tests;

public sealed class RuntimeAssemblyContentSourceTests : IDisposable
{
    private readonly string _runtimeRoot;

    public RuntimeAssemblyContentSourceTests()
    {
        _runtimeRoot = Path.Combine(Path.GetTempPath(), "VerityRuntimeContentTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_runtimeRoot);
    }

    [Fact]
    public void TryReadText_PrefersLooseFilesOverEmbeddedResources()
    {
        Directory.CreateDirectory(Path.Combine(_runtimeRoot, "Assets"));
        File.WriteAllText(Path.Combine(_runtimeRoot, "Assets", "BuildSettings.json"), "{\"source\":\"disk\"}");

        var contentSource = CreateContentSource();

        Assert.Equal("{\"source\":\"disk\"}", contentSource.TryReadText("Assets/BuildSettings.json"));
    }

    [Fact]
    public void TryReadText_FallsBackToEmbeddedResources()
    {
        var contentSource = CreateContentSource();

        Assert.Contains("\"source\":\"resource\"", contentSource.TryReadText("Assets/BuildSettings.json"));
        Assert.Contains("\"scene\":true", contentSource.TryReadText("scene.json"));
    }

    [Theory]
    [InlineData("NewLuaScript.lua", "NewLuaScript.lua")]
    [InlineData("NewLuaScript.lua.meta", "NewLuaScript.lua.meta")]
    [InlineData("NewRenderTexture.rendertexture", "NewRenderTexture.rendertexture")]
    [InlineData("NewRenderTexture.rendertexture.meta", "NewRenderTexture.rendertexture.meta")]
    [InlineData("Script.ChangeColor.cs", "Script\\ChangeColor.cs")]
    public void TryConvertManifestSuffixToAssetPath_PreservesKnownExtensions(string suffix, string expectedPath)
    {
        Assert.True(RuntimeContentPathMapper.TryConvertManifestSuffixToAssetPath(suffix, out string actualPath));
        Assert.Equal(expectedPath, actualPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_runtimeRoot))
            Directory.Delete(_runtimeRoot, recursive: true);
    }

    private RuntimeAssemblyContentSource CreateContentSource()
    {
        Assembly assembly = GetType().Assembly;
        return new RuntimeAssemblyContentSource(assembly, assembly.GetName().Name ?? "Verity.Tests", _runtimeRoot);
    }
}
