using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Graphics;

public static class CameraSelection
{
    public const string MainCameraTag = "MainCamera";

    public static Camera? GetDefaultCamera(World? world)
    {
        if (world == null)
            return null;

        var outputCamera = GetMainWindowCamera(world);
        if (outputCamera != null)
            return outputCamera;

        Camera? firstActiveCamera = null;
        Camera? firstOutputOnlyCamera = null;
        foreach (var camera in EnumerateActiveCameras(world))
        {
            if (IsNonMainOutputCamera(camera))
            {
                firstOutputOnlyCamera ??= camera;
                continue;
            }

            firstActiveCamera ??= camera;
            if (camera.Owner.Tag == MainCameraTag)
                return camera;
        }

        return firstActiveCamera ?? firstOutputOnlyCamera;
    }

    private static bool IsNonMainOutputCamera(Camera camera)
    {
        var output = camera.Owner.GetComponent<CameraOutput>();
        return output is { Enabled: true } && output.Target != CameraOutputTarget.MainWindow;
    }

    public static Camera? GetMainWindowCamera(World? world)
    {
        if (world == null)
            return null;

        Camera? firstOutputCamera = null;
        Camera? primaryOutputCamera = null;

        foreach (var output in EnumerateActiveOutputs(world)
                     .Where(static output => output.Target == CameraOutputTarget.MainWindow)
                     .OrderBy(static output => output.Order))
        {
            var camera = output.Camera;
            if (camera == null || !camera.Enabled)
                continue;

            firstOutputCamera ??= camera;
            if (output.Primary)
                primaryOutputCamera ??= camera;

            if (camera.Owner.Tag == MainCameraTag)
                return camera;
        }

        return primaryOutputCamera ?? firstOutputCamera;
    }

    public static IEnumerable<Camera> EnumerateActiveCameras(World world)
    {
        foreach (var root in world.RootEntities)
        {
            foreach (var camera in EnumerateActiveCameras(root))
                yield return camera;
        }
    }

    private static IEnumerable<Camera> EnumerateActiveCameras(Entity entity)
    {
        if (!entity.Active)
            yield break;

        var camera = entity.GetComponent<Camera>();
        if (camera is { Enabled: true })
            yield return camera;

        foreach (var child in entity.Transform.Children)
        {
            foreach (var childCamera in EnumerateActiveCameras(child.Owner))
                yield return childCamera;
        }
    }

    public static IEnumerable<CameraOutput> EnumerateActiveOutputs(World world)
    {
        foreach (var root in world.RootEntities)
        {
            foreach (var output in EnumerateActiveOutputs(root))
                yield return output;
        }
    }

    private static IEnumerable<CameraOutput> EnumerateActiveOutputs(Entity entity)
    {
        if (!entity.Active)
            yield break;

        var output = entity.GetComponent<CameraOutput>();
        if (output is { Enabled: true })
            yield return output;

        foreach (var child in entity.Transform.Children)
        {
            foreach (var childOutput in EnumerateActiveOutputs(child.Owner))
                yield return childOutput;
        }
    }
}
