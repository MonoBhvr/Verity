using Hexa.NET.ImGui;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Editor.Windows;

public class BuildSettingsWindow : EditorWindow
{
    private readonly EditorApp _app;
    private BuildSettings? _settings;
    private string? _settingsPath;

    public BuildSettingsWindow(EditorApp app) : base("Build Settings")
    {
        _app = app;
        IsOpen = false;
    }

    private void EnsureSettingsLoaded()
    {
        if (_app.ProjectPath == null) return;
        var currentPath = Path.Combine(_app.ProjectPath, "BuildSettings.json");
        if (_settingsPath != currentPath) {
            _settingsPath = currentPath;
            _settings = BuildSettings.Load(_settingsPath);
        }
    }

    public override void OnGui()
    {
        if (_app.ProjectPath == null || _app.AssetsPath == null) { ImGui.Text("No project loaded."); return; }
        EnsureSettingsLoaded();
        if (_settings == null) return;

        ImGui.TextColored(new System.Numerics.Vector4(0.7f, 0.7f, 1f, 1f), "Worlds in Build");
        ImGui.Separator();

        // 1. Current Build List
        // Fix: ALWAYS call EndChild if BeginChild was called.
        if (ImGui.BeginChild(1, new System.Numerics.Vector2(0, 200), ImGuiChildFlags.Borders)) {
            for (int i = 0; i < _settings.Worlds.Count; i++) {
                ImGui.PushID(i);
                var worldRelPath = _settings.Worlds[i];
                var fullPath = Path.Combine(_app.AssetsPath, worldRelPath);
                bool exists = File.Exists(fullPath);
                bool start = (_settings.StartWorldIndex == i);

                if (!exists) ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f));
                else if (start) ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(0.4f, 1f, 0.4f, 1f));

                string label = exists ? $"[{i}] {worldRelPath}" : $"[{i}] (Missing) {worldRelPath}";
                ImGui.Text(label);

                if (!exists || start) ImGui.PopStyleColor();

                ImGui.SameLine(ImGui.GetWindowWidth() - 100);
                if (ImGui.Button("Up") && i > 0) {
                    var tmp = _settings.Worlds[i]; _settings.Worlds[i] = _settings.Worlds[i - 1]; _settings.Worlds[i - 1] = tmp;
                }
                ImGui.SameLine();
                if (ImGui.Button("X")) { _settings.Worlds.RemoveAt(i); ImGui.PopID(); break; }
                if (ImGui.Selectable("##row", start, ImGuiSelectableFlags.SpanAllColumns)) _settings.StartWorldIndex = i;
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
        if (ImGui.BeginChild(2, new System.Numerics.Vector2(0, 0), ImGuiChildFlags.Borders)) {
            foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.verity", SearchOption.AllDirectories)) {
                if (ImGui.Selectable(Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/"))) AddToBuild(f);
            }
        }
        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.Button("Save Settings", new System.Numerics.Vector2(-1, 40)) && _settingsPath != null) {
            _settings.Save(_settingsPath);
        }
    }

    private void AddToBuild(string fullPath)
    {
        if (_settings == null || _app.AssetsPath == null) return;
        var rel = Path.GetRelativePath(_app.AssetsPath, fullPath).Replace("\\", "/");
        if (!_settings.Worlds.Contains(rel)) _settings.Worlds.Add(rel);
    }
}
