using Hexa.NET.ImGui;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Editor.Windows;

public class BuildSettingsWindow : EditorWindow
{
    private readonly EditorApp _app;

    public BuildSettingsWindow(EditorApp app) : base("Build Settings")
    {
        _app = app;
        IsOpen = false;
    }

    public override void OnGui()
    {
        if (_app.ProjectPath == null || _app.AssetsPath == null) { ImGui.Text("No project loaded."); return; }
        var settings = _app.BuildSettings;

        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 1f, 1f), "Worlds in Build");
        ImGui.Separator();

        // 1. Current Build List
        if (ImGui.BeginChild("BuildListChild", new System.Numerics.Vector2(0, 200), ImGuiChildFlags.Borders)) {
            for (int i = 0; i < settings.Worlds.Count; i++) {
                ImGui.PushID(i);
                var worldRelPath = settings.Worlds[i];
                var fullPath = Path.Combine(_app.AssetsPath, worldRelPath);
                bool exists = File.Exists(fullPath);
                bool start = (settings.StartWorldIndex == i);

                if (!exists) ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f));
                else if (start) ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.4f, 1f, 0.4f, 1f));

                string label = exists ? $"[{i}] {worldRelPath}" : $"[{i}] (Missing) {worldRelPath}";
                if (ImGui.Selectable(label, start, ImGuiSelectableFlags.SpanAllColumns)) settings.StartWorldIndex = i;

                if (!exists || start) ImGui.PopStyleColor();

                ImGui.SameLine(ImGui.GetWindowWidth() - 100);
                if (ImGui.Button("Up") && i > 0) {
                    var tmp = settings.Worlds[i]; settings.Worlds[i] = settings.Worlds[i - 1]; settings.Worlds[i - 1] = tmp;
                    if (settings.StartWorldIndex == i) settings.StartWorldIndex = i - 1;
                    else if (settings.StartWorldIndex == i - 1) settings.StartWorldIndex = i;
                }
                ImGui.SameLine();
                if (ImGui.Button("X")) { settings.Worlds.RemoveAt(i); ImGui.PopID(); break; }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();

        if (ImGui.Button("Add Active World", new System.Numerics.Vector2(-1, 0))) {
            var active = WorldManager.ActiveWorld;
            if (active != null) {
                var file = Directory.GetFiles(_app.AssetsPath, $"{active.Name}.verity", SearchOption.AllDirectories).FirstOrDefault();
                if (file != null) AddToBuild(file);
            }
        }

        ImGui.Dummy(new System.Numerics.Vector2(0, 10));
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 1f, 1f), "All Project Worlds");
        ImGui.Separator();

        // 2. Available Scenes List
        if (ImGui.BeginChild("AvailableWorldsChild", new System.Numerics.Vector2(0, 0), ImGuiChildFlags.Borders)) {
            foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.verity", SearchOption.AllDirectories)) {
                string rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                if (ImGui.Selectable(rel)) AddToBuild(f);
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.Button("Save Settings", new System.Numerics.Vector2(-1, 40))) {
            _app.SaveBuildSettings();
        }

        ImGui.Dummy(new System.Numerics.Vector2(0, 20));
        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 1f, 1f), "Branding");
        ImGui.Separator();
        string logo = settings.LogoPath ?? "";
        if (ImGui.InputText("Build Logo Path", ref logo, 256)) settings.LogoPath = logo;
        ImGui.TextDisabled("(Rel. to Assets folder, e.g. Logo.png)");
    }

    private void AddToBuild(string fullPath)
    {
        if (_app.AssetsPath == null) return;
        var rel = Path.GetRelativePath(_app.AssetsPath, fullPath).Replace("\\", "/");
        if (!_app.BuildSettings.Worlds.Contains(rel)) _app.BuildSettings.Worlds.Add(rel);
    }
}
