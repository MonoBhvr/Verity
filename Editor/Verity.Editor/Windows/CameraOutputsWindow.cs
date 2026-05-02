using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public sealed class CameraOutputsWindow : EditorWindow
{
    private readonly EditorApp _app;

    public CameraOutputsWindow(EditorApp app) : base(L10n.Tr("window_camera_outputs"))
    {
        _app = app;
    }

    public override void OnGui()
    {
        var world = WorldManager.ActiveWorld;
        if (!_app.ProjectSettings.MultiWindowEnabled)
        {
            ImGui.TextDisabled(L10n.Tr("msg_multi_window_disabled"));
            return;
        }

        if (world == null)
        {
            ImGui.TextDisabled(L10n.Tr("msg_no_active_world"));
            return;
        }

        var outputs = CameraSelection.EnumerateActiveOutputs(world)
            .Where(static output => output.Target == CameraOutputTarget.Window)
            .OrderBy(static output => output.Order)
            .ToList();

        if (outputs.Count == 0)
        {
            ImGui.TextDisabled(L10n.Tr("msg_no_camera_window_outputs"));
            return;
        }

        _app.RenderPipeline.RenderCameraOutputs(world, includeWindowOutputs: true);

        foreach (var output in outputs)
        {
            DrawOutput(output);
            ImGui.Separator();
        }
    }

    public override void RefreshTitle()
    {
        Title = L10n.Tr("window_camera_outputs");
    }

    private void DrawOutput(CameraOutput output)
    {
        string outputName = output.ResolveOutputName();
        string label = !string.IsNullOrWhiteSpace(output.OutputName)
            ? output.OutputName
            : output.Camera?.Owner?.Name ?? outputName;

        ImGui.Text(label);
        ImGui.SameLine();

        if (!_app.RenderPipeline.TryGetCameraOutputTexture(outputName, out var texture) ||
            texture.ImGuiTextureId == 0)
        {
            ImGui.TextDisabled(L10n.Tr("msg_camera_output_not_rendered"));
            return;
        }

        ImGui.TextDisabled($"{Math.Max(1, texture.Width)} x {Math.Max(1, texture.Height)}");

        Vector2 available = ImGui.GetContentRegionAvail();
        float maxWidth = Math.Max(1f, available.X);
        float maxHeight = Math.Max(120f, available.Y);
        float aspect = texture.Height > 0 ? texture.Width / (float)texture.Height : 1f;
        Vector2 size = new(maxWidth, maxWidth / Math.Max(0.0001f, aspect));
        if (size.Y > maxHeight)
            size = new Vector2(maxHeight * aspect, maxHeight);

        unsafe
        {
            var texRef = new ImTextureRef(null, new ImTextureID(texture.ImGuiTextureId));
            ImGui.Image(texRef, size, new Vector2(0, 1), new Vector2(1, 0));
        }
    }
}
