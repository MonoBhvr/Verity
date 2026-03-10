using System.Collections;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Linq;
using System.IO;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Input;
using Verity.Editor;
using Irodori.Backend.OpenGL;
using Verity.Core.Physics;

namespace Verity.Editor.Windows;

public unsafe class InspectorWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string _searchFilter = "";

    // Filter Editor State
    private string _newFilterName = "NewFilter";
    private string _newEnumTypeName = "Verity.Input.KeyCode, Verity.Input";
    private FilterMode _newFilterMode = FilterMode.Whitelist;
    private bool _createAsMixed = true;
    private Filter? _selectedFilter;
    private string _editValueBuffer = "";
    private string _editValueTypeBuffer = "Verity.Input.KeyCode, Verity.Input";

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
                    _app.BeginUndoAction();
                    entity.AddComponent(type);
                    _app.EndUndoAction();
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
            ImGui.Text("Global Project Configuration");
            ImGui.Separator();
            DrawGenericInspector(_app.ProjectSettings, () => _app.SaveProjectSettings());
        }
        else if (fileName == "BuildSettings.json")
        {
            ImGui.Text("Build Configuration");
            ImGui.Separator();
            DrawGenericInspector(_app.BuildSettings, () => _app.SaveBuildSettings());
        }
        else if (fileName == "Filters.json")
        {
            FilterEditorWindow.DrawFilterEditor(ref _selectedFilter, ref _newFilterName, ref _newEnumTypeName, ref _newFilterMode, ref _createAsMixed, ref _editValueBuffer, ref _editValueTypeBuffer);
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

    private void DrawWorldSettingsInspector(string path)
    {
        var world = WorldManager.ActiveWorld;
        bool isActive = world != null && string.Equals(world.Name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase);

        if (isActive && world != null)
        {
            ImGui.Text("Active World Settings");
            ImGui.Separator();
            DrawGenericInspector(world);
            
            ImGui.Dummy(new Vector2(0, 10));
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
        if (tex is OpenGlTexture glTex)
        {
            float width = glTex.Width;
            float height = glTex.Height;
            ImGui.Text($"Size: {width}x{height}");
            
            float availWidth = ImGui.GetContentRegionAvail().X;
            float scale = Math.Min(1.0f, availWidth / width);
            Vector2 displaySize = new Vector2(width * scale, height * scale);

            unsafe {
                ImTextureID id = new((nint)glTex.Id);
                var texRef = new ImTextureRef(null, id);
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
                _app.BeginUndoAction();
                entity.RemoveComponent(component);
                _app.EndUndoAction();
                ImGui.EndPopup();
                ImGui.PopID();
                return;
            }
            ImGui.EndPopup();
        }

        if (open)
        {
            ImGui.Indent();
            
            // Add "Edit Collider" button for PolygonShape
            if (component is PolygonShape poly)
            {
                bool isEditing = EditorSelection.IsEditingCollider && EditorSelection.SelectedEntity == entity;
                if (isEditing) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1.0f, 1.0f));
                if (ImGui.Button(isEditing ? "Exit Edit Collider" : "Edit Collider", new Vector2(-1, 25)))
                {
                    EditorSelection.IsEditingCollider = !isEditing;
                }
                if (isEditing) ImGui.PopStyleColor();
                ImGui.TextDisabled("Hold Ctrl to Remove vertex, Click edge to Add vertex.");
            }

            DrawComponentFields(component);
            ImGui.Unindent();
        }
        
        ImGui.PopID();
    }

    private void DrawComponentFields(Component component)
    {
        DrawGenericInspector(component);
    }

    private void DrawGenericInspector(object target, Action? onUpdate = null)
    {
        var type = target.GetType();
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        .Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0);

        foreach (var field in fields)
        {
            if (ShouldShowMember(field))
                ProcessMember(field.Name, field.FieldType, field.GetValue(target), val => { field.SetValue(target, val); onUpdate?.Invoke(); }, field);
        }

        foreach (var prop in props)
        {
            if (prop.DeclaringType == typeof(Component)) continue;
            if (ShouldShowMember(prop))
                ProcessMember(prop.Name, prop.PropertyType, prop.GetValue(target), val => { prop.SetValue(target, val); onUpdate?.Invoke(); }, prop);
        }
    }

    private bool ShouldShowMember(MemberInfo member)
    {
        var attributes = member.GetCustomAttributes(true);
        
        if (attributes.Any(a => a.GetType().Name == "HideInInspectorAttribute")) return false;
        if (attributes.Any(a => a.GetType().Name == "SerializeFieldAttribute")) return true;

        if (member is FieldInfo f) return f.IsPublic;
        if (member is PropertyInfo p) return p.GetGetMethod()?.IsPublic ?? false;

        return false;
    }

    private void ProcessMember(string name, Type type, object? value, Action<object?> onUpdate, MemberInfo member)
    {
        if (type == typeof(Sprite)) DrawSpriteField(name, (Sprite?)value ?? default, onUpdate);
        else if (type == typeof(Filter)) DrawFilterField(name, (Filter?)value, onUpdate);
        else if (member.GetCustomAttribute<AssetReferenceAttribute>() != null && type == typeof(string)) DrawAssetReferenceField(name, (string?)value, member.GetCustomAttribute<AssetReferenceAttribute>()!.Extension, onUpdate);
        else if (typeof(Component).IsAssignableFrom(type)) DrawComponentReferenceField(name, (Component?)value, type, onUpdate);
        else if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType) DrawList(name, (IList?)value, type.GetGenericArguments()[0], onUpdate);
        else if (type == typeof(float?)) DrawNullableFloat(name, (float?)value, onUpdate, member);
        else {
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

    private void DrawNullableFloat(string name, float? value, Action<object?> onUpdate, MemberInfo member)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 120);
        ImGui.Text(name);
        ImGui.NextColumn();

        bool hasValue = value.HasValue;
        if (ImGui.Checkbox($"##has_{name}", ref hasValue))
        {
            if (hasValue) onUpdate(GetInheritedValue(name, member));
            else onUpdate(null);
        }
        ImGui.SameLine();

        if (hasValue)
        {
            float val = value ?? 0f;
            if (ImGui.DragFloat("##v", ref val, 0.01f)) onUpdate(val);
        }
        else
        {
            float inherited = GetInheritedValue(name, member);
            ImGui.TextDisabled($"{inherited:F2} (Inherited)");
        }

        ImGui.Columns(1);
        ImGui.PopID();
    }

    private float GetInheritedValue(string name, MemberInfo member)
    {
        var world = WorldManager.ActiveWorld;
        var settings = _app.ProjectSettings;

        if (member.DeclaringType == typeof(Physical))
        {
            bool useCustom = world?.UseCustomSettings ?? false;
            return name switch
            {
                "LinearDamping" => useCustom ? world!.CustomLinearDamping : settings.DefaultLinearDamping,
                "AngularDamping" => useCustom ? world!.CustomAngularDamping : settings.DefaultAngularDamping,
                "Friction" => useCustom ? world!.CustomFriction : settings.DefaultFriction,
                "Bounciness" => useCustom ? world!.CustomBounciness : settings.DefaultBounciness,
                _ => 0f
            };
        }
        return 0f;
    }

    private void DrawFilterField(string name, Filter? current, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 100);
        ImGui.Text(name);
        ImGui.NextColumn();

        string display = current == null ? "None (Filter)" : $"{current.Name}";
        if (ImGui.Button($"{display}##box", new Vector2(-25, 0))) { }

        ImGui.SameLine();
        if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("FilterPicker");

        if (ImGui.BeginPopup("FilterPicker"))
        {
            if (ImGui.MenuItem("None")) { onUpdate(null); ImGui.CloseCurrentPopup(); }
            ImGui.Separator();
            
            foreach (var filter in FilterManager.GetAllFilters())
            {
                if (ImGui.MenuItem(filter.Name))
                {
                    onUpdate(filter);
                    ImGui.CloseCurrentPopup();
                }
            }
            
            ImGui.Separator();
            if (ImGui.MenuItem("+ Create New Filter..."))
            {
                _app.GetWindow<FilterEditorWindow>()!.IsOpen = true;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        ImGui.Columns(1);
        ImGui.PopID();
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

    private void DrawList(string label, IList? list, Type elementType, Action<object?> onUpdate)
    {
        if (list == null) return;

        ImGui.PushID(label);
        bool open = ImGui.TreeNodeEx($"{label} [{list.Count}]", ImGuiTreeNodeFlags.SpanAvailWidth);
        if (open)
        {
            for (int i = 0; i < list.Count; i++)
            {
                ImGui.PushID(i);
                int index = i;
                DrawField($"[{i}]", list[i], val => { list[index] = val; onUpdate?.Invoke(list); });
                ImGui.PopID();
            }
            
            if (ImGui.Button("+ Add Element"))
            {
                object? newItem = elementType == typeof(string) ? "" : Activator.CreateInstance(elementType);
                list.Add(newItem);
                onUpdate?.Invoke(list);
            }
            ImGui.TreePop();
        }
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
        if (ImGui.Button($"{display}##box", new Vector2(-25, 0))) { }
        
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (payload.Handle != null && EditorSelection.DraggedAssetPath != null) {
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
            if (payload.Handle != null && EditorSelection.DraggedAssetPath != null) {
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
            if (payload.Handle != null && EditorSelection.DraggedEntity != null) {
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
