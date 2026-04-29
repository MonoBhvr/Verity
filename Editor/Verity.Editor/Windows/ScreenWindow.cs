using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Core.UI;

namespace Verity.Editor.Windows;

public class ScreenWindow : EditorWindow
{
    private const double IdleRenderIntervalSeconds = 0.125;
    private const float FloatingTitleBarHeight = 24f;
    private const float FloatingResizeHandleSize = 14f;

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
        var monitor = _app.Device.Window.GetPrimaryDisplayBounds();
        Vector2 logicalScreenSize = new(Math.Max(1, monitor.Width), Math.Max(1, monitor.Height));
        Vector2 previewSize = FitInside(contentSize, logicalScreenSize);
        Vector2 cursorPos = ImGui.GetCursorScreenPos();
        Vector2 previewOffset = new((contentSize.X - previewSize.X) * 0.5f, (contentSize.Y - previewSize.Y) * 0.5f);
        Vector2 previewMin = new(cursorPos.X + previewOffset.X, cursorPos.Y + previewOffset.Y);
        int width = (int)previewSize.X;
        int height = (int)previewSize.Y;

        if (width <= 0 || height <= 0)
            return;

        _app.RenderPipeline.EnsureScreenFbo(width, height);

        bool sizeChanged = width != _lastRenderedWidth || height != _lastRenderedHeight;
        var uiEditor = _app.GetWindow<UIEditorWindow>();
        bool overlayActive = uiEditor is { IsOpen: true, OverlayEnabled: true } && uiEditor.PreviewScreen != null;

        var world = WorldManager.ActiveWorld;
        bool multiWindowEnabled = _app.ProjectSettings.MultiWindowEnabled;
        if (world != null)
            _app.NormalizeCameraOutputsForProjectSettings(world);

