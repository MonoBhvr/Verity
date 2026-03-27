using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Core.UI;

namespace Verity.Editor.Windows;

public class ScreenWindow : EditorWindow
{
    private readonly EditorApp _app;

    public ScreenWindow(EditorApp app) : base(L10n.Tr("window_screen"))
    {
        _app = app;
    }

    public override void OnGui()
    {
        var contentSize = ImGui.GetContentRegionAvail();
        int width = (int)contentSize.X;
        int height = (int)contentSize.Y;

        if (width <= 0 || height <= 0)
            return;

        _app.RenderPipeline.EnsureScreenFbo(width, height);

        var world = WorldManager.ActiveWorld;
        Camera? camera = null;
        if (world != null)
        {
            camera = FindWorldCamera(world);
            if (camera != null)
                _app.RenderPipeline.RenderWorld(world, camera, _app.RenderPipeline.ScreenFbo);
            else
                _app.Device.Clear(new Verity.Core.Color(0.12f, 0.12f, 0.14f, 1f), _app.RenderPipeline.ScreenFbo);
        }
        else
        {
            _app.Device.Clear(new Verity.Core.Color(0.12f, 0.12f, 0.14f, 1f), _app.RenderPipeline.ScreenFbo);
        }

        var uiEditor = _app.GetWindow<UIEditorWindow>();
        if (uiEditor is { IsOpen: true, OverlayEnabled: true } && uiEditor.PreviewScreen != null)
            UiRenderer.Render(_app.RenderPipeline, uiEditor.PreviewScreen, width, height, _app.RenderPipeline.ScreenFbo);

        var colorTex = _app.RenderPipeline.ScreenColorTexture;
        if (colorTex is OpenGlTexture glTex)
        {
            unsafe
            {
                var texRef = new ImTextureRef(null, new ImTextureID((nint)glTex.Id));
                ImGui.Image(texRef, contentSize, new Vector2(0, 1), new Vector2(1, 0));
            }

            if (camera != null)
                HandleInteraction(camera, ImGui.GetItemRectMin(), ImGui.GetItemRectSize());
        }
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_screen"); }

    private void HandleInteraction(Camera camera, Vector2 imgMin, Vector2 imgSize)
    {
        if (ImGui.IsItemHovered())
        {
            var mouseAbs = new Vector2(Verity.Input.Input.MousePosition.X, Verity.Input.Input.MousePosition.Y);
            var localMouse = mouseAbs - imgMin;
            _ = localMouse;
            _ = imgSize;
            _ = camera;
        }
    }

    private static Camera? FindWorldCamera(World world)
    {
        foreach (var entity in world.RootEntities)
        {
            var cam = FindCameraRecursive(entity);
            if (cam != null)
                return cam;
        }
        return null;
    }

    private static Camera? FindCameraRecursive(Entity entity)
    {
        if (!entity.Active) return null;

        var cam = entity.GetComponent<Camera>();
        if (cam != null && cam.Enabled)
            return cam;

        foreach (var child in entity.Transform.Children)
        {
            var found = FindCameraRecursive(child.Owner);
            if (found != null)
                return found;
        }
        return null;
    }
}
