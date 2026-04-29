using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Tests;

public class CameraSelectionTests
{
    [Fact]
    public void GetDefaultCamera_PrefersMainCameraTag()
    {
        var world = new World("Test");
        var first = world.CreateEntity("First Camera");
        var firstCamera = first.AddComponent<Camera>();

        var main = world.CreateEntity("Main Camera");
        main.Tag = CameraSelection.MainCameraTag;
        var mainCamera = main.AddComponent<Camera>();

        Assert.Same(mainCamera, CameraSelection.GetDefaultCamera(world));
        Assert.NotSame(firstCamera, CameraSelection.GetDefaultCamera(world));
    }

    [Fact]
    public void GetDefaultCamera_PrefersMainWindowCameraOutput()
    {
        var world = new World("Test");
        var first = world.CreateEntity("First Camera");
        first.AddComponent<Camera>();

        var outputEntity = world.CreateEntity("Output Camera");
        var outputCamera = outputEntity.AddComponent<Camera>();
        outputEntity.AddComponent<CameraOutput>();

        Assert.Same(outputCamera, CameraSelection.GetDefaultCamera(world));
    }

    [Fact]
    public void GetDefaultCamera_IgnoresRenderTextureOutputForMainWindow()
    {
        var world = new World("Test");
        var textureOnly = world.CreateEntity("Texture Camera");
        textureOnly.AddComponent<Camera>();
        textureOnly.AddComponent<CameraOutput>().Target = CameraOutputTarget.RenderTexture;

        var main = world.CreateEntity("Main Camera");
        var mainCamera = main.AddComponent<Camera>();

        Assert.Same(mainCamera, CameraSelection.GetDefaultCamera(world));
    }

    [Fact]
    public void GetDefaultCamera_IgnoresWindowOutputForMainWindow()
    {
        var world = new World("Test");
        var windowOnly = world.CreateEntity("Window Camera");
        windowOnly.AddComponent<Camera>();
        windowOnly.AddComponent<CameraOutput>().Target = CameraOutputTarget.Window;

        var main = world.CreateEntity("Main Camera");
        var mainCamera = main.AddComponent<Camera>();

        Assert.Same(mainCamera, CameraSelection.GetDefaultCamera(world));
    }

    [Fact]
    public void GetDefaultCamera_FallsBackToFirstActiveCamera()
    {
        var world = new World("Test");
        var first = world.CreateEntity("First Camera");
        var firstCamera = first.AddComponent<Camera>();
        world.CreateEntity("Second Camera").AddComponent<Camera>();

        Assert.Same(firstCamera, CameraSelection.GetDefaultCamera(world));
    }

    [Fact]
    public void GetDefaultCamera_FallsBackToFirstOutputOnlyCameraWhenNoMainCandidateExists()
    {
        var world = new World("Test");
        var textureOnly = world.CreateEntity("Texture Camera");
        var textureCamera = textureOnly.AddComponent<Camera>();
        textureOnly.AddComponent<CameraOutput>().Target = CameraOutputTarget.RenderTexture;

        var windowOnly = world.CreateEntity("Window Camera");
        windowOnly.AddComponent<Camera>();
        windowOnly.AddComponent<CameraOutput>().Target = CameraOutputTarget.Window;

        Assert.Same(textureCamera, CameraSelection.GetDefaultCamera(world));
    }

    [Fact]
    public void GetDefaultCamera_SkipsInactiveAndDisabledCameras()
    {
        var world = new World("Test");

        var inactive = world.CreateEntity("Inactive Camera");
        inactive.Active = false;
        inactive.Tag = CameraSelection.MainCameraTag;
        inactive.AddComponent<Camera>();

        var disabled = world.CreateEntity("Disabled Camera");
        disabled.Tag = CameraSelection.MainCameraTag;
        disabled.AddComponent<Camera>().Enabled = false;

        var fallback = world.CreateEntity("Fallback Camera");
        var fallbackCamera = fallback.AddComponent<Camera>();

        Assert.Same(fallbackCamera, CameraSelection.GetDefaultCamera(world));
    }
}