        bool windowOutputsActive = multiWindowEnabled && world != null && CameraSelection.EnumerateActiveOutputs(world)
            .Any(static output => output.Target == CameraOutputTarget.Window && output.WindowVisible);
        bool shouldRender = ShouldRenderFrame(sizeChanged, overlayActive || windowOutputsActive);
        Camera? interactionCamera = null;
        if (shouldRender)
        {
            if (world != null)
            {
                _app.RenderPipeline.RenderCameraOutputs(world, includeWindowOutputs: multiWindowEnabled);
                var mainWindowCameras = windowOutputsActive
                    ? new List<Camera>()
                    : CameraSelection.EnumerateActiveOutputs(world)
                        .Where(static output => output.Target == CameraOutputTarget.MainWindow)
                        .OrderBy(static output => output.Order)
                        .Select(static output => output.Camera)
                        .Where(static camera => camera is { Enabled: true })
                        .Cast<Camera>()
                        .ToList();

                if (mainWindowCameras.Count > 0)
                {
                    long renderStart = Stopwatch.GetTimestamp();
                    for (int i = 0; i < mainWindowCameras.Count; i++)
                        _app.RenderPipeline.RenderWorld(world, mainWindowCameras[i], _app.RenderPipeline.ScreenFbo, clearTarget: i == 0);
                    _app.Profiler.RecordRenderStage("Screen Render", Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
                    interactionCamera = mainWindowCameras[0];
                }
                else
                {
                    var defaultCamera = windowOutputsActive ? null : CameraSelection.GetDefaultCamera(world);
                    if (defaultCamera != null)
                    {
                        long renderStart = Stopwatch.GetTimestamp();
                        _app.RenderPipeline.RenderWorld(world, defaultCamera, _app.RenderPipeline.ScreenFbo);
                        _app.Profiler.RecordRenderStage("Screen Render", Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
                        interactionCamera = defaultCamera;
                    }
                    else
                        _app.Device.Clear(new Verity.Core.Color(0.12f, 0.12f, 0.14f, 1f), _app.RenderPipeline.ScreenFbo);
                }
            }
            else
                _app.Device.Clear(new Verity.Core.Color(0.12f, 0.12f, 0.14f, 1f), _app.RenderPipeline.ScreenFbo);

            if (overlayActive)
            {
                long overlayStart = Stopwatch.GetTimestamp();
                UiRenderer.Render(_app.RenderPipeline, uiEditor!.PreviewScreen!, width, height, _app.RenderPipeline.ScreenFbo);
                _app.Profiler.RecordRenderStage("Screen Overlay UI", Stopwatch.GetElapsedTime(overlayStart).TotalMilliseconds);
            }

            _lastRenderedWidth = width;
            _lastRenderedHeight = height;
            _lastRenderSeconds = _renderStopwatch.Elapsed.TotalSeconds;
            _hasRenderedFrame = true;
        }

        var colorTex = _app.RenderPipeline.ScreenColorTexture;
        if (colorTex != null && colorTex.ImGuiTextureId != 0)
        {
            var drawList = ImGui.GetWindowDrawList();
            Vector2 contentMin = ImGui.GetCursorScreenPos();
            drawList.AddRectFilled(contentMin, new Vector2(contentMin.X + contentSize.X, contentMin.Y + contentSize.Y), ImGui.GetColorU32(new Vector4(0.02f, 0.022f, 0.026f, 1f)));
            drawList.AddRect(previewMin, new Vector2(previewMin.X + previewSize.X, previewMin.Y + previewSize.Y), ImGui.GetColorU32(new Vector4(0.22f, 0.28f, 0.36f, 1f)), 0f, ImDrawFlags.None, 1f);

            ImGui.SetCursorScreenPos(previewMin);
            unsafe
            {
                var texRef = new ImTextureRef(null, new ImTextureID(colorTex.ImGuiTextureId));
                ImGui.Image(texRef, previewSize, new Vector2(0, 1), new Vector2(1, 0));
            }

            if (interactionCamera != null)
                HandleInteraction(interactionCamera, ImGui.GetItemRectMin(), ImGui.GetItemRectSize());

            if (multiWindowEnabled && world != null)
                DrawFloatingCameraOutputWindows(world, ImGui.GetItemRectMin(), ImGui.GetItemRectSize(), logicalScreenSize);
        }
    }

    private bool ShouldRenderFrame(bool sizeChanged, bool overlayActive)
    {
        if (!_hasRenderedFrame || sizeChanged || overlayActive)
            return true;

        if (_app.IsPlaying)
            return _app.LastPlayLogicTicksThisFrame > 0;

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

    private void DrawFloatingCameraOutputWindows(World world, Vector2 screenMin, Vector2 previewSize, Vector2 logicalScreenSize)
    {
        var outputs = CameraSelection.EnumerateActiveOutputs(world)
            .Where(static output => output.Target == CameraOutputTarget.Window && output.WindowVisible)
            .OrderBy(static output => output.Order)
            .ToList();
        if (outputs.Count == 0)
            return;

        var drawList = ImGui.GetWindowDrawList();
        float scale = MathF.Min(previewSize.X / logicalScreenSize.X, previewSize.Y / logicalScreenSize.Y);
        for (int i = 0; i < outputs.Count; i++)
            DrawFloatingCameraOutputWindow(outputs[i], i, screenMin, previewSize, logicalScreenSize, scale, drawList);
    }

    private void DrawFloatingCameraOutputWindow(CameraOutput output, int index, Vector2 screenMin, Vector2 previewSize, Vector2 logicalScreenSize, float scale, ImDrawListPtr drawList)
    {
        string outputName = output.ResolveOutputName();
        if (!_app.RenderPipeline.TryGetCameraOutputTexture(outputName, out var texture) || texture.ImGuiTextureId == 0)
            return;

        Vector2 pos = ToNumerics(output.WindowPosition);
        Vector2 size = ToNumerics(output.WindowSize);
        if (size.X <= 1f || size.Y <= 1f)
            size = new Vector2(320f, 180f);

        size.X = Math.Max(96f, size.X);
        size.Y = Math.Max(72f, size.Y);

        Vector2 scaledPos = pos * scale;
        Vector2 scaledContentSize = size * scale;
        float titleBarHeight = output.WindowDecorated ? MathF.Max(12f, FloatingTitleBarHeight * scale) : 0f;
        Vector2 scaledFrameSize = new(scaledContentSize.X, scaledContentSize.Y + titleBarHeight);
        Vector2 min = screenMin + scaledPos;
        Vector2 max = min + scaledFrameSize;
        Vector2 clipMin = screenMin;
        Vector2 clipMax = screenMin + previewSize;
        if (max.X <= clipMin.X || max.Y <= clipMin.Y || min.X >= clipMax.X || min.Y >= clipMax.Y)
            return;

        drawList.PushClipRect(clipMin, clipMax, true);
        Vector2 titleMax = new(max.X, min.Y + titleBarHeight);
        Vector2 imageMin = new(min.X, titleMax.Y);
        Vector2 imageMax = imageMin + scaledContentSize;

        if (output.WindowDecorated)
        {
            uint bodyColor = ImGui.GetColorU32(new Vector4(0.03f, 0.035f, 0.045f, 0.92f));
            uint titleColor = ImGui.GetColorU32(new Vector4(0.08f, 0.095f, 0.12f, 0.96f));
            uint borderColor = ImGui.GetColorU32(new Vector4(0.65f, 0.72f, 0.82f, 0.82f));
            drawList.AddRectFilled(min, max, bodyColor, 6f);
            drawList.AddRectFilled(min, titleMax, titleColor, 6f);
            drawList.AddRect(min, max, borderColor, 6f, ImDrawFlags.None, 1.5f);

            string title = !string.IsNullOrWhiteSpace(output.OutputName)
                ? output.OutputName
                : output.Camera?.Owner?.Name ?? $"Camera Output {index + 1}";
            drawList.AddText(min + new Vector2(8f, 4f), ImGui.GetColorU32(ImGuiCol.Text), title);
        }

        unsafe
        {
            var texRef = new ImTextureRef(null, new ImTextureID(texture.ImGuiTextureId));
            drawList.AddImage(texRef, imageMin, imageMax, new Vector2(0, 1), new Vector2(1, 0));
        }

        drawList.PopClipRect();
        DrawFloatingWindowInteraction(output, index, screenMin, logicalScreenSize, scale, pos, size);
    }

    private void DrawFloatingWindowInteraction(CameraOutput output, int index, Vector2 screenMin, Vector2 logicalScreenSize, float scale, Vector2 pos, Vector2 size)
    {
        Vector2 scaledPos = pos * scale;
        Vector2 scaledContentSize = size * scale;
        float titleBarHeight = output.WindowDecorated ? MathF.Max(12f, FloatingTitleBarHeight * scale) : scaledContentSize.Y;
        Vector2 scaledFrameSize = output.WindowDecorated
            ? new Vector2(scaledContentSize.X, scaledContentSize.Y + titleBarHeight)
            : scaledContentSize;
        float resizeHandleSize = MathF.Max(8f, FloatingResizeHandleSize * scale);
        Vector2 titleMin = screenMin + scaledPos;
        Vector2 resizeMin = screenMin + scaledPos + scaledFrameSize - new Vector2(resizeHandleSize, resizeHandleSize);
        var io = ImGui.GetIO();

        ImGui.SetCursorScreenPos(titleMin);
        ImGui.InvisibleButton($"##camera-output-move-{index}", new Vector2(MathF.Max(1f, scaledFrameSize.X), titleBarHeight));
        if (ImGui.IsItemActivated())
            _app.BeginUndoAction();
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            Vector2 moved = pos + (new Vector2(io.MouseDelta.X, io.MouseDelta.Y) / MathF.Max(0.0001f, scale));
            output.WindowPosition = ToCore(moved);
            _app.MarkAsDirty();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            _app.EndUndoAction();

        ImGui.SetCursorScreenPos(resizeMin);
        ImGui.InvisibleButton($"##camera-output-resize-{index}", new Vector2(resizeHandleSize, resizeHandleSize));
        if (ImGui.IsItemActivated())
            _app.BeginUndoAction();
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            Vector2 resized = size + (new Vector2(io.MouseDelta.X, io.MouseDelta.Y) / MathF.Max(0.0001f, scale));
            resized.X = Math.Max(96f, resized.X);
            resized.Y = Math.Max(72f, resized.Y);

            if (output.WindowLockAspect)
            {
                float aspect = Math.Max(0.0001f, size.X / Math.Max(1f, size.Y));
                if (MathF.Abs(io.MouseDelta.X) >= MathF.Abs(io.MouseDelta.Y))
                    resized.Y = resized.X / aspect;
                else
                    resized.X = resized.Y * aspect;
                resized.X = Math.Max(96f, resized.X);
                resized.Y = Math.Max(72f, resized.Y);
            }

            output.WindowSize = ToCore(resized);
            _app.MarkAsDirty();
        }
        if (ImGui.IsItemDeactivatedAfterEdit())
            _app.EndUndoAction();
    }

    private static Vector2 ToNumerics(Verity.Core.Vector2 value) => new(value.X, value.Y);

    private static Verity.Core.Vector2 ToCore(Vector2 value) => new(value.X, value.Y);

    private static Vector2 FitInside(Vector2 container, Vector2 content)
    {
        if (container.X <= 0f || container.Y <= 0f || content.X <= 0f || content.Y <= 0f)
            return Vector2.Zero;

        float scale = MathF.Min(container.X / content.X, container.Y / content.Y);
        return new Vector2(MathF.Floor(content.X * scale), MathF.Floor(content.Y * scale));
    }

}
