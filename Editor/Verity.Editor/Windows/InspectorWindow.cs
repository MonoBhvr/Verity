using System.Collections;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Editor.Windows;

public class InspectorWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string _searchFilter = "";

    public InspectorWindow(EditorApp app) : base("Inspector") { _app = app; }

    public override void OnGui()
    {
        var entity = EditorSelection.SelectedEntity;
        if (entity == null) { ImGui.Text("No Entity selected."); return; }

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
        else DrawField(name, value, onUpdate);
    }

    private void DrawField(string name, object? value, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Columns(2);
        ImGui.SetColumnWidth(0, 100);
        ImGui.Text(name);
        ImGui.NextColumn();

        if (value is float f) { if (ImGui.DragFloat("##v", ref f, 0.1f)) onUpdate(f); }
        else if (value is int i) { if (ImGui.DragInt("##v", ref i)) onUpdate(i); }
        else if (value is bool b) { if (ImGui.Checkbox("##v", ref b)) onUpdate(b); }
        else if (value is string s) { s ??= ""; if (ImGui.InputText("##v", ref s, 1024)) onUpdate(s); }
        else if (value is Vector2 v2) { if (ImGui.DragFloat2("##v", ref v2, 0.1f)) onUpdate(v2); }
        else if (value is Vector3 v3) { if (ImGui.DragFloat3("##v", ref v3, 0.1f)) onUpdate(v3); }
        else if (value is Verity.Core.Color color)
        {
            var v4 = (Vector4)color;
            if (ImGui.ColorEdit4("##v", ref v4, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar)) onUpdate((Verity.Core.Color)v4);
        }
        else if (value is Enum e)
        {
            string[] names = Enum.GetNames(e.GetType());
            int current = Array.IndexOf(names, e.ToString());
            if (ImGui.Combo("##v", ref current, names, names.Length)) onUpdate(Enum.Parse(e.GetType(), names[current]));
        }
        else { ImGui.TextDisabled(value?.ToString() ?? "null"); }

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
