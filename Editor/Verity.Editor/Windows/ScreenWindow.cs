using System.Diagnostics;
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
    private const double IdleRenderIntervalSeconds = 0.125;

    private readonly EditorApp _app;
    private readonly Stopwatch _renderStopwatch = Stopwatch.StartNew();
    private double _lastRenderSeconds;
    private int _lastRenderedWidth;
    private int _lastRenderedHeight;
    private bool _hasRenderedFrame;

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

        bool sizeChanged = width != _lastRenderedWidth || height != _lastRenderedHeight;
        var uiEditor = _app.GetWindow<UIEditorWindow>();
        bool overlayActive = uiEditor is { IsOpen: true, OverlayEnabled: true } && uiEditor.PreviewScreen != null;
        bool shouldRender = ShouldRenderFrame(sizeChanged, overlayActive);

        var world = WorldManager.ActiveWorld;
        Camera? camera = null;
        if (shouldRender)
        {
            if (world != null)
            {
                camera = FindWorldCamera(world);
                if (camera != null)
                    _app.RenderPipeline.RenderWorld(world, camera, _app.RenderPipeline.ScreenFbo);
                else
                    _app.Device.Clear(new Verity.Core.Color(0.12f, 0.12f, 0.14f, 1f), _app.RenderPipeline.ScreenFbo);
            }
            else
                _app.Device.Clear(new Verity.Core.Color(0.12f, 0.12f, 0.14f, 1f), _app.RenderPipeline.ScreenFbo);

            if (overlayActive)
                UiRenderer.Render(_app.RenderPipeline, uiEditor!.PreviewScreen!, width, height, _app.RenderPipeline.ScreenFbo);

            _lastRenderedWidth = width;
            _lastRenderedHeight = height;
            _lastRenderSeconds = _renderStopwatch.Elapsed.TotalSeconds;
            _hasRenderedFrame = true;
        }

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

    private bool ShouldRenderFrame(bool sizeChanged, bool overlayActive)
    {
        if (!_hasRenderedFrame || sizeChanged || _app.IsPlaying || overlayActive)
            return true;

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows))
            return true;

        return _renderStopwatch.Elapsed.TotalSeconds - _lastRenderSeconds >= IdleRenderIntervalSeconds;
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
