using Hexa.NET.ImGui;
using System.Numerics;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Editor.Windows;

internal static class BuildSettingsEditorUi
{
    public static void Draw(EditorApp app)
    {
        if (app.ProjectPath == null || app.AssetsPath == null)
        {
            ImGui.Text(L10n.Tr("msg_no_project_loaded"));
            return;
        }

        var settings = app.BuildSettings;

        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1f), L10n.Tr("label_worlds_in_build"));
        ImGui.Separator();

        if (ImGui.BeginChild("BuildListChild", new Vector2(0, 200), ImGuiChildFlags.Borders))
        {
            for (int i = 0; i < settings.Worlds.Count; i++)
            {
                ImGui.PushID(i);
                string worldRelPath = settings.Worlds[i];
                string fullPath = Path.Combine(app.AssetsPath, worldRelPath);
                bool exists = File.Exists(fullPath);
                bool start = settings.StartWorldIndex == i;

                if (!exists)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
                else if (start)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.4f, 1f, 0.4f, 1f));

                string label = exists ? $"[{i}] {worldRelPath}" : $"[{i}] ({L10n.Tr("label_none")}) {worldRelPath}";
                if (ImGui.Selectable(label, start, ImGuiSelectableFlags.SpanAllColumns))
                    settings.StartWorldIndex = i;

                if (!exists || start)
                    ImGui.PopStyleColor();

                ImGui.SameLine(ImGui.GetWindowWidth() - 100);
                if (ImGui.Button(L10n.Tr("btn_up")) && i > 0)
                {
                    (settings.Worlds[i], settings.Worlds[i - 1]) = (settings.Worlds[i - 1], settings.Worlds[i]);
                    if (settings.StartWorldIndex == i)
                        settings.StartWorldIndex = i - 1;
                    else if (settings.StartWorldIndex == i - 1)
                        settings.StartWorldIndex = i;
                }

                ImGui.SameLine();
                if (ImGui.Button("X"))
                {
                    settings.Worlds.RemoveAt(i);
                    if (settings.Worlds.Count == 0)
                        settings.StartWorldIndex = 0;
                    else if (settings.StartWorldIndex >= settings.Worlds.Count)
                        settings.StartWorldIndex = settings.Worlds.Count - 1;

                    ImGui.PopID();
                    break;
                }

                ImGui.PopID();
            }
        }

        ImGui.EndChild();

        if (ImGui.Button(L10n.Tr("btn_add_active_world"), new Vector2(-1, 0)))
        {
            var active = WorldManager.ActiveWorld;
            if (active != null)
            {
                string? file = Directory.GetFiles(app.AssetsPath, $"{active.Name}.verity", SearchOption.AllDirectories).FirstOrDefault();
                if (file != null)
                    AddToBuild(app, file);
            }
        }

        ImGui.Dummy(new Vector2(0, 10));
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1f), L10n.Tr("label_all_project_worlds"));
        ImGui.Separator();

        if (ImGui.BeginChild("AvailableWorldsChild", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            foreach (string file in Directory.GetFiles(app.AssetsPath, "*.verity", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(app.AssetsPath, file).Replace("\\", "/");
                if (ImGui.Selectable(rel))
                    AddToBuild(app, file);
            }
        }

        ImGui.EndChild();

        ImGui.Separator();
        if (ImGui.Button(L10n.Tr("btn_save_settings"), new Vector2(-1, 40)))
            app.SaveBuildSettings();

        ImGui.Dummy(new Vector2(0, 20));
        ImGui.TextColored(new Vector4(0.7f, 0.7f, 1f, 1f), L10n.Tr("label_branding"));
        ImGui.Separator();
        string logo = settings.LogoPath ?? string.Empty;
        if (ImGui.InputText(L10n.Tr("label_logo_path"), ref logo, 256))
            settings.LogoPath = logo;
        ImGui.TextDisabled(L10n.Tr("label_logo_hint"));
    }

    private static void AddToBuild(EditorApp app, string fullPath)
    {
        if (app.AssetsPath == null)
            return;

        string rel = Path.GetRelativePath(app.AssetsPath, fullPath).Replace("\\", "/");
        if (!app.BuildSettings.Worlds.Contains(rel))
            app.BuildSettings.Worlds.Add(rel);
    }
}
