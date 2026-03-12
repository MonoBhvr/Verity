using System.Collections;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Input;
using Verity.Editor;
using Irodori.Backend.OpenGL;
using Verity.Core.Physics;
using Verity.Core.Serialization;
using Verity.Core.Engine;

namespace Verity.Editor.Windows;

using Color = Verity.Core.Color;

public unsafe class InspectorWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string _searchFilter = "";
    private readonly Dictionary<Guid, bool> _scaleLocks = [];

    private string _newTagNameBuffer = "";
    private string _newGroupNameBuffer = "";
    private string _newLayerNameBuffer = "";

    public InspectorWindow(EditorApp app) : base(L10n.Tr("window_inspector")) { _app = app; }
    
    public override void OnGui()
    {
        try
        {
            if (EditorSelection.SelectedEntities.Count > 1) { DrawMultiEntityInspector(EditorSelection.SelectedEntities.ToList()); return; }
            var entity = EditorSelection.SelectedEntity;
            if (entity != null) { DrawEntityInspector(entity); return; }
            var assetPath = EditorSelection.SelectedAssetPath;
            if (assetPath != null) { DrawAssetInspector(assetPath); return; }
            ImGui.Text(L10n.Tr("msg_select_to_inspect"));
        }
        catch (Exception e)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"[Inspector] Error: {e.Message}");
        }
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_inspector"); }

    private void DrawMultiEntityInspector(List<Entity> entities)
    {
        ImGui.TextColored(new Vector4(1, 1, 0, 1), L10n.Tr("msg_inspecting_entities", entities.Count));
        ImGui.Separator();
        bool allActive = entities.All(e => e.Active);
        if (ImGui.Checkbox($"{L10n.Tr("label_active")}##Active", ref allActive)) { _app.RecordUndo(); foreach (var e in entities) e.Active = allActive; }
        ImGui.Separator();
        var firstEnt = entities[0];
        var commonTypes = firstEnt.GetAllComponents().Select(c => c.GetType()).ToList();
        foreach (var ent in entities.Skip(1)) {
            var types = ent.GetAllComponents().Select(c => c.GetType()).ToHashSet();
            commonTypes = commonTypes.Where(t => types.Contains(t)).ToList();
        }
        foreach (var type in commonTypes) {
            ImGui.PushID(type.FullName);
            string localizedTypeName = L10n.Tr($"type_{type.Name}");
            if (localizedTypeName == $"type_{type.Name}") localizedTypeName = type.Name;

            if (ImGui.CollapsingHeader(localizedTypeName, ImGuiTreeNodeFlags.DefaultOpen)) {
                ImGui.Indent();
                var components = entities.Select(e => e.GetComponent(type)!).ToList();
                DrawMultiComponentFields(type, components);
                ImGui.Unindent();
            }
            ImGui.PopID();
        }
    }

    private void DrawGenericInspector(object target, Action? onUpdate = null)
    {
        var type = target.GetType();
        var hierarchy = new List<Type>();
        var curr = type;
        while (curr != null && curr.FullName != "Verity.Core.ECS.Component" && curr != typeof(object)) { hierarchy.Add(curr); curr = curr.BaseType; }
        hierarchy.Reverse();
        var allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                             .Where(m => m.DeclaringType != null && m.DeclaringType.FullName != "System.Object" && m.DeclaringType.FullName != "Verity.Core.ECS.Component")
                             .OrderBy(m => hierarchy.IndexOf(m.DeclaringType!)).ThenBy(m => m.MetadataToken);
        foreach (var member in allMembers) {
            string localizedName = L10n.Tr($"field_{member.Name}");
            if (localizedName == $"field_{member.Name}") localizedName = member.Name;

            if (member is FieldInfo field && ShouldShowMember(field)) ProcessMember(localizedName, field.FieldType, field.GetValue(target), val => { field.SetValue(target, val); onUpdate?.Invoke(); }, field, target);
            else if (member is PropertyInfo prop && prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0 && ShouldShowMember(prop)) ProcessMember(localizedName, prop.PropertyType, prop.GetValue(target), val => { prop.SetValue(target, val); onUpdate?.Invoke(); }, prop, target);
            else if (member is MethodInfo method) {
                var attr = method.GetCustomAttributes(true).FirstOrDefault(a => a.GetType().Name == "ButtonAttribute");
                if (attr != null && method.GetParameters().Length == 0) {
                    var labelProp = attr.GetType().GetProperty("Label") ?? attr.GetType().GetProperties().FirstOrDefault(p => p.Name == "Label");
                    string label = labelProp?.GetValue(attr) as string ?? method.Name;
                    string localizedLabel = L10n.Tr($"btn_{label}") ?? label;
                    if (ImGui.Button($"{localizedLabel}##{method.Name}", new Vector2(-1, 25))) { try { method.Invoke(target, null); } catch (Exception e) { Verity.Core.Debug.LogError($"Button Error: {e.Message}"); } }
                }
            }
        }
    }

    private void DrawMultiComponentFields(Type type, List<Component> components)
    {
        var allMembers = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                             .Where(m => m.DeclaringType != null && m.DeclaringType.FullName != "System.Object" && m.DeclaringType.FullName != "Verity.Core.ECS.Component")
                             .OrderBy(m => m.MetadataToken);
        foreach (var member in allMembers) {
            string localizedName = L10n.Tr($"field_{member.Name}");
            if (localizedName == $"field_{member.Name}") localizedName = member.Name;
            if (member is FieldInfo field && ShouldShowMember(field)) DrawMultiField(localizedName, field.FieldType, components.Select(c => field.GetValue(c)).ToList(), val => { foreach (var c in components) field.SetValue(c, val); }, field, type);
            else if (member is PropertyInfo prop && prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0 && ShouldShowMember(prop)) DrawMultiField(localizedName, prop.PropertyType, components.Select(c => prop.GetValue(c)).ToList(), val => { foreach (var c in components) prop.SetValue(c, val); }, prop, type);
        }
    }

    private void DrawMultiField(string name, Type type, List<object?> values, Action<object?> onUpdate, MemberInfo member, Type targetType)
    {
        ImGui.PushID(name);
        ImGui.Text(name); ImGui.SameLine(120);
        object? val = values[0]; bool changed = false;
        bool mixed = values.Any(v => !Equals(v, val));

        if (type == typeof(string)) {
            if (HasAttribute(member, "PhysicsGroupSelectorAttribute")) {
                DrawPhysicsGroupDropdown("", (string?)(mixed ? "" : val) ?? "", onUpdate, true);
                ImGui.PopID(); return;
            }
            if (HasAttribute(member, "SortingLayerSelectorAttribute")) {
                DrawSortingLayerDropdown("", (string?)(mixed ? "" : val) ?? "", onUpdate, true);
                ImGui.PopID(); return;
            }
            if (HasAttribute(member, "TagSelectorAttribute")) {
                DrawTagDropdown("", (string?)(mixed ? "" : val) ?? "", onUpdate, true);
                ImGui.PopID(); return;
            }
        }

        if (mixed) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 1, 0, 1));
        if (type == typeof(float)) { float f = (float)(val ?? 0f); if (ImGui.DragFloat("##v", ref f, 0.1f)) { changed = true; val = f; } }
        else if (type == typeof(int)) { int i = (int)(val ?? 0); if (ImGui.DragInt("##v", ref i)) { changed = true; val = i; } }
        else if (type == typeof(bool)) { bool b = (bool)(val ?? false); if (ImGui.Checkbox("##v", ref b)) { changed = true; val = b; } }
        else if (type == typeof(string)) { string s = (string)(val ?? ""); if (ImGui.InputText("##v", ref s, 1024)) { changed = true; val = s; } }
        else if (type == typeof(Vector2)) { Vector2 v2 = (Vector2)(val ?? Vector2.Zero); if (ImGui.DragFloat2("##v", (float*)&v2, 0.1f)) { changed = true; val = v2; } }
        else if (type == typeof(Color)) { var c = (Color)(val ?? Color.White); var v4 = (Vector4)c; if (ImGui.ColorEdit4("##v", ref v4)) { changed = true; val = (Color)v4; } }
        else { ImGui.TextDisabled(mixed ? L10n.Tr("msg_mixed") : (val?.ToString() ?? L10n.Tr("msg_none"))); }
        if (mixed) ImGui.PopStyleColor();
        if (changed) onUpdate(val);
        ImGui.PopID();
    }

    private void DrawEntityInspector(Entity entity)
    {
        ImGui.PushID("EntityHeader");
        bool active = entity.Active; if (ImGui.Checkbox($"{L10n.Tr("label_active")}##Active", ref active)) entity.Active = active;
        ImGui.SameLine(); string name = entity.Name; if (ImGui.InputText("##Name", ref name, 128)) entity.Name = name;
        ImGui.Separator();
        DrawTagDropdown(L10n.Tr("label_tag"), entity.Tag, val => entity.Tag = (string?)val ?? "Untagged");
        ImGui.PopID();
        ImGui.Separator();
        var components = entity.GetAllComponents();
        for (int i = 0; i < components.Count; i++) DrawComponent(components[i], entity);
        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button(L10n.Tr("btn_add_component"), new Vector2(-1, 30))) ImGui.OpenPopup("AddComponentPopup");
        if (ImGui.BeginPopup("AddComponentPopup")) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64); ImGui.Separator();
            var types = _app.ScriptCompiler?.GetAllAddableComponentTypes() ?? new List<Type>();
            foreach (var type in types) { 
                string typeName = type.Name;
                string localizedName = L10n.Tr($"type_{typeName}");
                if (localizedName == $"type_{typeName}") localizedName = type.Name;
                if (string.IsNullOrEmpty(_searchFilter) || typeName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) || localizedName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    if (ImGui.MenuItem(localizedName)) { _app.BeginUndoAction(); entity.AddComponent(type); _app.EndUndoAction(); ImGui.CloseCurrentPopup(); } 
            }
            ImGui.EndPopup();
        }
    }

    private void DrawAssetInspector(string path)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path).ToLower();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"{L10n.Tr("window_project")}: {fileName}");
        ImGui.Separator();
        if (string.Equals(fileName, "ProjectSettings.json", StringComparison.OrdinalIgnoreCase)) DrawProjectSettingsInspector();
        else if (string.Equals(fileName, "BuildSettings.json", StringComparison.OrdinalIgnoreCase)) DrawGenericInspector(_app.BuildSettings, () => _app.SaveBuildSettings());
        else if (string.Equals(fileName, "Filters.json", StringComparison.OrdinalIgnoreCase)) _app.GetWindow<FilterEditorWindow>()?.DrawFilterEditor(true);
        else if (extension == ".cs" || extension == ".shader") DrawScriptPreview(path);
        else if (extension == ".png" || extension == ".jpg" || extension == ".jpeg") DrawImagePreview(path);
        else if (extension == ".verity") DrawWorldSettingsInspector(path);
        else if (extension == ".style") DrawStyleAssetInspector(path);
        else { ImGui.Text($"{L10n.Tr("label_type")}: {extension}"); ImGui.Text($"{L10n.Tr("label_path")}: {path}"); }
    }

    private void DrawProjectSettingsInspector()
    {
        var settings = _app.ProjectSettings;
        bool changed = false;
        if (ImGui.CollapsingHeader(L10n.Tr("header_general"), ImGuiTreeNodeFlags.DefaultOpen)) {
            float fontSize = settings.EditorFontSize; if (ImGui.DragFloat(L10n.Tr("field_EditorFontSize"), ref fontSize, 0.5f, 8f, 72f)) { settings.EditorFontSize = fontSize; changed = true; }
            int targetTps = settings.TargetTPS; if (ImGui.DragInt(L10n.Tr("field_TargetTPS"), ref targetTps, 1, 1, 1000)) { settings.TargetTPS = targetTps; changed = true; }
            int targetPtps = settings.TargetPTPS; if (ImGui.DragInt(L10n.Tr("field_TargetPTPS"), ref targetPtps, 1, 1, 1000)) { settings.TargetPTPS = targetPtps; changed = true; }
            var bgColor = (Vector4)settings.EditorWorldBackgroundColor; if (ImGui.ColorEdit4(L10n.Tr("field_EditorWorldBackgroundColor"), ref bgColor)) { settings.EditorWorldBackgroundColor = (Color)bgColor; changed = true; }
        }
        if (ImGui.CollapsingHeader(L10n.Tr("header_physics"), ImGuiTreeNodeFlags.DefaultOpen)) {
            Vector2 gravity = settings.DefaultGravity; if (ImGui.DragFloat2(L10n.Tr("field_DefaultGravity"), (float*)&gravity, 0.1f)) { settings.DefaultGravity = gravity; changed = true; }
            float friction = settings.DefaultFriction; if (ImGui.DragFloat(L10n.Tr("field_DefaultFriction"), ref friction, 0.01f, 0f, 1f)) { settings.DefaultFriction = friction; changed = true; }
            float bounciness = settings.DefaultBounciness; if (ImGui.DragFloat(L10n.Tr("field_DefaultBounciness"), ref bounciness, 0.01f, 0f, 1f)) { settings.DefaultBounciness = bounciness; changed = true; }
        }
        changed |= DrawProjectSettingsList(L10n.Tr("header_tags"), settings.Tags, "Tag", false);
        changed |= DrawProjectSettingsList(L10n.Tr("header_sorting_layers"), settings.SortingLayers, "Layer", true);
        changed |= DrawProjectSettingsList(L10n.Tr("header_physics_groups"), settings.PhysicsGroups, "Group", false);
        if (changed) _app.SaveProjectSettings();
    }

    private bool DrawProjectSettingsList(string header, List<string> list, string idPrefix, bool allowReorder)
    {
        bool changed = false;
        if (ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen)) {
            ImGui.Indent();
            for (int i = 0; i < list.Count; i++) {
                ImGui.PushID($"{idPrefix}_{i}");
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                if (ImGui.Button("X", new Vector2(25, 0))) { list.RemoveAt(i); changed = true; ImGui.PopStyleColor(); ImGui.PopID(); break; }
                ImGui.PopStyleColor();
                if (allowReorder) {
                    ImGui.SameLine(); if (ImGui.Button("^", new Vector2(25, 0)) && i > 0) { (list[i], list[i - 1]) = (list[i - 1], list[i]); changed = true; }
                    ImGui.SameLine(); if (ImGui.Button("v", new Vector2(25, 0)) && i < list.Count - 1) { (list[i], list[i + 1]) = (list[i + 1], list[i]); changed = true; }
                }
                ImGui.SameLine(); string val = list[i]; ImGui.SetNextItemWidth(-1); if (ImGui.InputText("##edit", ref val, 64)) { list[i] = val; changed = true; }
                ImGui.PopID();
            }
            ImGui.Dummy(new Vector2(0, 5));
            if (ImGui.Button($"+ {L10n.Tr("btn_add")}##{idPrefix}", new Vector2(-1, 25))) { list.Add($"{idPrefix}_{list.Count}"); changed = true; }
            ImGui.Unindent();
        }
        return changed;
    }

    private void DrawStyleAssetInspector(string path)
    {
        try {
            string json = File.ReadAllText(path);
            var data = StyleData.FromJson(json) ?? new StyleData();
            ImGui.Text(L10n.Tr("label_style_asset_editor")); ImGui.Separator();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 100);
            DrawShaderField(L10n.Tr("CreationType_Shader"), (ShaderAsset)(data.ShaderPath ?? ""), val => { 
                if (val is ShaderAsset sa) data.ShaderPath = sa.Path;
                else if (val is string s) data.ShaderPath = s;
                SaveStyle(path, data); 
            });
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_refresh") ?? "Refresh", new Vector2(-1, 0))) {
                string relPath = Path.GetRelativePath(_app.ProjectPath!, path).Replace("\\", "/");
                _app.RenderPipeline.ClearStyleCache(relPath);
                if (!string.IsNullOrEmpty(data.ShaderPath)) _app.RenderPipeline.ClearShaderCache(data.ShaderPath);
                _app.ShowOverlayMessage("Refreshed Style & Shader Cache");
            }
            ImGui.Dummy(new Vector2(0, 5));
            if (!string.IsNullOrEmpty(data.ShaderPath)) {
                string shaderFullPath = ResolveAssetPath(data.ShaderPath);
                if (File.Exists(shaderFullPath)) {
                    string shaderContent = File.ReadAllText(shaderFullPath);
                    var uniforms = Shader2D.ParseUniforms(shaderContent);
                    var customUniforms = uniforms.Where(u => u.Name != "uProjection" && u.Name != "uView" && u.Name != "uModel" && u.Name != "uTexture" && u.Name != "uColor").ToList();
                    if (customUniforms.Count > 0) {
                        foreach (var u in customUniforms) {
                            ImGui.PushID(u.Name); ImGui.Text(u.Name); ImGui.SameLine(120); bool changed = false;
                            if (u.Type == "float") { float val = data.Floats.TryGetValue(u.Name, out var f) ? f : 0f; if (val == 0f && u.Name.Contains("Count")) ImGui.TextColored(new Vector4(1, 1, 0, 1), "(Warning: 0 may cause black screen)"); if (ImGui.DragFloat("##v", ref val, 0.1f)) { data.Floats[u.Name] = val; changed = true; } }
                            else if (u.Type == "vec2") { Vector2 val = data.Vector2s.TryGetValue(u.Name, out var v) ? v : Vector2.Zero; if (ImGui.DragFloat2("##v", (float*)&val, 0.1f)) { data.Vector2s[u.Name] = val; changed = true; } }
                            else if (u.Type == "vec3") { Vector3 val = data.Vector3s.TryGetValue(u.Name, out var v) ? v : Vector3.Zero; if (ImGui.DragFloat3("##v", (float*)&val, 0.1f)) { data.Vector3s[u.Name] = val; changed = true; } }
                            else if (u.Type == "vec4") {
                                if (u.Name.Contains("Color", StringComparison.OrdinalIgnoreCase)) { var c = data.Colors.TryGetValue(u.Name, out var col) ? col : Color.White; var v4 = (Vector4)c; if (ImGui.ColorEdit4("##v", ref v4)) { data.Colors[u.Name] = (Color)v4; changed = true; } }
                                else { Vector4 val = data.Vector4s.TryGetValue(u.Name, out var v) ? v : Vector4.One; if (ImGui.DragFloat4("##v", ref val)) { data.Vector4s[u.Name] = val; changed = true; } }
                            }
                            else if (u.Type == "sampler2D") { string val = data.Textures.TryGetValue(u.Name, out var s) ? s : ""; DrawAssetReferenceField("##v", val, ".png;.jpg;.jpeg", newVal => { data.Textures[u.Name] = (string)newVal!; SaveStyle(path, data); }); }
                            if (changed) { SaveStyle(path, data); string relPath = Path.GetRelativePath(_app.ProjectPath!, path).Replace("\\", "/"); _app.RenderPipeline.ClearStyleCache(relPath); }
                            ImGui.PopID();
                        }
                    } else ImGui.TextDisabled("(No custom parameters)");
                } else ImGui.TextColored(new Vector4(1, 0, 0, 1), "Shader not found");
            } else ImGui.TextDisabled("(Select a shader)");
        } catch (Exception e) { ImGui.TextColored(new Vector4(1, 0, 0, 1), $"Error: {e.Message}"); }
    }

    private string ResolveAssetPath(string p) => Path.IsPathRooted(p) ? p : (_app.ProjectPath == null ? p : Path.Combine(_app.ProjectPath, p));
    private void SaveStyle(string path, StyleData data) { try { File.WriteAllText(path, data.ToJson()); if (_app.ProjectPath != null) { string relPath = Path.GetRelativePath(_app.ProjectPath, path).Replace("\\", "/"); _app.RenderPipeline.ClearStyleCache(relPath); } } catch { } }

    private void DrawWorldSettingsInspector(string path) {
        var world = WorldManager.ActiveWorld;
        if (world != null && string.Equals(world.Name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)) { ImGui.Text(L10n.Tr("msg_active_world_settings")); ImGui.Separator(); DrawGenericInspector(world); if (ImGui.Button(L10n.Tr("btn_save_world"), new Vector2(-1, 30))) _app.GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset(); }
        else { ImGui.Text(L10n.Tr("msg_selected_world_not_active")); if (ImGui.Button(L10n.Tr("btn_load_world"), new Vector2(-1, 40))) _app.GetWindow<ProjectWindow>()?.LoadWorldByPath(path); }
    }

    private void DrawScriptPreview(string path) { try { string code = File.ReadAllText(path); ImGui.Text(L10n.Tr("msg_source")); ImGui.InputTextMultiline("##code", ref code, (uint)code.Length + 1024, new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly); } catch { ImGui.Text(L10n.Tr("msg_error_reading_file")); } }
    private void DrawImagePreview(string path) { var tex = _app.TextureManager.Load(path); if (tex is OpenGlTexture glTex) { ImGui.Text($"{L10n.Tr("label_size")}: {glTex.Width}x{glTex.Height}"); float scale = Math.Min(1.0f, ImGui.GetContentRegionAvail().X / glTex.Width); ImGui.Image(new ImTextureRef(null, new ImTextureID((nint)glTex.Id)), new Vector2(glTex.Width * scale, glTex.Height * scale), new Vector2(0, 1), new Vector2(1, 0)); } }

    private void DrawComponent(Component component, Entity entity) {
        ImGui.PushID(component.GetHashCode());
        string typeName = component.GetType().Name; string localizedTypeName = L10n.Tr($"type_{typeName}");
        if (localizedTypeName == $"type_{typeName}") localizedTypeName = typeName;
        if (ImGui.CollapsingHeader(localizedTypeName, ImGuiTreeNodeFlags.DefaultOpen)) {
            if (ImGui.BeginPopupContextItem()) { if (component is not Transform && ImGui.MenuItem(L10n.Tr("ctx_remove"))) { _app.BeginUndoAction(); entity.RemoveComponent(component); _app.EndUndoAction(); } ImGui.EndPopup(); }
            ImGui.Indent(); 
            if (component is PolygonShape || component is PolygonRenderer) { 
                bool isEdit = EditorSelection.EditingPolygonComponent == component; 
                if (isEdit) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1.0f, 1.0f)); 
                string btnLabel = isEdit ? L10n.Tr("btn_exit_edit") : (component is PolygonShape ? L10n.Tr("btn_edit_polygon_shape") : L10n.Tr("btn_edit_polygon_renderer"));
                if (ImGui.Button(btnLabel, new Vector2(-1, 25))) EditorSelection.EditingPolygonComponent = isEdit ? null : component; 
                if (isEdit) ImGui.PopStyleColor(); 
            }
            DrawGenericInspector(component); 
            ImGui.Unindent();
        }
        ImGui.PopID();
    }

    private bool ShouldShowMember(MemberInfo m) => HasAttribute(m, "SerializeFieldAttribute") || (m is FieldInfo f && f.IsPublic) || (m is PropertyInfo p && (p.GetGetMethod()?.IsPublic ?? false) && !HasAttribute(m, "HideInInspectorAttribute"));

    private void ProcessMember(string name, Type type, object? value, Action<object?> onUpdate, MemberInfo member, object target) {
        if (type == typeof(string)) {
            if (HasAttribute(member, "PhysicsGroupSelectorAttribute")) { DrawPhysicsGroupDropdown(name, (string?)value ?? "", onUpdate); return; }
            if (HasAttribute(member, "SortingLayerSelectorAttribute")) { DrawSortingLayerDropdown(name, (string?)value ?? "", onUpdate); return; }
            if (HasAttribute(member, "TagSelectorAttribute")) { DrawTagDropdown(name, (string?)value ?? "", onUpdate); return; }
        }
        if (member.Name == "Scale" && target is Transform t) { DrawTransformScaleField(t); return; }
        if (type == typeof(Sprite)) { DrawSpriteField(name, (Sprite?)value ?? default, onUpdate); return; }
        if (type == typeof(StyleAsset)) { DrawStyleField(name, (StyleAsset?)value ?? default, onUpdate); return; }
        if (type == typeof(ShaderAsset)) { DrawShaderField(name, (ShaderAsset?)value ?? default, onUpdate); return; }
        if (type == typeof(Filter)) { DrawFilterField(name, (Filter?)value, onUpdate); return; }
        if (HasAttribute(member, "AssetReferenceAttribute") && type == typeof(string)) { DrawAssetReferenceField(name, (string?)value ?? "", member.GetCustomAttribute<AssetReferenceAttribute>()!.Extension, onUpdate); return; }
        if (typeof(Component).IsAssignableFrom(type)) { DrawComponentReferenceField(name, (Component?)value, type, onUpdate); return; }
        if (typeof(IList).IsAssignableFrom(type) && type.IsGenericType) { DrawList(name, (IList?)value, type.GetGenericArguments()[0], onUpdate); return; }
        if (type == typeof(float?)) { DrawNullableFloat(name, (float?)value, onUpdate, member); return; }
        DrawField(name, value, onUpdate);
    }

    private bool HasAttribute(MemberInfo member, string attributeName) { return member.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName); }

    private void DrawPhysicsGroupDropdown(string label, string current, Action<object?> onUpdate, bool noLabel = false) {
        if (!noLabel) { ImGui.PushID(label.GetHashCode()); ImGui.Text(label); ImGui.SameLine(120); }
        bool openPopup = false; var groups = _app.ProjectSettings.PhysicsGroups;
        if (ImGui.BeginCombo("##Group", string.IsNullOrEmpty(current) ? L10n.Tr("msg_none") : current)) {
            foreach (var group in groups) if (ImGui.Selectable(group, current == group)) onUpdate(group);
            ImGui.Separator(); if (ImGui.Selectable(L10n.Tr("ctx_add_group"))) { _newGroupNameBuffer = ""; openPopup = true; }
            ImGui.EndCombo();
        }
        if (openPopup) ImGui.OpenPopup("AddPhysicsGroupPopup_Local");
        if (ImGui.BeginPopup("AddPhysicsGroupPopup_Local")) {
            ImGui.Text(L10n.Tr("msg_new_group_name"));
            if (ImGui.InputText("##newgroup", ref _newGroupNameBuffer, 32, ImGuiInputTextFlags.EnterReturnsTrue)) { if (!string.IsNullOrWhiteSpace(_newGroupNameBuffer) && !groups.Contains(_newGroupNameBuffer)) { groups.Add(_newGroupNameBuffer); _app.SaveProjectSettings(); onUpdate(_newGroupNameBuffer); } ImGui.CloseCurrentPopup(); }
            if (ImGui.Button(L10n.Tr("btn_add")) && !string.IsNullOrWhiteSpace(_newGroupNameBuffer)) { if (!groups.Contains(_newGroupNameBuffer)) { groups.Add(_newGroupNameBuffer); _app.SaveProjectSettings(); onUpdate(_newGroupNameBuffer); } ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!noLabel) ImGui.PopID();
    }

    private void DrawSortingLayerDropdown(string label, string current, Action<object?> onUpdate, bool noLabel = false) {
        if (!noLabel) { ImGui.PushID(label.GetHashCode()); ImGui.Text(label); ImGui.SameLine(120); }
        bool openPopup = false; var layers = _app.ProjectSettings.SortingLayers;
        if (ImGui.BeginCombo("##Layer", string.IsNullOrEmpty(current) ? L10n.Tr("msg_none") : current)) {
            foreach (var layer in layers) if (ImGui.Selectable(layer, current == layer)) onUpdate(layer);
            ImGui.Separator(); if (ImGui.Selectable(L10n.Tr("ctx_add_layer"))) { _newLayerNameBuffer = ""; openPopup = true; }
            ImGui.EndCombo();
        }
        if (openPopup) ImGui.OpenPopup("AddSortingLayerPopup_Local");
        if (ImGui.BeginPopup("AddSortingLayerPopup_Local")) {
            ImGui.Text(L10n.Tr("msg_new_layer_name"));
            if (ImGui.InputText("##newlayer", ref _newLayerNameBuffer, 32, ImGuiInputTextFlags.EnterReturnsTrue)) { if (!string.IsNullOrWhiteSpace(_newLayerNameBuffer) && !layers.Contains(_newLayerNameBuffer)) { layers.Add(_newLayerNameBuffer); _app.SaveProjectSettings(); onUpdate(_newLayerNameBuffer); } ImGui.CloseCurrentPopup(); }
            if (ImGui.Button(L10n.Tr("btn_add")) && !string.IsNullOrWhiteSpace(_newLayerNameBuffer)) { if (!layers.Contains(_newLayerNameBuffer)) { layers.Add(_newLayerNameBuffer); _app.SaveProjectSettings(); onUpdate(_newLayerNameBuffer); } ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!noLabel) ImGui.PopID();
    }

    private void DrawTagDropdown(string label, string current, Action<object?> onUpdate, bool noLabel = false) {
        if (!noLabel) { ImGui.PushID(label.GetHashCode()); ImGui.Text(label); ImGui.SameLine(120); }
        bool openPopup = false; var tags = _app.ProjectSettings.Tags;
        if (ImGui.BeginCombo("##Tag", current)) {
            foreach (var tag in tags) if (ImGui.Selectable(tag, current == tag)) onUpdate(tag);
            ImGui.Separator(); if (ImGui.Selectable(L10n.Tr("ctx_add_tag"))) { _newTagNameBuffer = ""; openPopup = true; }
            ImGui.EndCombo();
        }
        if (openPopup) ImGui.OpenPopup("AddTagPopup_Local");
        if (ImGui.BeginPopup("AddTagPopup_Local")) {
            ImGui.Text(L10n.Tr("msg_new_tag_name"));
            if (ImGui.InputText("##newtag", ref _newTagNameBuffer, 32, ImGuiInputTextFlags.EnterReturnsTrue)) { if (!string.IsNullOrWhiteSpace(_newTagNameBuffer) && !tags.Contains(_newTagNameBuffer)) { tags.Add(_newTagNameBuffer); _app.SaveProjectSettings(); onUpdate(_newTagNameBuffer); } ImGui.CloseCurrentPopup(); }
            if (ImGui.Button(L10n.Tr("btn_add")) && !string.IsNullOrWhiteSpace(_newTagNameBuffer)) { if (!tags.Contains(_newTagNameBuffer)) { tags.Add(_newTagNameBuffer); _app.SaveProjectSettings(); onUpdate(_newTagNameBuffer); } ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (!noLabel) ImGui.PopID();
    }

    private void DrawNullableFloat(string name, float? value, Action<object?> onUpdate, MemberInfo member) {
        ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        bool hasValue = value.HasValue; if (ImGui.Checkbox("##hasValue", ref hasValue)) onUpdate(hasValue ? 0.0f : null);
        if (hasValue) { ImGui.SameLine(); float val = value ?? 0.0f; if (ImGui.DragFloat("##v", ref val, 0.1f)) onUpdate(val); }
        ImGui.PopID();
    }

    private void DrawTransformScaleField(Transform t) {
        ImGui.PushID("ScaleLock"); ImGui.Text(L10n.Tr("label_scale")); ImGui.SameLine(120);
        bool isLocked = _scaleLocks.TryGetValue(t.Owner.Id, out bool locked) && locked;
        if (ImGui.Button(isLocked ? "[L]" : "[U]", new Vector2(25, 20))) _scaleLocks[t.Owner.Id] = !isLocked;
        ImGui.SameLine(); Vector2 v2 = t.Scale;
        if (ImGui.DragFloat2("##v", (float*)&v2, 0.1f)) {
            if (ImGui.IsItemActivated()) _app.BeginUndoAction();
            if (isLocked) { float oldX = t.Scale.X; if (MathF.Abs(v2.X - oldX) > 0.0001f) t.Scale = new Vector2(v2.X, t.Scale.Y * (v2.X / (oldX != 0 ? oldX : 1f))); else t.Scale = new Vector2(t.Scale.X * (v2.Y / (t.Scale.Y != 0 ? t.Scale.Y : 1f)), v2.Y); }
            else t.Scale = v2;
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) _app.EndUndoAction();
        ImGui.PopID();
    }

    private void DrawFilterField(string name, Filter? current, Action<object?> onUpdate) {
        ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        if (ImGui.Button($"{(current?.Name ?? L10n.Tr("msg_none"))}##box", new Vector2(-25, 0))) { }
        ImGui.SameLine(); if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) { if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(null); ImGui.Separator(); foreach (var f in FilterManager.GetAllFilters()) if (ImGui.MenuItem(f.Name)) onUpdate(f); ImGui.EndPopup(); }
        ImGui.PopID();
    }

    private void DrawField(string name, object? value, Action<object?> onUpdate) {
        if (value == null) return; ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        bool changed = false; Type t = value.GetType();
        if (t == typeof(float)) { float f = (float)value; if (ImGui.DragFloat("##v", ref f, 0.1f)) { changed = true; value = f; } }
        else if (t == typeof(int)) { int i = (int)value; if (ImGui.DragInt("##v", ref i)) { changed = true; value = i; } }
        else if (t == typeof(bool)) { bool b = (bool)value; if (ImGui.Checkbox("##v", ref b)) { changed = true; value = b; } }
        else if (t == typeof(string)) { string s = (string)value; if (ImGui.InputText("##v", ref s, 1024)) { changed = true; value = s; } }
        else if (t == typeof(Vector2)) { Vector2 v2 = (Vector2)value; if (ImGui.DragFloat2("##v", (float*)&v2, 0.1f)) { changed = true; value = v2; } }
        else if (t == typeof(Color)) { var c = (Color)value; var v4 = (Vector4)c; if (ImGui.ColorEdit4("##v", ref v4)) { changed = true; value = (Color)v4; } }
        else if (value is Enum) { string[] names = Enum.GetNames(t); int curr = Array.IndexOf(names, value.ToString()); if (ImGui.Combo("##v", ref curr, names, names.Length)) { changed = true; value = Enum.Parse(t, names[curr]); } }
        if (changed) onUpdate(value); ImGui.PopID();
    }

    private void DrawList(string label, IList? list, Type elementType, Action<object?> onUpdate) {
        if (list == null) return;
        if (ImGui.TreeNodeEx($"{label} [{list.Count}]")) { for (int i = 0; i < list.Count; i++) { int idx = i; DrawField($"[{i}]", list[i], val => { list[idx] = val; onUpdate?.Invoke(list); }); } if (ImGui.Button("+ " + L10n.Tr("btn_add"))) { list.Add(elementType == typeof(string) ? "" : Activator.CreateInstance(elementType)); onUpdate?.Invoke(list); } ImGui.TreePop(); }
    }

    private void DrawSpriteField(string name, Sprite current, Action<object?> onUpdate) 
    {
        ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        string btnLabel = string.IsNullOrEmpty(current.Path) ? L10n.Tr("msg_none") : Path.GetFileName(current.Path);
        if (ImGui.Button($"{btnLabel}##box", new Vector2(-25, 0))) { }
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) { var ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower(); if (ext is ".png" or ".jpg" or ".jpeg") onUpdate((Sprite)EditorSelection.DraggedAssetPath); } ImGui.EndDragDropTarget(); }
        ImGui.SameLine(); if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(Sprite));
            if (_app.AssetsPath != null) foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.*", SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(f).ToLower();
                if (ext is ".png" or ".jpg" or ".jpeg") {
                    var rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                    if (string.IsNullOrEmpty(_searchFilter) || rel.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) if (ImGui.MenuItem(rel)) onUpdate((Sprite)f);
                }
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawStyleField(string name, StyleAsset current, Action<object?> onUpdate) 
    {
        ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        string btnLabel = string.IsNullOrEmpty(current.Path) ? L10n.Tr("msg_none") : Path.GetFileName(current.Path);
        if (ImGui.Button($"{btnLabel}##box", new Vector2(-25, 0))) { }
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) if (Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower() == ".style") onUpdate((StyleAsset)EditorSelection.DraggedAssetPath); ImGui.EndDragDropTarget(); }
        ImGui.SameLine(); if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(StyleAsset));
            if (_app.AssetsPath != null) foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.style", SearchOption.AllDirectories)) {
                var rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                if (string.IsNullOrEmpty(_searchFilter) || rel.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) if (ImGui.MenuItem(rel)) onUpdate((StyleAsset)f);
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawShaderField(string name, ShaderAsset current, Action<object?> onUpdate) 
    {
        ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        string btnLabel = string.IsNullOrEmpty(current.Path) ? L10n.Tr("msg_none") : Path.GetFileName(current.Path);
        if (ImGui.Button($"{btnLabel}##box", new Vector2(-25, 0))) { }
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) if (Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower() == ".shader") onUpdate((ShaderAsset)EditorSelection.DraggedAssetPath); ImGui.EndDragDropTarget(); }
        ImGui.SameLine(); if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(ShaderAsset));
            if (_app.AssetsPath != null) foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.shader", SearchOption.AllDirectories)) {
                var rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                if (string.IsNullOrEmpty(_searchFilter) || rel.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) if (ImGui.MenuItem(rel)) onUpdate((ShaderAsset)f);
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawAssetReferenceField(string name, string current, string exts, Action<object?> onUpdate) 
    {
        ImGui.PushID(name); if (name != "##v") { ImGui.Text(name); ImGui.SameLine(120); }
        string btnLabel = string.IsNullOrEmpty(current) ? L10n.Tr("msg_none") : Path.GetFileName(current);
        if (ImGui.Button($"{btnLabel}##box", new Vector2(-25, 0))) { }
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) { var ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower(); if (exts.Split(';').Any(e => e.Trim().ToLower() == ext)) onUpdate(EditorSelection.DraggedAssetPath); } ImGui.EndDragDropTarget(); }
        ImGui.SameLine(); if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(null);
            if (_app.AssetsPath != null) {
                var eList = exts.Split(';').Select(e => e.Trim().ToLower()).ToArray();
                foreach (var f in Directory.GetFiles(_app.AssetsPath, "*.*", SearchOption.AllDirectories)) if (eList.Contains(Path.GetExtension(f).ToLower())) {
                    var rel = Path.GetRelativePath(_app.AssetsPath, f).Replace("\\", "/");
                    if (string.IsNullOrEmpty(_searchFilter) || rel.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) if (ImGui.MenuItem(rel)) onUpdate(f);
                }
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawComponentReferenceField(string name, Component? current, Type targetType, Action<object?> onUpdate) 
    {
        ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        string btnLabel = (current == null) ? L10n.Tr("msg_none") : current.Owner.Name;
        if (ImGui.Button($"{btnLabel}##box", new Vector2(-25, 0))) { }
        ImGui.SameLine(); if (ImGui.Button("o##picker", new Vector2(20, 0))) ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) {
            if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(null);
            if (WorldManager.ActiveWorld != null) foreach (var e in WorldManager.ActiveWorld.GetAllEntities()) {
                var c = e.GetComponent(targetType);
                if (c != null && (string.IsNullOrEmpty(_searchFilter) || e.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))) if (ImGui.MenuItem(e.Name)) onUpdate(c);
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }
}
