using System.Collections;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public unsafe class InspectorWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string _searchFilter = "";

    public InspectorWindow(EditorApp app) : base("Inspector") { _app = app; }

    public override void OnGui()
    {
        var entity = EditorSelection.SelectedEntity;
        if (entity != null)
        {
            DrawEntityInspector(entity);
            return;
        }

        var assetPath = EditorSelection.SelectedAssetPath;
        if (assetPath != null)
        {
            DrawAssetInspector(assetPath);
            return;
        }

        ImGui.Text("Select an Entity or Asset to inspect.");
    }

    private void DrawEntityInspector(Entity entity)
    {
        ImGui.PushID("EntityHeader");
        bool active = entity.Active;
        if (ImGui.Checkbox("##Active", ref active)) entity.Active = active;
        ImGui.SameLine();
        string name = entity.Name;
        if (ImGui.InputText("##Name", ref name, 128)) entity.Name = name;
        ImGui.PopID();

        ImGui.Separator();

        var components = entity.GetAllComponents();
        for (int i = 0; i < components.Count; i++)
        {
            DrawComponent(components[i], entity);
        }

        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button("Add Component", new Vector2(-1, 30))) ImGui.OpenPopup("AddComponentPopup");

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            var types = _app.ScriptCompiler?.GetAllAddableComponentTypes() ?? new List<Type>();
            foreach (var type in types)
            {
                if (ImGui.MenuItem(type.Name))
                {
                    entity.AddComponent(type);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }
    }

    private void DrawAssetInspector(string path)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path).ToLower();

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"Asset: {fileName}");
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 5));

        if (fileName == "ProjectSettings.json")
        {
            DrawProjectSettingsInspector();
        }
        else if (fileName == "BuildSettings.json")
        {
            DrawBuildSettingsInspector();
        }
        else if (extension == ".cs")
        {
            DrawScriptPreview(path);
        }
        else if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
        {
            DrawImagePreview(path);
        }
        else if (extension == ".verity")
        {
            DrawWorldSettingsInspector(path);
        }
        else
        {
            ImGui.Text($"Type: {extension}");
            ImGui.Text($"Path: {path}");
        }
    }

    private void DrawProjectSettingsInspector()
    {
        var settings = _app.ProjectSettings;
        ImGui.Text("Global Project Configuration");
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 5));

        int tps = settings.TargetTPS;
        if (ImGui.DragInt("Target TPS", ref tps, 1, 1, 1000)) {
            settings.TargetTPS = tps;
            _app.SaveProjectSettings();
        }

        int ptps = settings.TargetPTPS;
        if (ImGui.DragInt("Physics TPS", ref ptps, 1, 1, 1000)) {
            settings.TargetPTPS = ptps;
            _app.SaveProjectSettings();
        }

        float fsize = settings.EditorFontSize;
        if (ImGui.DragFloat("Editor Font Size", ref fsize, 0.5f, 8f, 72f)) {
            settings.EditorFontSize = fsize;
            _app.SaveProjectSettings();
        }
        ImGui.TextDisabled("(Requires restart to apply font size changes)");

        if (ImGui.Button("Save Project Settings", new Vector2(-1, 30))) {
            _app.SaveProjectSettings();
        }
    }

    private void DrawBuildSettingsInspector()
    {
        var settings = _app.BuildSettings;
        ImGui.Text("Build Configuration");
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 5));

        string bLogo = settings.LogoPath ?? "";
        if (ImGui.InputText("Build Logo Path", ref bLogo, 256)) {
            settings.LogoPath = bLogo;
            _app.SaveBuildSettings();
        }
        ImGui.TextDisabled("(Rel. to Assets folder, e.g. Logo.png)");

        ImGui.Separator();
        ImGui.Text("Worlds in Build (Read Only):");
        if (ImGui.BeginChild("BuildWorldsList", new Vector2(0, 150), ImGuiChildFlags.Borders))
        {
            for (int i = 0; i < settings.Worlds.Count; i++)
            {
                bool isStart = settings.StartWorldIndex == i;
                if (isStart) ImGui.TextColored(new Vector4(0.4f, 1.0f, 0.4f, 1.0f), $"* [{i}] {settings.Worlds[i]}");
                else ImGui.Text($"  [{i}] {settings.Worlds[i]}");
            }
        }
        ImGui.EndChild();

        ImGui.TextDisabled("Use 'Build Settings' window to edit this list.");
    }

    private void DrawWorldSettingsInspector(string path)
    {
        // To edit a world without loading it as the active world, we'd need to deserialize it temporarily.
        // For simplicity, if it's the active world, we edit live. If not, we show a button to load it.
        var world = WorldManager.ActiveWorld;
        bool isActive = world != null && string.Equals(world.Name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase);

        if (isActive && world != null)
        {
            ImGui.Text("Active World Settings");
            ImGui.Separator();
            
            bool custom = world.UseCustomSettings;
            if (ImGui.Checkbox("Use Custom Settings", ref custom)) world.UseCustomSettings = custom;

            if (custom)
            {
                int wtps = world.CustomTPS;
                if (ImGui.DragInt("TPS Override", ref wtps, 1, 1, 1000)) world.CustomTPS = wtps;

                int wptps = world.CustomPTPS;
                if (ImGui.DragInt("Physics TPS Override", ref wptps, 1, 1, 1000)) world.CustomPTPS = wptps;
            }
            
            if (ImGui.Button("Save World", new Vector2(-1, 30))) {
                _app.GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset();
            }
        }
        else
        {
            ImGui.Text("Selected World (Not Active)");
            if (ImGui.Button("Load World for Editing", new Vector2(-1, 40)))
            {
                _app.GetWindow<ProjectWindow>()?.LoadWorldByPath(path);
            }
        }
    }

    private void DrawScriptPreview(string path)
    {
        try {
            string code = File.ReadAllText(path);
            ImGui.Text("Source Code:");
            ImGui.InputTextMultiline("##code", ref code, (uint)code.Length + 1024, new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly);
        } catch (Exception e) {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error reading file: {e.Message}");
        }
    }

    private void DrawImagePreview(string path)
    {
        var tex = _app.TextureManager.Load(path);
        if (tex is Irodori.Backend.OpenGL.OpenGlTexture glTex)
        {
            float width = glTex.Width;
            float height = glTex.Height;
            ImGui.Text($"Size: {width}x{height}");
            
            float availWidth = ImGui.GetContentRegionAvail().X;
            float scale = Math.Min(1.0f, availWidth / width);
            Vector2 displaySize = new Vector2(width * scale, height * scale);

            unsafe {
                var texRef = new ImTextureRef(null, new ImTextureID((nint)glTex.Id));
                ImGui.Image(texRef, displaySize, new Vector2(0, 1), new Vector2(1, 0));
            }
        }
        else
        {
            ImGui.Text("Failed to load image for preview.");
        }
    }

    private void DrawComponent(Component component, Entity entity)
    {
        var type = component.GetType();
        ImGui.PushID(component.GetHashCode());

        bool open = ImGui.CollapsingHeader(type.Name, ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem($"{type.FullName}_Context"))
        {
            if (type != typeof(Transform) && ImGui.MenuItem("Remove Component"))
            {
                entity.RemoveComponent(component);
                ImGui.EndPopup();
                ImGui.PopID();
                return;
            }
            ImGui.EndPopup();
        }

        if (open)
        {
            ImGui.Indent();
            DrawComponentFields(component);
            ImGui.Unindent();
        }
        
        ImGui.PopID();
    }

    private void DrawComponentFields(Component component)
    {
        var type = component.GetType();
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

        foreach (var field in fields)
        {
            if (ShouldShowMember(field))
                ProcessMember(field.Name, field.FieldType, field.GetValue(component), val => field.SetValue(component, val), field);
        }

        foreach (var prop in props)
        {
            if (prop.DeclaringType == typeof(Component)) continue;
            if (ShouldShowMember(prop))
                ProcessMember(prop.Name, prop.PropertyType, prop.GetValue(component), val => prop.SetValue(component, val), prop);
        }
    }

    private bool ShouldShowMember(MemberInfo member)
    {
        var attributes = member.GetCustomAttributes(true);
        
        // 1. HideInInspector always wins (Check by name to avoid namespace ambiguity)
        if (attributes.Any(a => a.GetType().Name == "HideInInspectorAttribute")) return false;

        // 2. Explicitly serialized wins
        if (attributes.Any(a => a.GetType().Name == "SerializeFieldAttribute")) return true;

        // 3. Otherwise, check visibility
        if (member is FieldInfo f) return f.IsPublic;
        if (member is PropertyInfo p) return p.GetGetMethod()?.IsPublic ?? false;

        return false;
    }

    private void ProcessMember(string name, Type type, object? value, Action<object?> onUpdate, MemberInfo member)
    {
        if (type == typeof(Sprite)) DrawSpriteField(name, (Sprite?)value ?? default, onUpdate);
        else if (member.GetCustomAttribute<AssetReferenceAttribute>() != null && type == typeof(string)) DrawAssetReferenceField(name, (string?)value, member.GetCustomAttribute<AssetReferenceAttribute>()!.Extension, onUpdate);
        else if (typeof(Component).IsAssignableFrom(type)) DrawComponentReferenceField(name, (Component?)value, type, onUpdate);
        else {
            // Special handling for Camera Aspect Ratio properties to make them clearer
            if (member.DeclaringType == typeof(Camera) && (name == "AspectWidth" || name == "AspectHeight")) {
                ImGui.PushID(name);
                ImGui.Columns(2); ImGui.SetColumnWidth(0, 100); ImGui.Text(name); ImGui.NextColumn();
                float val = (float)(value ?? 0f);
                if (ImGui.DragFloat("##v", ref val, 0.1f, 0.001f, 1000f)) onUpdate(val);
                ImGui.Columns(1);
                ImGui.PopID();
            } else {
                DrawField(name, value, onUpdate);
            }
        }
    }

    private void DrawField(string name, object? value, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 100);
        ImGui.Text(name);
        ImGui.NextColumn();

        bool changed = false;
        if (value is float f) { if (ImGui.DragFloat("##v", ref f, 0.1f)) { changed = true; value = f; } }
        else if (value is int i) { if (ImGui.DragInt("##v", ref i)) { changed = true; value = i; } }
        else if (value is bool b) { if (ImGui.Checkbox("##v", ref b)) { changed = true; value = b; } }
        else if (value is string s) { s ??= ""; if (ImGui.InputText("##v", ref s, 1024)) { changed = true; value = s; } }
        else if (value is Vector2 v2) { if (ImGui.DragFloat2("##v", ref v2, 0.1f)) { changed = true; value = v2; } }
        else if (value is Vector3 v3) { if (ImGui.DragFloat3("##v", ref v3, 0.1f)) { changed = true; value = v3; } }
        else if (value is Verity.Core.Color color)
        {
            var v4 = (Vector4)color;
            if (ImGui.ColorEdit4("##v", ref v4, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar)) { changed = true; value = (Verity.Core.Color)v4; }
        }
        else if (value is Enum e)
        {
            string[] names = Enum.GetNames(e.GetType());
            int current = Array.IndexOf(names, e.ToString());
            if (ImGui.Combo("##v", ref current, names, names.Length)) { changed = true; value = Enum.Parse(e.GetType(), names[current]); }
        }
        else { ImGui.TextDisabled(value?.ToString() ?? "null"); }

        if (ImGui.IsItemActivated()) _app.BeginUndoAction();
        if (changed) onUpdate(value);
        if (ImGui.IsItemDeactivatedAfterEdit()) _app.EndUndoAction();

        ImGui.Columns(1);
        ImGui.PopID();
    }

    private unsafe void DrawSpriteField(string name, Sprite current, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 100);
        ImGui.Text(name);
        ImGui.NextColumn();

        string display = string.IsNullOrEmpty(current.Path) ? "None (Sprite)" : Path.GetFileName(current.Path);
        if (ImGui.Button($"{display}##box", new Vector2(-25, 0))) { /* Future: Focus asset in Project Window */ }
        
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if ((nint)payload.Handle != 0 && EditorSelection.DraggedAssetPath != null) {
                string ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg") onUpdate((Sprite)EditorSelection.DraggedAssetPath);
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("SpritePicker");

        if (ImGui.BeginPopup("SpritePicker"))
        {
            ImGui.InputText("Search", ref _searchFilter, 64);
            if (ImGui.MenuItem("None")) { onUpdate(default(Sprite)); ImGui.CloseCurrentPopup(); }
            if (_app.AssetsPath != null)
            {
                foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") continue;
                    string rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                    if (string.IsNullOrEmpty(_searchFilter) || rel.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ImGui.MenuItem(rel)) { onUpdate((Sprite)f); ImGui.CloseCurrentPopup(); }
                    }
                }
            }
            ImGui.EndPopup();
        }

        ImGui.Columns(1);
        ImGui.PopID();
    }

    private unsafe void DrawAssetReferenceField(string name, string? currentPath, string extensions, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 100);
        ImGui.Text(name);
        ImGui.NextColumn();

        string display = string.IsNullOrEmpty(currentPath) ? "None (Asset)" : Path.GetFileName(currentPath);
        ImGui.Button($"{display}##box", new Vector2(-25, 0));
        
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if ((nint)payload.Handle != 0 && EditorSelection.DraggedAssetPath != null) {
                string ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower();
                if (string.IsNullOrEmpty(extensions) || extensions.Split(';').Any(e => e.Trim().ToLower() == ext)) onUpdate(EditorSelection.DraggedAssetPath);
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("AssetPicker");

        if (ImGui.BeginPopup("AssetPicker"))
        {
            ImGui.InputText("Search", ref _searchFilter, 64);
            if (ImGui.MenuItem("None")) { onUpdate(null); ImGui.CloseCurrentPopup(); }
            if (_app.AssetsPath != null)
            {
                string[] exts = extensions.Split(';').Select(e => e.Trim().ToLower()).ToArray();
                foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.*", SearchOption.AllDirectories))
                {
                    if (exts.Length > 0 && !exts.Contains(Path.GetExtension(f).ToLower())) continue;
                    string rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                    if (string.IsNullOrEmpty(_searchFilter) || rel.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ImGui.MenuItem(rel)) { onUpdate(f); ImGui.CloseCurrentPopup(); }
                    }
                }
            }
            ImGui.EndPopup();
        }

        ImGui.Columns(1);
        ImGui.PopID();
    }

    private unsafe void DrawComponentReferenceField(string name, Component? current, Type targetType, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 100);
        ImGui.Text(name);
        ImGui.NextColumn();

        string display = current == null ? $"None ({targetType.Name})" : $"{current.Owner.Name} ({targetType.Name})";
        ImGui.Button($"{display}##box", new Vector2(-25, 0));

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITY");
            if ((nint)payload.Handle != 0 && EditorSelection.DraggedEntity != null) {
                var comp = EditorSelection.DraggedEntity.GetComponent(targetType);
                if (comp != null) {
                    onUpdate(comp);
                    EditorSelection.DraggedEntity = null; // Consume
                }
            }
            ImGui.EndDragDropTarget();
        }

        ImGui.SameLine();
        if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("ComponentPicker");

        if (ImGui.BeginPopup("ComponentPicker"))
        {
            ImGui.InputText("Search", ref _searchFilter, 64);
            if (ImGui.MenuItem("None")) { onUpdate(null); ImGui.CloseCurrentPopup(); }
            
            var world = WorldManager.ActiveWorld;
            if (world != null)
            {
                foreach (var entity in world.GetAllEntities())
                {
                    var comp = entity.GetComponent(targetType);
                    if (comp != null)
                    {
                        if (string.IsNullOrEmpty(_searchFilter) || entity.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            if (ImGui.MenuItem($"{entity.Name} ({targetType.Name})"))
                            {
                                onUpdate(comp);
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }
                }
            }
            ImGui.EndPopup();
        }

        ImGui.Columns(1);
        ImGui.PopID();
    }
}
