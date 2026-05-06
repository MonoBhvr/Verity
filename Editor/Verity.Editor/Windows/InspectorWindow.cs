using System.Collections;
using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Reflection;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using Hexa.NET.ImGui;
using Lua;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using Verity.Filter;
using FilterType = Verity.Filter.Filter;
using Verity.Editor;
using Irodori.Backend.OpenGL;
using Irodori.Texture;
using Verity.Core.Physics;
using Verity.Core.Serialization;
using Verity.Core.Engine;
using Verity.Core.Audio;
using Verity.Core.UI;

namespace Verity.Editor.Windows;

using Color = Verity.Core.Color;
using Vector3 = Verity.Core.Vector3;

public unsafe class InspectorWindow : EditorWindow
{
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> GenericInspectorMembersCache = new();
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> MultiInspectorMembersCache = new();
    private static readonly ConcurrentDictionary<Type, MemberInfo[]> NestedInspectorMembersCache = new();
    private static readonly ConcurrentDictionary<MemberInfo, HashSet<string>> MemberAttributeCache = new();
    private static readonly ConcurrentDictionary<MethodInfo, ButtonMetadata?> ButtonMetadataCache = new();
    private static readonly ConcurrentDictionary<MemberInfo, AssetReferenceAttribute?> AssetReferenceAttributeCache = new();

    private readonly EditorApp _app;
    private string _searchFilter = "";
    private readonly Dictionary<Guid, bool> _scaleLocks = [];
    private readonly Dictionary<string, string> _selectedSliceIds = [];
    private int _sliceGridCellWidth = 32;
    private int _sliceGridCellHeight = 32;
    private int _sliceGridOffsetX = 0;
    private int _sliceGridOffsetY = 0;
    private int _sliceGridPaddingX = 0;
    private int _sliceGridPaddingY = 0;
    private uint _draggedCollectionId = 0;
    private int _draggedCollectionIndex = -1;

    private string _newTagNameBuffer = "";
    private string _newGroupNameBuffer = "";
    private string _newLayerNameBuffer = "";
    private string? _cachedUiScreenPath;
    private DateTime _cachedUiScreenWriteTimeUtc;
    private UIScreenAsset? _cachedUiScreen;
    private string? _cachedUiPrefabPath;
    private DateTime _cachedUiPrefabWriteTimeUtc;
    private UiPrefabAsset? _cachedUiPrefab;
    private readonly Dictionary<string, (DateTime WriteTimeUtc, string Content)> _cachedTextFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime WriteTimeUtc, StyleData Data)> _cachedStyleData = new(StringComparer.OrdinalIgnoreCase);
    private string? _cachedBlueprintPath;
    private DateTime _cachedBlueprintWriteTimeUtc;
    private BlueprintPreviewData? _cachedBlueprintPreview;
    private readonly Dictionary<string, List<AssetPickerEntry>> _assetPickerCache = new(StringComparer.OrdinalIgnoreCase);
    private string? _assetPickerCacheRoot;

    public InspectorWindow(EditorApp app) : base(L10n.Tr("window_inspector")) { _app = app; }

    internal static void ClearReflectionCaches()
    {
        GenericInspectorMembersCache.Clear();
        MultiInspectorMembersCache.Clear();
        NestedInspectorMembersCache.Clear();
        MemberAttributeCache.Clear();
        ButtonMetadataCache.Clear();
        AssetReferenceAttributeCache.Clear();
    }

    internal void ClearCachedState()
    {
        _scaleLocks.Clear();
        _selectedSliceIds.Clear();
        _cachedUiScreenPath = null;
        _cachedUiScreenWriteTimeUtc = default;
        _cachedUiScreen = null;
        _cachedUiPrefabPath = null;
        _cachedUiPrefabWriteTimeUtc = default;
        _cachedUiPrefab = null;
        _cachedTextFiles.Clear();
        _cachedStyleData.Clear();
        _cachedBlueprintPath = null;
        _cachedBlueprintWriteTimeUtc = default;
        _cachedBlueprintPreview = null;
        _assetPickerCache.Clear();
        _assetPickerCacheRoot = null;
    }

    private sealed class BlueprintPreviewData
    {
        public List<BlueprintEntityPreview> Entities { get; init; } = [];
        public int RootCount { get; init; }
        public int ComponentCount { get; init; }
        public Sprite PreviewSprite { get; init; }
        public bool HasPreviewSprite { get; init; }
    }

    private sealed class BlueprintEntityPreview
    {
        public int Index { get; init; }
        public int ParentIndex { get; init; }
        public string Name { get; init; } = "";
        public bool Active { get; init; } = true;
        public Vector2 Position { get; init; }
        public float Rotation { get; init; }
        public Vector2 Scale { get; init; } = new Vector2(1, 1);
        public List<BlueprintComponentPreview> Components { get; } = [];
        public List<int> Children { get; } = [];
    }

    private sealed class BlueprintComponentPreview
    {
        public string Name { get; init; } = "";
        public JsonObject? Fields { get; init; }
    }

    private sealed class AssetPickerEntry
    {
        public string FullPath { get; init; } = "";
        public string RelativePath { get; init; } = "";
    }

    private sealed class ButtonMetadata
    {
        public string Label { get; init; } = "";
        public bool Undoable { get; init; }
    }
    
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
            ImGui.TextColored(new Vector4(1, 0, 0, 1), $"{L10n.Tr("msg_inspectorError")} {e.Message}");
        }
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_inspector"); }

    private IEnumerable<AssetPickerEntry> GetAssetPickerEntries(string cacheKey, Func<string, bool> predicate)
    {
        if (string.IsNullOrWhiteSpace(_app.AssetsPath) || !Directory.Exists(_app.AssetsPath))
            return Array.Empty<AssetPickerEntry>();

        string assetsPath = Path.GetFullPath(_app.AssetsPath);
        if (!string.Equals(_assetPickerCacheRoot, assetsPath, StringComparison.OrdinalIgnoreCase))
        {
            _assetPickerCacheRoot = assetsPath;
            _assetPickerCache.Clear();
        }

        if (_assetPickerCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var entries = new List<AssetPickerEntry>();
        foreach (string path in Directory.EnumerateFiles(assetsPath, "*", SearchOption.AllDirectories))
        {
            if (!predicate(path))
                continue;

            entries.Add(new AssetPickerEntry
            {
                FullPath = path,
                RelativePath = Path.GetRelativePath(assetsPath, path).Replace("\\", "/")
            });
        }

        _assetPickerCache[cacheKey] = entries;
        return entries;
    }

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

    private void DrawLuaScriptComponentInspector(LuaScriptComponent component)
    {
        DrawGenericInspector(component);

        if (component.State != null)
        {
            var exportTableCheck = component.State.DoStringAsync("return type(Export) == 'table'").GetAwaiter().GetResult();
            if (exportTableCheck.Length > 0 && exportTableCheck[0].TryRead<bool>(out var isTable) && isTable)
            {
                ImGui.Separator();
                ImGui.Text(L10n.Tr("msg_exported_variables"));

                var keysQuery = component.State.DoStringAsync(@"
                    local res = ''
                    for k, v in pairs(Export) do
                        res = res .. tostring(k) .. '|' .. type(v) .. '\n'
                    end
                    return res
                ").GetAwaiter().GetResult();

                if (keysQuery.Length > 0 && keysQuery[0].TryRead<string>(out var keysStr) && !string.IsNullOrWhiteSpace(keysStr))
                {
                    var lines = keysStr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('|');
                        if (parts.Length != 2) continue;

                        string key = parts[0];
                        string luaType = parts[1];

                        var valResult = component.State.DoStringAsync($"return Export['{key}']").GetAwaiter().GetResult();
                        if (valResult.Length == 0) continue;
                        
                        var val = valResult[0];

                        if (luaType == "number" && val.TryRead<double>(out var numVal))
                        {
                            float floatVal = (float)numVal;
                            if (ImGui.DragFloat(key, ref floatVal))
                            {
                                component.State.DoStringAsync($"Export['{key}'] = {floatVal}").GetAwaiter().GetResult();
                            }
                        }
                        else if (luaType == "string" && val.TryRead<string>(out var strVal))
                        {
                            if (ImGui.InputText(key, ref strVal, 256))
                            {
                                string escaped = strVal.Replace("\"", "\\\"");
                                component.State.DoStringAsync($"Export['{key}'] = \"{escaped}\"").GetAwaiter().GetResult();
                            }
                        }
                        else if (luaType == "boolean" && val.TryRead<bool>(out var boolVal))
                        {
                            if (ImGui.Checkbox(key, ref boolVal))
                            {
                                component.State.DoStringAsync($"Export['{key}'] = {(boolVal ? "true" : "false")}").GetAwaiter().GetResult();
                            }
                        }
                    }
                }
            }
        }
    }

    private void DrawGenericInspector(object target, Action? onUpdate = null)
    {
        var type = target.GetType();
        foreach (var member in GetGenericInspectorMembers(type)) {
            string localizedName = L10n.Tr($"field_{member.Name}");
            if (localizedName == $"field_{member.Name}") localizedName = member.Name;

            if (member is FieldInfo field && ShouldShowMember(field, target)) ProcessMember(localizedName, field.FieldType, field.GetValue(target), val => { field.SetValue(target, val); onUpdate?.Invoke(); }, field, target);
            else if (member is PropertyInfo prop && prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0 && ShouldShowMember(prop, target)) ProcessMember(localizedName, prop.PropertyType, prop.GetValue(target), val => { prop.SetValue(target, val); onUpdate?.Invoke(); }, prop, target);
            else if (member is MethodInfo method) {
                ButtonMetadata? metadata = GetButtonMetadata(method);
                if (metadata != null) {
                    string localizedLabel = L10n.Tr($"btn_{metadata.Label}") ?? metadata.Label;
                    if (ImGui.Button($"{localizedLabel}##{method.Name}", new Vector2(-1, 25))) {
                        try
                        {
                            if (metadata.Undoable)
                                _app.BeginUndoAction();

                            method.Invoke(target, null);
                        }
                        catch (Exception e)
                        {
                            Verity.Core.Debug.LogError($"Button Error: {e.Message}");
                        }
                        finally
                        {
                            if (metadata.Undoable)
                                _app.EndUndoAction();
                        }
                    }
                }
            }
        }
    }

    private void DrawMultiComponentFields(Type type, List<Component> components)
    {
        foreach (var member in GetMultiInspectorMembers(type)) {
            string localizedName = L10n.Tr($"field_{member.Name}");
            if (localizedName == $"field_{member.Name}") localizedName = member.Name;
            if (member is FieldInfo field && ShouldShowMember(field, components)) DrawMultiField(localizedName, field.FieldType, components.Select(c => field.GetValue(c)).ToList(), val => { foreach (var c in components) field.SetValue(c, val); }, field, type, components);
            else if (member is PropertyInfo prop && prop.CanRead && prop.CanWrite && prop.GetIndexParameters().Length == 0 && ShouldShowMember(prop, components)) DrawMultiField(localizedName, prop.PropertyType, components.Select(c => prop.GetValue(c)).ToList(), val => { foreach (var c in components) prop.SetValue(c, val); }, prop, type, components);
        }
    }

    private void DrawMultiField(string name, Type type, List<object?> values, Action<object?> onUpdate, MemberInfo member, Type targetType, List<Component> components)
    {
        ImGui.PushID(name);
        ImGui.Text(name); ImGui.SameLine(120);
        object? val = values[0]; bool changed = false;
        bool mixed = values.Any(v => !Equals(v, val));

        if (type == typeof(ulong) && HasAttribute(member, "PhysicsGroupMaskSelectorAttribute")) {
            DrawPhysicsGroupMaskDropdown("", mixed ? 0UL : (ulong)(val ?? 0UL), onUpdate, true, mixed);
            ImGui.PopID(); return;
        }

        if (type == typeof(ulong) && HasAttribute(member, "SortingLayerMaskSelectorAttribute")) {
            DrawSortingLayerMaskDropdown("", mixed ? 0UL : (ulong)(val ?? 0UL), onUpdate, true, mixed);
            ImGui.PopID(); return;
        }

        if (targetType == typeof(Light2D) && member.Name == nameof(Light2D.ShadowReceiverMask))
        {
            var lights = components.OfType<Light2D>().ToList();
            if (lights.Count == 0)
            {
                ImGui.PopID();
                return;
            }

            bool mixedSource = lights.Select(light => light.ShadowLayerSource).Distinct().Skip(1).Any();
            if (mixedSource)
            {
                ImGui.TextDisabled(L10n.Tr("msg_mixed"));
                ImGui.PopID();
                return;
            }

            if (lights[0].ShadowLayerSource == Light2DMaskSource.PhysicsGroup)
                DrawPhysicsGroupMaskDropdown("", mixed ? 0UL : (ulong)(val ?? 0UL), onUpdate, true, mixed);
            else
                DrawSortingLayerMaskDropdown("", mixed ? 0UL : (ulong)(val ?? 0UL), onUpdate, true, mixed);
            ImGui.PopID();
            return;
        }

        if (targetType == typeof(Light2D) && type == typeof(FilterType))
        {
            var lights = components.OfType<Light2D>().ToList();
            Type? requiredType = GetLightFilterType(member.Name, lights);
            if (requiredType == null)
            {
                ImGui.TextDisabled(L10n.Tr("msg_mixed"));
                ImGui.PopID();
                return;
            }

            DrawFilterField("", mixed ? null : val as FilterType, onUpdate, true, mixed, requiredType);
            ImGui.PopID();
            return;
        }

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
        else if (type == typeof(Vector3)) { Vector3 v3 = (Vector3)(val ?? Vector3.Zero); var raw = (System.Numerics.Vector3)v3; if (ImGui.DragFloat3("##v", ref raw, 0.1f)) { changed = true; val = (Vector3)raw; } }
        else if (type == typeof(Vector4)) { Vector4 v4 = (Vector4)(val ?? Vector4.Zero); if (ImGui.DragFloat4("##v", ref v4, 0.1f)) { changed = true; val = v4; } }
        else if (type == typeof(Color)) { var c = (Color)(val ?? Color.White); var v4 = (Vector4)c; if (ImGui.ColorEdit4("##v", ref v4)) { changed = true; val = (Color)v4; } }
        else if (val is Enum enumValue) { string[] names = Enum.GetNames(type); string[] displayNames = names.Select(name => GetEnumDisplayName(type, name)).ToArray(); int curr = Array.IndexOf(names, enumValue.ToString()); if (ImGui.Combo("##v", ref curr, displayNames, displayNames.Length)) { changed = true; val = Enum.Parse(type, names[curr]); } }
        else { ImGui.TextDisabled(mixed ? L10n.Tr("msg_mixed") : (val?.ToString() ?? L10n.Tr("msg_none"))); }
        if (mixed) ImGui.PopStyleColor();
        if (changed) onUpdate(val);
        ImGui.PopID();
    }

    private void DrawEntityInspector(Entity entity)
    {
        DrawBlueprintInstanceInspectorHeader(entity);

        ImGui.PushID("EntityHeader");
        bool active = entity.Active; if (ImGui.Checkbox($"{L10n.Tr("label_active")}##Active", ref active)) entity.Active = active;
        ImGui.SameLine(); string name = entity.Name; if (ImGui.InputText("##Name", ref name, 128)) entity.Name = name;
        ImGui.Separator();
        DrawTagDropdown(L10n.Tr("label_tag"), entity.Tag, val => entity.Tag = (string?)val ?? L10n.Tr("label_untagged"));
        ImGui.PopID();
        ImGui.Separator();
        var components = entity.GetAllComponents();
        for (int i = 0; i < components.Count; i++) DrawComponent(components[i], entity);
        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button(L10n.Tr("btn_add_component"), new Vector2(-1, 30))) ImGui.OpenPopup("AddComponentPopup");
        if (ImGui.BeginPopup("AddComponentPopup")) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64); ImGui.Separator();
            var types = _app.ScriptCompiler?.GetAllAddableComponentTypes() ?? new List<Type>();
            _assetPickerCache.Remove("lua-add-component");
            var luaEntries = GetAssetPickerEntries("lua-add-component", static path => Path.GetExtension(path).Equals(".lua", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var type in types.OrderBy(static type => type.Name, StringComparer.OrdinalIgnoreCase)) {
                string typeName = type.Name;
                string displayName = GetAddComponentTypeDisplayName(type);

                if (MatchesAddComponentSearch(_searchFilter, typeName, displayName))
                {
                    bool canAdd = entity.CanAddComponent(type, out _);
                    if (!canAdd) ImGui.BeginDisabled();
                    if (ImGui.MenuItem(displayName) && canAdd) { _app.BeginUndoAction(); entity.AddComponent(type); _app.EndUndoAction(); ImGui.CloseCurrentPopup(); }
                    if (!canAdd) ImGui.EndDisabled();
                }
            }

            bool canAddLuaScript = entity.CanAddComponent(typeof(LuaScriptComponent), out _);
            foreach (var entry in luaEntries)
            {
                string luaScriptName = Path.GetFileNameWithoutExtension(entry.RelativePath);
                string displayName = $"{luaScriptName} (Lua Script)";
                string detailLabel = $"{displayName}##{entry.RelativePath}";

                if (!MatchesAddComponentSearch(_searchFilter, luaScriptName, entry.RelativePath, displayName))
                    continue;

                if (!canAddLuaScript) ImGui.BeginDisabled();
                if (ImGui.MenuItem(detailLabel) && canAddLuaScript)
                {
                    _app.BeginUndoAction();
                    var luaScript = entity.AddComponent<LuaScriptComponent>();
                    luaScript.ScriptPath = entry.RelativePath;
                    luaScript.ScriptGuid = AssetPathUtility.TryGetGuid(entry.FullPath) ?? string.Empty;
                    _app.EndUndoAction();
                    ImGui.CloseCurrentPopup();
                }
                if (!canAddLuaScript) ImGui.EndDisabled();

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(entry.RelativePath);
            }

            ImGui.EndPopup();
        }
    }

    private static bool MatchesAddComponentSearch(string filter, params string?[] candidates)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        foreach (string? candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                candidate.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetAddComponentTypeDisplayName(Type type)
    {
        string typeName = type.Name;
        string localizedName = L10n.Tr($"type_{typeName}");
        if (localizedName == $"type_{typeName}")
            localizedName = typeName;

        return IsUserCSharpScriptType(type)
            ? $"{localizedName} (C# Script)"
            : localizedName;
    }

    private static bool IsUserCSharpScriptType(Type type)
    {
        if (!typeof(Script).IsAssignableFrom(type) || type == typeof(LuaScriptComponent))
            return false;

        string assemblyName = type.Assembly.GetName().Name ?? string.Empty;
        return !assemblyName.StartsWith("Verity.Core", StringComparison.Ordinal) &&
               !assemblyName.StartsWith("Verity.Graphics", StringComparison.Ordinal);
    }

    private void DrawBlueprintInstanceInspectorHeader(Entity entity)
    {
        if (!entity.IsBlueprintInstance)
            return;

        ImGui.TextColored(new Vector4(0.35f, 0.9f, 1f, 1f), L10n.Tr("msg_blueprint_instance"));
        ImGui.Text($"{L10n.Tr("msg_source")}: {AssetPathUtility.DisplayName(entity.BlueprintAssetPath)}");

        if (!string.IsNullOrWhiteSpace(entity.BlueprintAssetPath) &&
            ImGui.Button(L10n.Tr("btn_open_source_blueprint"), new Vector2(-1, 0)))
        {
            string resolvedPath = AssetPathUtility.ResolvePath(_app.ProjectPath ?? _app.AssetsPath, entity.BlueprintAssetPath, entity.BlueprintAssetGuid);
            if (File.Exists(resolvedPath))
            {
                EditorSelection.ClearSelection();
                EditorSelection.SelectedAssetPath = resolvedPath;
            }
        }

        List<string> overrides = GetBlueprintOverrideLabels(entity);
        if (overrides.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextColored(new Vector4(0.35f, 0.65f, 1f, 1f), L10n.Tr("label_overrides"));
            foreach (string item in overrides)
                ImGui.TextColored(new Vector4(0.35f, 0.65f, 1f, 1f), item);
        }

        ImGui.Separator();
    }

    private List<string> GetBlueprintOverrideLabels(Entity entity)
    {
        if (!entity.IsBlueprintInstanceRoot && !entity.BlueprintSourceEntityId.HasValue)
            return [];

        Entity? root = FindBlueprintRoot(entity);
        if (root == null || !entity.BlueprintSourceEntityId.HasValue)
            return [];

        foreach (JsonNode? node in SceneSerializer.CaptureBlueprintInstanceOverrides(root))
        {
            if (!Guid.TryParse((string?)node?["SourceId"], out Guid sourceId) ||
                sourceId != entity.BlueprintSourceEntityId.Value)
            {
                continue;
            }

            var labels = new List<string>();
            if (node?["Name"] != null) labels.Add(LocalizeFieldName("Name"));
            if (node?["Active"] != null) labels.Add(LocalizeFieldName("Active"));
            if (node?["Position"] != null) labels.Add($"{LocalizeTypeName("Transform")}.{LocalizeFieldName("Position")}");
            if (node?["Rotation"] != null) labels.Add($"{LocalizeTypeName("Transform")}.{LocalizeFieldName("Rotation")}");
            if (node?["Scale"] != null) labels.Add($"{LocalizeTypeName("Transform")}.{LocalizeFieldName("Scale")}");

            if (node?["Components"] is JsonArray componentOverrides)
            {
                foreach (JsonNode? componentNode in componentOverrides)
                {
                string componentName = ((string?)componentNode?["Type"] ?? L10n.Tr("label_blueprint_component_fallback")).Split('.').Last();
                    string localizedComponentName = LocalizeTypeName(componentName);
                    if ((bool?)componentNode?["Added"] == true)
                    {
                        labels.Add(L10n.Tr("label_blueprint_added_component", localizedComponentName));
                        continue;
                    }

                    if ((bool?)componentNode?["Removed"] == true)
                    {
                        labels.Add(L10n.Tr("label_blueprint_removed_component", localizedComponentName));
                        continue;
                    }

                    if (componentNode?["Enabled"] != null)
                        labels.Add($"{localizedComponentName}.{LocalizeFieldName("Enabled")}");

                    if (componentNode?["Fields"] is JsonObject fields)
                    {
                        foreach (string fieldName in fields.Select(field => field.Key))
                            labels.Add($"{localizedComponentName}.{LocalizeFieldName(fieldName)}");
                    }
                }
            }

            return labels;
        }

        return [];
    }

    private static Entity? FindBlueprintRoot(Entity entity)
    {
        Entity? current = entity;
        while (current != null)
        {
            if (current.IsBlueprintInstanceRoot)
                return current;

            current = current.Transform.Parent?.Owner;
        }

        return null;
    }

    private void DrawAssetInspector(string path)
    {
        string fileName = Path.GetFileName(path);
        string extension = Path.GetExtension(path).ToLower();
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), $"{L10n.Tr("window_project")}: {fileName}");
        ImGui.Separator();
        if (string.Equals(fileName, "ProjectSettings.json", StringComparison.OrdinalIgnoreCase)) DrawProjectSettingsInspector();
        else if (string.Equals(fileName, "BuildSettings.json", StringComparison.OrdinalIgnoreCase)) DrawBuildSettingsInspector(path);
        else if (string.Equals(fileName, "Filters.json", StringComparison.OrdinalIgnoreCase)) _app.GetWindow<FilterEditorWindow>()?.DrawFilterEditor(true);
        else if (extension == ".blueprint") DrawBlueprintAssetInspector(path);
        else if (extension == ".cs" || extension == ".shader" || extension == ".lua") DrawScriptPreview(path);
        else if (extension == ".png" || extension == ".jpg" || extension == ".jpeg") DrawImagePreview(path);
        else if (extension is ".wav" or ".ogg" or ".mp3" or ".flac" or ".mod") DrawAudioFileInspector(path);
        else if (extension == ".verity") DrawWorldSettingsInspector(path);
        else if (extension == ".style") DrawStyleAssetInspector(path);
        else if (extension == ".rendertexture") DrawRenderTextureAssetInspector(path);
        else if (extension == ".ui") DrawUiAssetInspector(path);
        else if (extension == ".uiprefab") DrawUiPrefabInspector(path);
        else if (extension is ".tile" or ".animtile" or ".ruletile") DrawTileAssetInspector(path);
        else { ImGui.Text($"{L10n.Tr("label_type")}: {extension}"); ImGui.Text($"{L10n.Tr("label_path")}: {path}"); }
    }

    private void DrawUiAssetInspector(string path)
    {
        try
        {
            var screen = GetCachedUiScreen(path);
            if (ImGui.Button(L10n.Tr("btn_open_ui_editor"), new Vector2(-1, 30)))
            {
                EditorSelection.SelectedAssetPath = path;
                _app.OpenWindow<UIEditorWindow>();
            }

            ImGui.Separator();
            ImGui.Text($"{L10n.Tr("label_name")}: {screen.Name}");
            ImGui.Text($"{L10n.Tr("ui_label_reference_resolution")}: {screen.ReferenceResolution.X} x {screen.ReferenceResolution.Y}");
            ImGui.Text($"{L10n.Tr("ui_label_sorting_order")}: {screen.SortingOrder}");
            ImGui.Text($"{L10n.Tr("ui_label_nodes")}: {screen.Root.DescendantsAndSelf().Count()}");
        }
        catch (Exception e)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), L10n.Tr("msg_ui_load_error", e.Message));
        }
    }

    private void DrawUiPrefabInspector(string path)
    {
        try
        {
            var prefab = GetCachedUiPrefab(path);
            ImGui.Text($"{L10n.Tr("label_name")}: {prefab.Name}");
            ImGui.Text($"{L10n.Tr("ui_label_root")}: {prefab.Root.Name} ({GetUiNodeKindLabel(prefab.Root.Kind)})");
            ImGui.Text($"{L10n.Tr("ui_label_nodes")}: {prefab.Root.DescendantsAndSelf().Count()}");
        }
        catch (Exception e)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), L10n.Tr("msg_ui_prefab_load_error", e.Message));
        }
    }

    private UIScreenAsset GetCachedUiScreen(string path)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
        if (_cachedUiScreen == null ||
            !string.Equals(_cachedUiScreenPath, path, StringComparison.OrdinalIgnoreCase) ||
            _cachedUiScreenWriteTimeUtc != writeTimeUtc)
        {
            _cachedUiScreenPath = path;
            _cachedUiScreenWriteTimeUtc = writeTimeUtc;
            _cachedUiScreen = UiSerializer.Load(path);
        }

        return _cachedUiScreen;
    }

    private UiPrefabAsset GetCachedUiPrefab(string path)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
        if (_cachedUiPrefab == null ||
            !string.Equals(_cachedUiPrefabPath, path, StringComparison.OrdinalIgnoreCase) ||
            _cachedUiPrefabWriteTimeUtc != writeTimeUtc)
        {
            _cachedUiPrefabPath = path;
            _cachedUiPrefabWriteTimeUtc = writeTimeUtc;
            _cachedUiPrefab = UiSerializer.LoadPrefab(path);
        }

        return _cachedUiPrefab;
    }

    private string GetCachedTextFile(string path)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
        if (!_cachedTextFiles.TryGetValue(path, out var entry) || entry.WriteTimeUtc != writeTimeUtc)
        {
            entry = (writeTimeUtc, File.ReadAllText(path));
            _cachedTextFiles[path] = entry;
        }

        return entry.Content;
    }

    private StyleData GetCachedStyleData(string path)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
        if (!_cachedStyleData.TryGetValue(path, out var entry) || entry.WriteTimeUtc != writeTimeUtc)
        {
            entry = (writeTimeUtc, StyleData.FromJson(GetCachedTextFile(path)) ?? new StyleData());
            _cachedStyleData[path] = entry;
        }

        return entry.Data;
    }

    private BlueprintPreviewData GetCachedBlueprintPreview(string path)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(path);
        if (_cachedBlueprintPreview == null ||
            !string.Equals(_cachedBlueprintPath, path, StringComparison.OrdinalIgnoreCase) ||
            _cachedBlueprintWriteTimeUtc != writeTimeUtc)
        {
            _cachedBlueprintPath = path;
            _cachedBlueprintWriteTimeUtc = writeTimeUtc;
            _cachedBlueprintPreview = ParseBlueprintPreview(path);
        }

        return _cachedBlueprintPreview;
    }

    private void DrawTileAssetInspector(string path)
    {
        var tilePalette = _app.GetWindow<TilePaletteWindow>();
        if (tilePalette == null)
        {
            ImGui.Text($"{L10n.Tr("label_type")}: {Path.GetExtension(path)}");
            ImGui.Text($"{L10n.Tr("label_path")}: {path}");
            return;
        }

        if (!string.Equals(EditorSelection.SelectedAssetPath, path, StringComparison.OrdinalIgnoreCase) || EditorSelection.SelectedTile == null)
        {
            var json = File.ReadAllText(path);
            EditorSelection.SelectedAssetPath = path;
            EditorSelection.SelectedTile = JsonSerializer.Deserialize<TileBase>(json, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new Vector2Converter(), new Vector3Converter(), new Vector4Converter(), new SpriteConverter(), new StyleAssetConverter(), new ShaderAssetConverter(), new Verity.Core.Serialization.ColorConverter(), new TileBaseConverter(), new TilemapTilesConverter() }
            });
        }

        tilePalette.DrawSelectedTileInspector();
    }

    private void DrawBuildSettingsInspector(string path)
    {
        ImGui.Text($"{L10n.Tr("label_path")}: {path}");
        ImGui.Separator();
        BuildSettingsEditorUi.Draw(_app);
    }

    private void DrawBlueprintAssetInspector(string path)
    {
        try
        {
            var preview = GetCachedBlueprintPreview(path);

        if (ImGui.Button(L10n.Tr("btn_open_blueprint"), new Vector2(-1, 30)))
                _app.OpenBlueprintAsset(path);

            ImGui.Spacing();

            if (preview.HasPreviewSprite)
            {
                ImGui.Text(L10n.Tr("label_preview"));
                DrawSpritePreview(preview.PreviewSprite);
                ImGui.Separator();
            }

            bool hasWorld = WorldManager.ActiveWorld != null;
            if (!hasWorld)
                ImGui.BeginDisabled();
            if (ImGui.Button(L10n.Tr("btn_instantiate_blueprint"), new Vector2(-1, 30)))
                _app.InstantiateBlueprint(path);
            if (!hasWorld)
            {
                ImGui.EndDisabled();
                ImGui.TextDisabled(L10n.Tr("msg_no_active_world"));
            }

            ImGui.Separator();
            ImGui.Text($"{L10n.Tr("label_entities")}: {preview.Entities.Count}");
            ImGui.Text($"{L10n.Tr("label_root_entities")}: {preview.RootCount}");
            ImGui.Text($"{L10n.Tr("label_components")}: {preview.ComponentCount}");
            ImGui.Text($"{L10n.Tr("label_path")}: {path}");
            ImGui.Separator();

            if (ImGui.CollapsingHeader(L10n.Tr("label_blueprint_hierarchy"), ImGuiTreeNodeFlags.DefaultOpen))
            {
                foreach (var entity in preview.Entities.Where(e => e.ParentIndex < 0))
                    DrawBlueprintEntityNode(preview, entity.Index);
            }

            if (ImGui.CollapsingHeader(L10n.Tr("msg_source"), ImGuiTreeNodeFlags.None))
            {
                string json = GetCachedTextFile(path);
                ImGui.InputTextMultiline("##blueprint_json", ref json, (uint)json.Length + 1024, new Vector2(-1, 220), ImGuiInputTextFlags.ReadOnly);
            }
        }
        catch (Exception e)
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), L10n.Tr("msg_invalid_blueprint", e.Message));
        }
    }

    private void DrawProjectSettingsInspector()
    {
        var settings = _app.ProjectSettings;
        settings.UiCatalog ??= new List<UiAssetReference>();
        settings.UiRoleDefaults ??= new List<UiRoleBinding>();
        settings.StartupUiRoles ??= new List<string>();
        bool changed = false;
        if (ImGui.CollapsingHeader(L10n.Tr("header_general"), ImGuiTreeNodeFlags.DefaultOpen)) {
            float fontSize = settings.EditorFontSize; ImGui.Text(L10n.Tr("field_EditorFontSize")); ImGui.SameLine(120); if (ImGui.DragFloat("##v_editor_font_size", ref fontSize, 0.5f, 8f, 72f)) { settings.EditorFontSize = fontSize; changed = true; }
            int targetTps = settings.TargetTPS; ImGui.Text(L10n.Tr("field_TargetTPS")); ImGui.SameLine(120); if (ImGui.DragInt("##v_target_tps", ref targetTps, 1, 1, 1000)) { settings.TargetTPS = targetTps; changed = true; }
            int targetPtps = settings.TargetPTPS; ImGui.Text(L10n.Tr("field_TargetPTPS")); ImGui.SameLine(120); if (ImGui.DragInt("##v_target_ptps", ref targetPtps, 1, 1, 1000)) { settings.TargetPTPS = targetPtps; changed = true; }
            bool multiWindowEnabled = settings.MultiWindowEnabled; ImGui.Text(L10n.Tr("field_MultiWindowEnabled")); ImGui.SameLine(120); if (ImGui.Checkbox("##v_multi_window_enabled", ref multiWindowEnabled)) { settings.MultiWindowEnabled = multiWindowEnabled; if (multiWindowEnabled) _app.NormalizeCameraOutputsForProjectSettings(WorldManager.ActiveWorld); changed = true; }
            if (settings.MultiWindowEnabled)
            {
                int prewarmMode = (int)settings.MultiWindowPrewarmMode; ImGui.Text("Window Pool Mode"); ImGui.SameLine(120); if (ImGui.Combo("##v_multi_window_prewarm_mode", ref prewarmMode, "None\0Startup\0Lazy Background\0")) { settings.MultiWindowPrewarmMode = (MultiWindowPrewarmMode)Math.Clamp(prewarmMode, 0, 2); changed = true; }
                int prewarmCount = settings.MultiWindowPrewarmCount; ImGui.Text("Window Pool Count"); ImGui.SameLine(120); if (ImGui.DragInt("##v_multi_window_prewarm_count", ref prewarmCount, 1, 0, 64)) { settings.MultiWindowPrewarmCount = Math.Clamp(prewarmCount, 0, 64); changed = true; }
            }
            var bgColor = (Vector4)settings.EditorWorldBackgroundColor; ImGui.Text(L10n.Tr("field_EditorWorldBackgroundColor")); ImGui.SameLine(120); if (ImGui.ColorEdit4("##v_editor_world_background_color", ref bgColor)) { settings.EditorWorldBackgroundColor = (Color)bgColor; changed = true; }
            DrawAssetReferenceField(L10n.Tr("ui_field_default_ui_font"), settings.DefaultUiFontPath, ".fontasset;.sdfont", value =>
            {
                string path = value as string ?? string.Empty;
                settings.DefaultUiFontPath = AssetPathUtility.Normalize(path);
                settings.DefaultUiFontGuid = string.IsNullOrWhiteSpace(path) ? string.Empty : AssetPathUtility.EnsureMetaAndGetGuid(path);
                changed = true;
            });
        }
        if (ImGui.CollapsingHeader(L10n.Tr("header_physics"), ImGuiTreeNodeFlags.DefaultOpen)) {
            Vector2 gravity = settings.DefaultGravity; ImGui.Text(L10n.Tr("field_DefaultGravity")); ImGui.SameLine(120); if (ImGui.DragFloat2("##v_default_gravity", (float*)&gravity, 0.1f)) { settings.DefaultGravity = gravity; changed = true; }
            float friction = settings.DefaultFriction; ImGui.Text(L10n.Tr("field_DefaultFriction")); ImGui.SameLine(120); if (ImGui.DragFloat("##v_default_friction", ref friction, 0.01f, 0f, 1f)) { settings.DefaultFriction = friction; changed = true; }
            float bounciness = settings.DefaultBounciness; ImGui.Text(L10n.Tr("field_DefaultBounciness")); ImGui.SameLine(120); if (ImGui.DragFloat("##v_default_bounciness", ref bounciness, 0.01f, 0f, 1f)) { settings.DefaultBounciness = bounciness; changed = true; }
        }
        if (ImGui.CollapsingHeader(L10n.Tr("header_sprite_import"), ImGuiTreeNodeFlags.DefaultOpen)) {
            int ppu = settings.DefaultSpritePixelsPerUnit; ImGui.Text(L10n.Tr("field_DefaultSpritePixelsPerUnit")); ImGui.SameLine(120); if (ImGui.DragInt("##v_default_sprite_pixels_per_unit", ref ppu, 1f, 1, 4096)) { settings.DefaultSpritePixelsPerUnit = Math.Max(1, ppu); changed = true; }
            int threshold = settings.DefaultPointFilterMaxDimension; ImGui.Text(L10n.Tr("field_DefaultPointFilterMaxDimension")); ImGui.SameLine(120); if (ImGui.DragInt("##v_default_point_filter_max_dimension", ref threshold, 1f, 1, 8192)) { settings.DefaultPointFilterMaxDimension = Math.Max(1, threshold); changed = true; }
            int sizeMode = settings.DefaultSpriteSizeMode == SpriteSizingMode.FitInsideUnit ? 0 : 1; ImGui.Text(L10n.Tr("field_DefaultSpriteSizeMode")); ImGui.SameLine(120); if (ImGui.Combo("##v_default_sprite_size_mode", ref sizeMode, $"{L10n.Tr("sprite_size_mode_fit_inside_unit")}\0{L10n.Tr("sprite_size_mode_pixels_per_unit")}\0")) { settings.DefaultSpriteSizeMode = sizeMode == 0 ? SpriteSizingMode.FitInsideUnit : SpriteSizingMode.PixelsPerUnit; changed = true; }
        }
        changed |= DrawProjectSettingsList(L10n.Tr("header_tags"), settings.Tags, "Tag", false);
        changed |= DrawProjectSettingsList(L10n.Tr("header_sorting_layers"), settings.SortingLayers, "Layer", true);
        changed |= DrawProjectSettingsList(L10n.Tr("header_physics_groups"), settings.PhysicsGroups, "Group", false);
        changed |= DrawUiAssetReferenceList(L10n.Tr("ui_header_catalog"), settings.UiCatalog);
        changed |= DrawUiRoleBindingList(L10n.Tr("ui_header_role_defaults"), settings.UiRoleDefaults);
        changed |= DrawProjectSettingsList(L10n.Tr("ui_header_startup_ui_roles"), settings.StartupUiRoles, "UiRole", false);
        if (changed) _app.SaveProjectSettings();
    }

    private BlueprintPreviewData ParseBlueprintPreview(string path)
    {
        JsonNode? root = JsonNode.Parse(GetCachedTextFile(path));
        if (root is not JsonArray entitiesArray)
            throw new InvalidDataException("Blueprint root is not an array.");

        var entities = new List<BlueprintEntityPreview>();
        int componentCount = 0;
        Sprite previewSprite = default;
        bool hasPreviewSprite = _app.TryGetBlueprintPreviewSprite(path, out previewSprite);

        for (int i = 0; i < entitiesArray.Count; i++)
        {
            JsonObject? entityNode = entitiesArray[i] as JsonObject;
            if (entityNode == null)
                continue;

            var preview = new BlueprintEntityPreview
            {
                Index = i,
                ParentIndex = (int?)entityNode["ParentIndex"] ?? -1,
                Name = (string?)entityNode["Name"] ?? L10n.Tr("label_blueprint_entity_fallback", i),
                Active = (bool?)entityNode["Active"] ?? true,
                Position = ReadVector2(entityNode["Position"]),
                Rotation = (float?)entityNode["Rotation"] ?? 0f,
                Scale = ReadVector2(entityNode["Scale"], new Vector2(1, 1))
            };

            if (entityNode["Components"] is JsonArray componentsArray)
            {
                foreach (JsonNode? componentNode in componentsArray)
                {
                    if (componentNode is not JsonObject componentObject)
                        continue;

                    string typeName = (string?)componentObject["Type"] ?? L10n.Tr("label_blueprint_component_fallback");
                    var fields = componentObject["Fields"] as JsonObject;
                    preview.Components.Add(new BlueprintComponentPreview
                    {
                        Name = typeName.Split('.').Last(),
                        Fields = fields
                    });
                    componentCount++;

                }
            }

            entities.Add(preview);
        }

        foreach (var entity in entities)
        {
            if (entity.ParentIndex >= 0 && entity.ParentIndex < entities.Count)
                entities[entity.ParentIndex].Children.Add(entity.Index);
        }

        return new BlueprintPreviewData
        {
            Entities = entities,
            RootCount = entities.Count(entity => entity.ParentIndex < 0),
            ComponentCount = componentCount,
            PreviewSprite = previewSprite,
            HasPreviewSprite = hasPreviewSprite
        };
    }

    private static Vector2 ReadVector2(JsonNode? node, Vector2? fallback = null)
    {
        Vector2 value = fallback ?? Vector2.Zero;
        if (node == null)
            return value;

        value.X = (float?)node["X"] ?? value.X;
        value.Y = (float?)node["Y"] ?? value.Y;
        return value;
    }

    private void DrawBlueprintEntityNode(BlueprintPreviewData preview, int entityIndex)
    {
        if (entityIndex < 0 || entityIndex >= preview.Entities.Count)
            return;

        var entity = preview.Entities[entityIndex];
        string state = entity.Active ? L10n.Tr("label_active") : L10n.Tr("label_inactive");
        if (!ImGui.TreeNodeEx($"{entity.Name}##blueprint_entity_{entity.Index}", ImGuiTreeNodeFlags.DefaultOpen, $"{entity.Name} ({entity.Components.Count}) [{state}]"))
            return;

        ImGui.Text($"{L10n.Tr("label_position")}: {FormatVector2(entity.Position)}");
        ImGui.Text($"{L10n.Tr("label_rotation")}: {entity.Rotation:0.###}");
        ImGui.Text($"{L10n.Tr("label_scale")}: {FormatVector2(entity.Scale)}");

        if (entity.Components.Count > 0)
        {
            ImGui.Separator();
            ImGui.Text(L10n.Tr("label_components"));
            foreach (var component in entity.Components)
                DrawBlueprintComponent(component);
        }

        if (entity.Children.Count > 0)
        {
            ImGui.Separator();
            foreach (int childIndex in entity.Children)
                DrawBlueprintEntityNode(preview, childIndex);
        }

        ImGui.TreePop();
    }

    private void DrawBlueprintComponent(BlueprintComponentPreview component)
    {
        string localizedComponentName = LocalizeTypeName(component.Name);
        if (!ImGui.TreeNodeEx($"{localizedComponentName}##blueprint_component_{component.Name}", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        if (component.Fields == null || component.Fields.Count == 0)
        {
            ImGui.TextDisabled(L10n.Tr("msg_none"));
            ImGui.TreePop();
            return;
        }

        foreach (var field in component.Fields)
            ImGui.Text($"{LocalizeFieldName(field.Key)}: {FormatBlueprintValue(field.Value)}");

        ImGui.TreePop();
    }

    private void DrawSpritePreview(Sprite sprite)
    {
        var texture = _app.LoadSpriteTexture(sprite);
        if (texture == null || texture.ImGuiTextureId == 0)
        {
            ImGui.TextDisabled(AssetPathUtility.DisplayName(sprite.Path));
            return;
        }

        var slice = _app.ResolveSpriteSlice(sprite);
        Vector2 size = new(Math.Min(192, Math.Max(32, slice.Width * 4)), Math.Min(192, Math.Max(32, slice.Height * 4)));
        Vector2 uvMin = new(slice.X / (float)Math.Max(1, texture.Width), 1f - (slice.Y / (float)Math.Max(1, texture.Height)));
        Vector2 uvMax = new((slice.X + slice.Width) / (float)Math.Max(1, texture.Width), 1f - ((slice.Y + slice.Height) / (float)Math.Max(1, texture.Height)));
        ImGui.Image(new ImTextureRef(null, new ImTextureID(texture.ImGuiTextureId)), size, uvMin, uvMax);
        ImGui.TextDisabled(AssetPathUtility.DisplayName(sprite.Path));
    }

    private static string FormatVector2(Vector2 value) => $"{value.X:0.###}, {value.Y:0.###}";

    private static string FormatBlueprintValue(JsonNode? value)
    {
        if (value == null)
            return L10n.Tr("msg_none");

        if (value is JsonValue jsonValue)
            return jsonValue.ToJsonString().Trim('"');

        if (value is JsonObject obj)
        {
            if (obj["Path"] != null)
            {
                string path = (string?)obj["Path"] ?? string.Empty;
                string spriteId = (string?)obj["SpriteId"] ?? string.Empty;
                string name = AssetPathUtility.DisplayName(path);
                return string.IsNullOrWhiteSpace(spriteId) ? name : $"{name} [{spriteId}]";
            }

            if (obj["EntityId"] != null || obj["ComponentType"] != null)
            {
                string type = (string?)obj["ComponentType"] ?? L10n.Tr("label_blueprint_component_fallback");
                string id = (string?)obj["EntityId"] ?? string.Empty;
                return L10n.Tr("label_blueprint_component_ref", LocalizeTypeName(type.Split('.').Last()), id);
            }

            string[] orderedKeys = ["X", "Y", "Z", "W", "R", "G", "B", "A"];
            if (orderedKeys.Any(obj.ContainsKey))
            {
                var values = orderedKeys.Where(obj.ContainsKey).Select(key => $"{key}:{((JsonValue)obj[key]!).ToJsonString().Trim('"')}");
                return string.Join(", ", values);
            }

            return "{...}";
        }

        if (value is JsonArray array)
            return $"[{array.Count}]";

        return value.ToJsonString();
    }

    private bool DrawProjectSettingsList(string header, List<string> list, string idPrefix, bool allowReorder)
    {
        bool changed = false;
        if (ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen)) {
            ImGui.Indent();
            for (int i = 0; i < list.Count; i++) {
                ImGui.PushID($"{idPrefix}_{i}");
                ImGui.BeginGroup();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                bool removed = false;
                if (ImGui.Button("X", new Vector2(25, 0))) { list.RemoveAt(i); changed = true; removed = true; }
                ImGui.PopStyleColor();
                if (!removed && allowReorder) {
                    ImGui.SameLine(); if (ImGui.Button("^", new Vector2(25, 0)) && i > 0) { (list[i], list[i - 1]) = (list[i - 1], list[i]); changed = true; }
                    ImGui.SameLine(); if (ImGui.Button("v", new Vector2(25, 0)) && i < list.Count - 1) { (list[i], list[i + 1]) = (list[i + 1], list[i]); changed = true; }
                }
                if (!removed) { ImGui.SameLine(); string val = list[i]; ImGui.SetNextItemWidth(-1); if (ImGui.InputText("##edit", ref val, 64)) { list[i] = val; changed = true; } }
                ImGui.EndGroup();

                if (removed)
                {
                    ImGui.PopID();
                    break;
                }

                if (DrawCollectionItemContextMenu(i, list, idPrefix, () => changed = true, CloneCollectionItem, allowReorder))
                {
                    ImGui.PopID();
                    break;
                }

                ImGui.PopID();
            }
            ImGui.Dummy(new Vector2(0, 5));
            if (ImGui.Button($"+ {L10n.Tr("btn_add")}##{idPrefix}", new Vector2(-1, 25))) { list.Add($"{idPrefix}_{list.Count}"); changed = true; }
            ImGui.Unindent();
        }
        return changed;
    }

    private bool DrawUiAssetReferenceList(string header, List<UiAssetReference> list)
    {
        bool changed = false;
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
            return false;

        for (int i = 0; i < list.Count; i++)
        {
            UiAssetReference entry = list[i];
            ImGui.PushID($"ui-catalog-{i}");
            ImGui.BeginGroup();
            bool removed = false;

            string name = entry.Name ?? string.Empty;
            if (ImGui.InputText(L10n.Tr("label_name"), ref name, 128))
            {
                entry.Name = name;
                changed = true;
            }

            if (DrawUiAssetField(L10n.Tr("label_asset"), entry.Asset, out UiAsset selectedAsset))
            {
                entry.Asset = selectedAsset;
                if (string.IsNullOrWhiteSpace(entry.Name))
                    entry.Name = Path.GetFileNameWithoutExtension(selectedAsset.Path);
                changed = true;
            }

            if (ImGui.Button(L10n.Tr("ctx_remove"), new Vector2(80f, 0f)))
            {
                list.RemoveAt(i);
                changed = true;
                removed = true;
            }

            ImGui.EndGroup();

            if (removed)
            {
                ImGui.PopID();
                break;
            }

            if (DrawCollectionItemContextMenu(i, list, "ui-catalog", () => changed = true, CloneCollectionItem))
            {
                ImGui.PopID();
                break;
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(L10n.Tr("ui_btn_add_ui_asset"), new Vector2(-1, 0)))
        {
            list.Add(new UiAssetReference());
            changed = true;
        }

        return changed;
    }

    private bool DrawUiRoleBindingList(string header, List<UiRoleBinding> list)
    {
        bool changed = false;
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
            return false;

        for (int i = 0; i < list.Count; i++)
        {
            UiRoleBinding binding = list[i];
            ImGui.PushID($"ui-role-{i}");
            ImGui.BeginGroup();
            bool removed = false;

            string role = binding.Role ?? string.Empty;
            if (ImGui.InputText(L10n.Tr("ui_field_role"), ref role, 128))
            {
                binding.Role = role;
                changed = true;
            }

            if (DrawUiAssetField(L10n.Tr("label_asset"), binding.Asset, out UiAsset selectedAsset))
            {
                binding.Asset = selectedAsset;
                changed = true;
            }

            if (ImGui.Button(L10n.Tr("ctx_remove"), new Vector2(80f, 0f)))
            {
                list.RemoveAt(i);
                changed = true;
                removed = true;
            }

            ImGui.EndGroup();

            if (removed)
            {
                ImGui.PopID();
                break;
            }

            if (DrawCollectionItemContextMenu(i, list, "ui-role", () => changed = true, CloneCollectionItem))
            {
                ImGui.PopID();
                break;
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(L10n.Tr("ui_btn_add_ui_role"), new Vector2(-1, 0)))
        {
            list.Add(new UiRoleBinding());
            changed = true;
        }

        return changed;
    }

    private static bool TryGetSelectedUiAsset(out string path, out string guid)
    {
        path = string.Empty;
        guid = string.Empty;

        string? selectedPath = EditorSelection.SelectedAssetPath;
        if (string.IsNullOrWhiteSpace(selectedPath) || !selectedPath.EndsWith(".ui", StringComparison.OrdinalIgnoreCase))
            return false;

        path = AssetPathUtility.Normalize(selectedPath);
        guid = AssetPathUtility.TryGetGuid(selectedPath);
        return true;
    }

    private bool DrawUiAssetField(string label, UiAsset current, out UiAsset updated)
    {
        updated = current;
        string display = string.IsNullOrWhiteSpace(current.Path) ? L10n.Tr("msg_none") : AssetPathUtility.DisplayName(current.Path);
        bool changed = false;

        if (DrawReferenceSlot(label, display, current.Path) && !string.IsNullOrWhiteSpace(current.Path))
            RevealAssetReference(current.Path);

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (payload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".ui", StringComparison.OrdinalIgnoreCase))
            {
                updated = new UiAsset(EditorSelection.DraggedAssetPath, AssetPathUtility.TryGetGuid(EditorSelection.DraggedAssetPath));
                changed = true;
            }
            ImGui.EndDragDropTarget();
        }

        if (DrawReferencePickerButton())
            ImGui.OpenPopup("UiAssetPicker");

        if (ImGui.BeginPopup("UiAssetPicker"))
        {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            if (ImGui.MenuItem(L10n.Tr("msg_none")))
            {
                updated = default;
                changed = true;
            }

            foreach (var entry in GetAssetPickerEntries("ui", static path => Path.GetExtension(path).Equals(".ui", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.IsNullOrEmpty(_searchFilter) && !entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ImGui.MenuItem(entry.RelativePath))
                {
                    updated = new UiAsset(entry.FullPath, AssetPathUtility.TryGetGuid(entry.FullPath));
                    changed = true;
                }
            }

            if (ImGui.MenuItem(L10n.Tr("ui_btn_use_selected_ui")) && TryGetSelectedUiAsset(out string selectedPath, out string selectedGuid))
            {
                updated = new UiAsset(selectedPath, selectedGuid);
                changed = true;
            }

            ImGui.EndPopup();
        }

        return changed;
    }

    private void DrawStyleAssetInspector(string path)
    {
        try {
            var data = GetCachedStyleData(path);
            ImGui.Text(L10n.Tr("label_style_asset_editor")); ImGui.Separator();
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - 100);
            DrawShaderField(L10n.Tr("CreationType_Shader"), (ShaderAsset)(data.ShaderPath ?? ""), val => { 
                if (val is ShaderAsset sa) data.ShaderPath = NormalizeStyleAssetPath(sa.Path);
                else if (val is string s) data.ShaderPath = NormalizeStyleAssetPath(s);
                SaveStyle(path, data); 
            });
            ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("btn_refresh"), new Vector2(-1, 0))) {
                string relPath = Path.GetRelativePath(_app.ProjectPath!, path).Replace("\\", "/");
                _app.RenderPipeline.ClearStyleCache(relPath);
                if (!string.IsNullOrEmpty(data.ShaderPath)) _app.RenderPipeline.ClearShaderCache(data.ShaderPath);
                _app.ShowOverlayMessage(L10n.Tr("msg_refreshed_style_shader_cache"));
            }
            ImGui.Dummy(new Vector2(0, 5));
            if (!string.IsNullOrEmpty(data.ShaderPath)) {
                string shaderFullPath = ResolveAssetPath(data.ShaderPath);
                if (File.Exists(shaderFullPath)) {
                    string shaderContent = GetCachedTextFile(shaderFullPath);
                    var uniforms = Shader2D.ParseUniforms(shaderContent);
                    var customUniforms = uniforms.Where(u => u.Name != "uProjection" && u.Name != "uView" && u.Name != "uModel" && u.Name != "uTexture" && u.Name != "uColor").ToList();
                    if (customUniforms.Count > 0) {
                        foreach (var u in customUniforms) {
                            ImGui.PushID(u.Name); ImGui.Text(u.Name); ImGui.SameLine(120); bool changed = false;
        if (u.Type == "float") { float val = data.Floats.TryGetValue(u.Name, out var f) ? f : 0f; if (val == 0f && u.Name.Contains("Count")) ImGui.TextColored(new Vector4(1, 1, 0, 1), L10n.Tr("msg_warning_zero_black_screen")); if (ImGui.DragFloat("##v", ref val, 0.1f)) { data.Floats[u.Name] = val; changed = true; } }
                            else if (u.Type == "vec2") { Vector2 val = data.Vector2s.TryGetValue(u.Name, out var v) ? v : Vector2.Zero; if (ImGui.DragFloat2("##v", (float*)&val, 0.1f)) { data.Vector2s[u.Name] = val; changed = true; } }
                            else if (u.Type == "vec3") { System.Numerics.Vector3 val = data.Vector3s.TryGetValue(u.Name, out var v) ? v : System.Numerics.Vector3.Zero; if (ImGui.DragFloat3("##v", (float*)&val, 0.1f)) { data.Vector3s[u.Name] = val; changed = true; } }
                            else if (u.Type == "vec4") {
                                if (u.Name.Contains("Color", StringComparison.OrdinalIgnoreCase)) { var c = data.Colors.TryGetValue(u.Name, out var col) ? col : Color.White; var v4 = (Vector4)c; if (ImGui.ColorEdit4("##v", ref v4)) { data.Colors[u.Name] = (Color)v4; changed = true; } }
                                else { Vector4 val = data.Vector4s.TryGetValue(u.Name, out var v) ? v : Vector4.One; if (ImGui.DragFloat4("##v", ref val)) { data.Vector4s[u.Name] = val; changed = true; } }
                            }
                            else if (u.Type == "sampler2D") { string val = data.Textures.TryGetValue(u.Name, out var s) ? s : ""; DrawAssetReferenceField("##v", val, ".png;.jpg;.jpeg", newVal => { data.Textures[u.Name] = NormalizeStyleAssetPath((string?)newVal) ?? string.Empty; SaveStyle(path, data); }); }
                            if (changed) { SaveStyle(path, data); string relPath = Path.GetRelativePath(_app.ProjectPath!, path).Replace("\\", "/"); _app.RenderPipeline.ClearStyleCache(relPath); }
                            ImGui.PopID();
                        }
                } else ImGui.TextDisabled(L10n.Tr("msg_no_custom_parameters"));
            } else ImGui.TextColored(new Vector4(1, 0, 0, 1), L10n.Tr("msg_shader_not_found"));
        } else ImGui.TextDisabled(L10n.Tr("msg_select_shader"));
        } catch (Exception e) { ImGui.TextColored(new Vector4(1, 0, 0, 1), L10n.Tr("msg_error_generic", e.Message)); }
    }

    private string ResolveAssetPath(string p) => Path.IsPathRooted(p) ? p : (_app.ProjectPath == null ? p : Path.Combine(_app.ProjectPath, p));
    private string? NormalizeStyleAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string normalized = path.Replace('\\', '/');
        if (_app.ProjectPath != null)
        {
            string projectPath = _app.ProjectPath.Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith(projectPath + "/", StringComparison.OrdinalIgnoreCase))
                return normalized[(projectPath.Length + 1)..];
        }

        int assetsIndex = normalized.LastIndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
        if (assetsIndex >= 0)
            return normalized[(assetsIndex + 1)..];

        return normalized;
    }
    private void SaveStyle(string path, StyleData data) { try { string json = data.ToJson(); File.WriteAllText(path, json); _cachedTextFiles[path] = (File.GetLastWriteTimeUtc(path), json); _cachedStyleData[path] = (File.GetLastWriteTimeUtc(path), data); if (_app.ProjectPath != null) { string relPath = Path.GetRelativePath(_app.ProjectPath, path).Replace("\\", "/"); _app.RenderPipeline.ClearStyleCache(relPath); } } catch { } }

    private void DrawRenderTextureAssetInspector(string path)
    {
        var data = CameraTextureAssetData.Load(path, null, _app.ProjectPath);
        bool changed = false;

        ImGui.Text(L10n.Tr("CreationType_RenderTexture"));
        ImGui.Separator();

        int width = Math.Max(1, data.Width);
        int height = Math.Max(1, data.Height);
        if (ImGui.InputInt("Width", ref width))
        {
            data.Width = Math.Max(1, width);
            changed = true;
        }

        if (ImGui.InputInt("Height", ref height))
        {
            data.Height = Math.Max(1, height);
            changed = true;
        }

        if (changed)
            data.Save(path, _app.ProjectPath);
    }

    private void DrawWorldSettingsInspector(string path) {
        var world = WorldManager.ActiveWorld;
        if (world != null && string.Equals(world.Name, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)) {
            world.UiRoleOverrides ??= new List<UiRoleBinding>();
            ImGui.Text(L10n.Tr("msg_active_world_settings"));
            ImGui.Separator();
            DrawGenericInspector(world);
            if (DrawUiRoleBindingList(L10n.Tr("ui_header_role_overrides"), world.UiRoleOverrides))
                _app.RecordUndo();
            if (ImGui.Button(L10n.Tr("btn_save_world"), new Vector2(-1, 30)))
                _app.GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset();
        }
        else {
            ImGui.Text(L10n.Tr("msg_selected_world_not_active"));
            if (ImGui.Button(L10n.Tr("btn_load_world"), new Vector2(-1, 40)))
                _app.GetWindow<ProjectWindow>()?.LoadWorldByPath(path);
        }
    }

    private void DrawScriptPreview(string path) { try { string code = GetCachedTextFile(path); ImGui.Text(L10n.Tr("msg_source")); ImGui.InputTextMultiline("##code", ref code, (uint)code.Length + 1024, new Vector2(-1, -1), ImGuiInputTextFlags.ReadOnly); } catch { ImGui.Text(L10n.Tr("msg_error_reading_file")); } }

    private void DrawImagePreview(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var settings = _app.GetOrCreateSpriteImportSettings(fullPath);
        var tex = _app.TextureManager.Load(fullPath, settings.Filter);
        var raw = _app.TextureManager.GetRawPixels(fullPath);
        settings.Normalize(raw.Width, raw.Height);

        if (EditorSelection.SelectedSpriteAsset.HasValue &&
            string.Equals(ResolveAssetPath(EditorSelection.SelectedSpriteAsset.Value.Path), fullPath, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(EditorSelection.SelectedSpriteAsset.Value.SpriteId))
        {
            _selectedSliceIds[fullPath] = EditorSelection.SelectedSpriteAsset.Value.SpriteId;
        }

        ImGui.Text($"{L10n.Tr("label_size")}: {raw.Width}x{raw.Height}");
        ImGui.Text($"{L10n.Tr("field_SpriteFilter")}: {GetSpriteFilterLabel(settings.Filter)}");
        ImGui.Text($"{L10n.Tr("field_SpriteMode")}: {GetSpriteModeLabel(settings.SpriteMode)}");
        ImGui.Separator();

        bool changed = false;

        int filterIndex = settings.Filter == SpriteTextureFilter.Point ? 0 : 1;
        ImGui.Text(L10n.Tr("field_SpriteFilter"));
        ImGui.SameLine(120);
        if (ImGui.Combo("##v_sprite_filter", ref filterIndex, $"{L10n.Tr("sprite_filter_point")}\0{L10n.Tr("sprite_filter_linear")}\0"))
        {
            settings.Filter = filterIndex == 0 ? SpriteTextureFilter.Point : SpriteTextureFilter.Linear;
            changed = true;
        }

        int modeIndex = settings.SpriteMode == SpriteImportMode.Single ? 0 : 1;
        ImGui.Text(L10n.Tr("field_SpriteMode"));
        ImGui.SameLine(120);
        if (ImGui.Combo("##v_sprite_mode", ref modeIndex, $"{L10n.Tr("sprite_mode_single")}\0{L10n.Tr("sprite_mode_multiple")}\0"))
        {
            settings.SpriteMode = modeIndex == 0 ? SpriteImportMode.Single : SpriteImportMode.Multiple;
            if (settings.SpriteMode == SpriteImportMode.Single)
            {
                string keepId = settings.Slices.FirstOrDefault()?.Id ?? string.Empty;
                settings.Slices = [AssetPathUtility.ResolveSpriteSlice(fullPath, new Sprite(fullPath, AssetPathUtility.TryGetGuid(fullPath), keepId), raw.Width, raw.Height)];
            }
            changed = true;
        }

        int sizeMode = settings.SizeMode == SpriteSizingMode.FitInsideUnit ? 0 : 1;
        ImGui.Text(L10n.Tr("field_SpriteSizeMode"));
        ImGui.SameLine(120);
        if (ImGui.Combo("##v_sprite_size_mode", ref sizeMode, $"{L10n.Tr("sprite_size_mode_fit_inside_unit")}\0{L10n.Tr("sprite_size_mode_pixels_per_unit")}\0"))
        {
            settings.SizeMode = sizeMode == 0 ? SpriteSizingMode.FitInsideUnit : SpriteSizingMode.PixelsPerUnit;
            changed = true;
        }

        int ppu = settings.PixelsPerUnit;
        ImGui.Text(L10n.Tr("field_PixelsPerUnit"));
        ImGui.SameLine(120);
        if (ImGui.DragInt("##v_pixels_per_unit", ref ppu, 1f, 1, 4096))
        {
            settings.PixelsPerUnit = Math.Max(1, ppu);
            changed = true;
        }

        Vector2 defaultPivot = settings.DefaultPivot;
        ImGui.Text(L10n.Tr("field_DefaultPivot"));
        ImGui.SameLine(120);
        if (ImGui.DragFloat2("##v_default_pivot", (float*)&defaultPivot, 0.01f, 0f, 1f))
        {
            settings.DefaultPivot = SpriteImportUtility.ClampPivot(defaultPivot);
            changed = true;
        }

        int recommendedThreshold = Math.Max(1, _app.ProjectSettings.DefaultPointFilterMaxDimension);
        SpriteTextureFilter recommendedFilter = Math.Max(raw.Width, raw.Height) <= recommendedThreshold ? SpriteTextureFilter.Point : SpriteTextureFilter.Linear;
        if (ImGui.Button($"{L10n.Tr("btn_use_recommended_filter")} ({GetSpriteFilterLabel(recommendedFilter)})", new Vector2(-1, 0)))
        {
            settings.Filter = recommendedFilter;
            changed = true;
        }

        ImGui.Separator();
        DrawSpriteImportPreview(fullPath, tex, raw.Width, raw.Height, settings);
        ImGui.Separator();
        changed |= DrawSpriteSliceEditor(fullPath, raw.Width, raw.Height, settings);

        if (changed)
        {
            settings.Normalize(raw.Width, raw.Height);
            AssetPathUtility.SaveSpriteImportSettings(fullPath, settings);
        }
    }

    private void DrawSpriteImportPreview(string fullPath, RenderTexture tex, int width, int height, SpriteImportSettings settings)
    {
        if (tex.ImGuiTextureId == 0)
            return;

        SpriteSlice selected = GetSelectedSlice(fullPath, settings, width, height);
        float maxWidth = Math.Max(64f, ImGui.GetContentRegionAvail().X);
        float scale = Math.Min(1.0f, maxWidth / Math.Max(1f, width));
        var drawSize = new Vector2(width * scale, height * scale);
        var uvMin = new Vector2(selected.X / (float)Math.Max(1, width), 1f - (selected.Y / (float)Math.Max(1, height)));
        var uvMax = new Vector2((selected.X + selected.Width) / (float)Math.Max(1, width), 1f - ((selected.Y + selected.Height) / (float)Math.Max(1, height)));

        ImGui.Text($"{L10n.Tr("label_preview_slice")}: {selected.Name}");
        ImGui.Image(new ImTextureRef(null, new ImTextureID(tex.ImGuiTextureId)), drawSize, new Vector2(0, 1), new Vector2(1, 0));
        ImGui.Text($"{L10n.Tr("label_slice_rect")}: {selected.X}, {selected.Y}, {selected.Width}, {selected.Height}");
        ImGui.Image(new ImTextureRef(null, new ImTextureID(tex.ImGuiTextureId)), new Vector2(Math.Min(192, selected.Width * 4), Math.Min(192, selected.Height * 4)), uvMin, uvMax);
    }

    private bool DrawSpriteSliceEditor(string fullPath, int textureWidth, int textureHeight, SpriteImportSettings settings)
    {
        bool changed = false;
        if (settings.SpriteMode == SpriteImportMode.Single)
        {
            var single = settings.Slices.FirstOrDefault() ?? SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, settings.DefaultPivot);
            EditSlice(L10n.Tr("label_single_sprite"), single, settings, textureWidth, textureHeight, updated =>
            {
                settings.Slices = [updated];
                changed = true;
            });
            return changed;
        }

        ImGui.Text(L10n.Tr("label_slices"));
        if (ImGui.Button(L10n.Tr("btn_add_slice"), new Vector2(-1, 0)))
        {
            settings.Slices.Add(new SpriteSlice
            {
                Name = L10n.Tr("sprite_default_name_n", settings.Slices.Count + 1),
                X = 0,
                Y = 0,
                Width = Math.Max(1, Math.Min(textureWidth, _sliceGridCellWidth)),
                Height = Math.Max(1, Math.Min(textureHeight, _sliceGridCellHeight)),
                Pivot = settings.DefaultPivot
            });
            changed = true;
        }

        if (ImGui.Button(L10n.Tr("btn_reset_to_full_image"), new Vector2(-1, 0)))
        {
            settings.SpriteMode = SpriteImportMode.Single;
            settings.Slices = [SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, settings.DefaultPivot)];
            changed = true;
            return changed;
        }

        ImGui.Separator();
        ImGui.Text(L10n.Tr("label_grid_slice"));
        ImGui.DragInt(L10n.Tr("field_CellWidth"), ref _sliceGridCellWidth, 1f, 1, textureWidth);
        ImGui.DragInt(L10n.Tr("field_CellHeight"), ref _sliceGridCellHeight, 1f, 1, textureHeight);
        ImGui.DragInt(L10n.Tr("field_OffsetX"), ref _sliceGridOffsetX, 1f, 0, textureWidth);
        ImGui.DragInt(L10n.Tr("field_OffsetY"), ref _sliceGridOffsetY, 1f, 0, textureHeight);
        ImGui.DragInt(L10n.Tr("field_PaddingX"), ref _sliceGridPaddingX, 1f, 0, textureWidth);
        ImGui.DragInt(L10n.Tr("field_PaddingY"), ref _sliceGridPaddingY, 1f, 0, textureHeight);
        if (ImGui.Button(L10n.Tr("btn_apply_grid_slice"), new Vector2(-1, 0)))
        {
            settings.Slices = CreateGridSlices(textureWidth, textureHeight, settings.DefaultPivot);
            changed = true;
        }

        ImGui.Separator();
        string selectedId = GetSelectedSliceId(fullPath, settings);
        for (int i = 0; i < settings.Slices.Count; i++)
        {
            var slice = settings.Slices[i];
            bool selected = string.Equals(selectedId, slice.Id, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable($"{slice.Name} ({slice.Width}x{slice.Height})##{slice.Id}", selected))
                _selectedSliceIds[fullPath] = slice.Id;
        }

        int selectedIndex = settings.Slices.FindIndex(slice => string.Equals(slice.Id, GetSelectedSliceId(fullPath, settings), StringComparison.OrdinalIgnoreCase));
        if (selectedIndex < 0 && settings.Slices.Count > 0)
            selectedIndex = 0;

        if (selectedIndex >= 0 && selectedIndex < settings.Slices.Count)
        {
            EditSlice(L10n.Tr("label_selected_slice"), settings.Slices[selectedIndex], settings, textureWidth, textureHeight, updated =>
            {
                settings.Slices[selectedIndex] = updated;
                _selectedSliceIds[fullPath] = updated.Id;
                changed = true;
            });

            if (ImGui.Button(L10n.Tr("btn_delete_slice"), new Vector2(-1, 0)))
            {
                settings.Slices.RemoveAt(selectedIndex);
                if (settings.Slices.Count == 0)
                    settings.Slices.Add(SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, settings.DefaultPivot));
                _selectedSliceIds[fullPath] = settings.Slices[0].Id;
                changed = true;
            }
        }

        return changed;
    }

    private void EditSlice(string label, SpriteSlice slice, SpriteImportSettings settings, int textureWidth, int textureHeight, Action<SpriteSlice> onUpdate)
    {
        var working = slice.Clone();
        working.EnsureId();
        ImGui.Separator();
        ImGui.Text(label);

        string name = working.Name;
        ImGui.Text("Name");
        ImGui.SameLine(120);
        if (ImGui.InputText($"##name_{working.Id}", ref name, 128))
        {
            working.Name = name;
            onUpdate(working);
        }

        int x = working.X;
        ImGui.Text("X");
        ImGui.SameLine(120);
        if (ImGui.DragInt($"##x_{working.Id}", ref x, 1f, 0, Math.Max(0, textureWidth - 1)))
        {
            working.X = x;
            onUpdate(working);
        }

        int y = working.Y;
        ImGui.Text("Y");
        ImGui.SameLine(120);
        if (ImGui.DragInt($"##y_{working.Id}", ref y, 1f, 0, Math.Max(0, textureHeight - 1)))
        {
            working.Y = y;
            onUpdate(working);
        }

        int width = working.Width;
        ImGui.Text("Width");
        ImGui.SameLine(120);
        if (ImGui.DragInt($"##width_{working.Id}", ref width, 1f, 1, textureWidth))
        {
            working.Width = width;
            onUpdate(working);
        }

        int height = working.Height;
        ImGui.Text("Height");
        ImGui.SameLine(120);
        if (ImGui.DragInt($"##height_{working.Id}", ref height, 1f, 1, textureHeight))
        {
            working.Height = height;
            onUpdate(working);
        }

        Vector2 pivot = working.Pivot;
        ImGui.Text("Pivot");
        ImGui.SameLine(120);
        if (ImGui.DragFloat2($"##pivot_{working.Id}", (float*)&pivot, 0.01f, 0f, 1f))
        {
            working.Pivot = SpriteImportUtility.ClampPivot(pivot);
            onUpdate(working);
        }
    }

    private List<SpriteSlice> CreateGridSlices(int textureWidth, int textureHeight, Vector2 pivot)
    {
        int cellWidth = Math.Max(1, _sliceGridCellWidth);
        int cellHeight = Math.Max(1, _sliceGridCellHeight);
        int offsetX = Math.Max(0, _sliceGridOffsetX);
        int offsetY = Math.Max(0, _sliceGridOffsetY);
        int paddingX = Math.Max(0, _sliceGridPaddingX);
        int paddingY = Math.Max(0, _sliceGridPaddingY);

        var slices = new List<SpriteSlice>();
        int row = 0;
        for (int y = offsetY; y + cellHeight <= textureHeight; y += cellHeight + paddingY)
        {
            int col = 0;
            for (int x = offsetX; x + cellWidth <= textureWidth; x += cellWidth + paddingX)
            {
                slices.Add(new SpriteSlice
                {
                    Name = L10n.Tr("sprite_grid_name_format", row, col),
                    X = x,
                    Y = y,
                    Width = cellWidth,
                    Height = cellHeight,
                    Pivot = pivot
                });
                col++;
            }
            row++;
        }

        if (slices.Count == 0)
            slices.Add(SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, pivot));

        return slices;
    }

    private string GetSelectedSliceId(string fullPath, SpriteImportSettings settings)
    {
        if (_selectedSliceIds.TryGetValue(fullPath, out var selectedId) &&
            settings.Slices.Any(slice => string.Equals(slice.Id, selectedId, StringComparison.OrdinalIgnoreCase)))
            return selectedId;

        string fallback = settings.Slices.FirstOrDefault()?.Id ?? string.Empty;
        _selectedSliceIds[fullPath] = fallback;
        return fallback;
    }

    private SpriteSlice GetSelectedSlice(string fullPath, SpriteImportSettings settings, int textureWidth, int textureHeight)
    {
        string selectedId = GetSelectedSliceId(fullPath, settings);
        return settings.Slices.FirstOrDefault(slice => string.Equals(slice.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, settings.DefaultPivot);
    }

    private void DrawAudioFileInspector(string path)
    {
        ImGui.Text($"{L10n.Tr("label_type")}: {Path.GetExtension(path)}");
        ImGui.Text($"{L10n.Tr("label_path")}: {path}");
        ImGui.Text(L10n.Tr("label_guessed_type", AudioClip.GuessType(path)));
        if (ImGui.Button(L10n.Tr("btn_preview"), new Vector2(-1, 28)))
        {
            using var clip = AudioClip.FromPath(path);
            clip.Preview();
        }
    }

    private void DrawComponent(Component component, Entity entity) {
        ImGui.PushID(component.GetHashCode());
        string typeName = component.GetType().Name;
        string localizedTypeName = LocalizeTypeName(typeName);
        if (ImGui.CollapsingHeader(localizedTypeName, ImGuiTreeNodeFlags.DefaultOpen)) {
            if (ImGui.BeginPopupContextItem()) { if (component is not Transform && ImGui.MenuItem(L10n.Tr("ctx_remove"))) { _app.BeginUndoAction(); entity.RemoveComponent(component); _app.EndUndoAction(); } ImGui.EndPopup(); }
            ImGui.Indent();

            bool enabled = component.Enabled;
            if (!component.CanBeDisabled) ImGui.BeginDisabled();
            if (ImGui.Checkbox($"{L10n.Tr("field_Enabled")}##ComponentEnabled", ref enabled) && component.CanBeDisabled) { _app.RecordUndo(); component.Enabled = enabled; }
            if (!component.CanBeDisabled) ImGui.EndDisabled();

            if (component is PolygonShape || component is PolygonRenderer) { 
                bool isEdit = EditorSelection.EditingPolygonComponent == component; 
                if (isEdit) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1.0f, 1.0f)); 
                string btnLabel = isEdit ? L10n.Tr("btn_exit_edit") : (component is PolygonShape ? L10n.Tr("btn_edit_polygon_shape") : L10n.Tr("btn_edit_polygon_renderer"));
                if (ImGui.Button(btnLabel, new Vector2(-1, 25))) { _app.RecordUndo(); EditorSelection.EditingPolygonComponent = isEdit ? null : component; }
                if (isEdit) ImGui.PopStyleColor(); 
            }

            if (component is AudioManager audioManager) DrawAudioManagerInspector(audioManager);
            else if (component is UiDocument uiDocument) DrawUiDocumentInspector(uiDocument);
            else if (component is Tilemap tilemap) DrawTilemapInspector(tilemap);
            else if (component is LuaScriptComponent luaScriptComponent) DrawLuaScriptComponentInspector(luaScriptComponent);
            else DrawGenericInspector(component); 
            
            ImGui.Unindent();
        }
        ImGui.PopID();
    }

    private void DrawAudioManagerInspector(AudioManager manager)
    {
        manager.EnsureDefaultGroups();

        float masterVolume = manager.MasterVolume;
        ImGui.Text(L10n.Tr("field_MasterVolume"));
        ImGui.SameLine(120);
        if (ImGui.DragFloat("##v_master_volume", ref masterVolume, 0.01f, 0f, 1f))
        {
            manager.MasterVolume = masterVolume;
        }

        ImGui.Separator();
        ImGui.Text(L10n.Tr("label_groups_count", manager.Groups.Count));

        for (int i = 0; i < manager.Groups.Count; i++)
        {
            var group = manager.Groups[i];
            ImGui.PushID($"AudioGroup_{i}");
            if (ImGui.TreeNodeEx(group.Name, ImGuiTreeNodeFlags.DefaultOpen))
            {
                string name = group.Name;
                ImGui.Text(L10n.Tr("label_name"));
                ImGui.SameLine(120);
            if (ImGui.InputText($"##name_{i}", ref name, 64))
                    group.Name = name;

                float volume = group.Volume;
                ImGui.Text(L10n.Tr("field_Volume"));
                ImGui.SameLine(120);
            if (ImGui.DragFloat($"##volume_{i}", ref volume, 0.01f, 0f, 1f))
                    group.Volume = volume;

                float pitch = group.Pitch;
                ImGui.Text(L10n.Tr("field_Pitch"));
                ImGui.SameLine(120);
            if (ImGui.DragFloat($"##pitch_{i}", ref pitch, 0.01f, 0.1f, 4f))
                    group.Pitch = pitch;

                bool muted = group.IsMuted;
                ImGui.Text(L10n.Tr("field_Muted"));
                ImGui.SameLine(120);
            if (ImGui.Checkbox($"##muted_{i}", ref muted))
                    group.IsMuted = muted;

                int maxVoices = group.MaxVoices;
                ImGui.Text(L10n.Tr("field_MaxVoices"));
                ImGui.SameLine(120);
            if (ImGui.DragInt($"##max_voices_{i}", ref maxVoices, 1, 1, 256))
                    group.MaxVoices = maxVoices;

                bool protectedGroup = group.Name is "Master" or "BGM" or "SFX" or "UI";
            if (!protectedGroup && ImGui.Button(L10n.Tr("btn_remove_group"), new Vector2(-1, 24)))
                {
                    manager.Groups.RemoveAt(i);
                    manager.SyncGroupMap();
                    ImGui.TreePop();
                    ImGui.PopID();
                    return;
                }

                ImGui.TreePop();
            }
            ImGui.PopID();
        }

        if (ImGui.Button(L10n.Tr("btn_add_audio_group"), new Vector2(-1, 26)))
        {
            manager.Groups.Add(new AudioGroup($"Group_{manager.Groups.Count}", 16));
        }

        manager.SyncGroupMap();
    }

    private void DrawUiDocumentInspector(UiDocument document)
    {
        var screenPathMember = (MemberInfo?)document.GetType().GetProperty(nameof(UiDocument.ScreenPath)) ?? document.GetType().GetField(nameof(UiDocument.ScreenPath));
        DrawAssetReferenceField(L10n.Tr("ui_field_screen"), document.ScreenPath, ".ui", newVal =>
        {
            string path = (string?)newVal ?? string.Empty;
            if (screenPathMember != null)
                UpdateSiblingAssetGuid(document, screenPathMember, path);
            document.ScreenPath = path;
            if (_app.IsPlaying)
                document.Reload();
        });

        string bindingNamespace = document.BindingNamespace;
        ImGui.Text(L10n.Tr("ui_field_binding_namespace"));
        ImGui.SameLine(120);
        if (ImGui.InputText("##v_binding_namespace", ref bindingNamespace, 128))
            document.BindingNamespace = bindingNamespace;

        bool autoShow = document.AutoShow;
        ImGui.Text(L10n.Tr("ui_field_auto_show"));
        ImGui.SameLine(120);
        if (ImGui.Checkbox("##v_auto_show", ref autoShow))
            document.AutoShow = autoShow;

        bool visible = document.Visible;
        ImGui.Text(L10n.Tr("label_visible"));
        ImGui.SameLine(120);
        if (ImGui.Checkbox("##v_visible", ref visible))
        {
            document.Visible = visible;
            if (document.Canvas != null)
                document.Canvas.Visible = visible;
        }

        bool bindOwnerEntity = document.BindOwnerEntity;
        ImGui.Text(L10n.Tr("ui_field_bind_owner_entity"));
        ImGui.SameLine(120);
        if (ImGui.Checkbox("##v_bind_owner_entity", ref bindOwnerEntity))
            document.BindOwnerEntity = bindOwnerEntity;

        bool bindOwnerComponents = document.BindOwnerComponents;
        ImGui.Text(L10n.Tr("ui_field_bind_owner_components"));
        ImGui.SameLine(120);
        if (ImGui.Checkbox("##v_bind_owner_components", ref bindOwnerComponents))
            document.BindOwnerComponents = bindOwnerComponents;

        if (ImGui.Button(L10n.Tr("ui_btn_reload_ui"), new Vector2(-1, 0)))
            document.Reload();

        if (document.Canvas != null)
        {
            ImGui.TextDisabled($"{L10n.Tr("ui_label_canvas")}: {document.Canvas.Screen.Name}");
            ImGui.TextDisabled($"{L10n.Tr("ui_label_bindings")}: {document.BindingNamespace}");
        }
    }

    private void DrawTilemapInspector(Tilemap tilemap)
    {
        Vector2 tileSize = tilemap.TileSize;
        ImGui.Text(L10n.Tr("field_TileSize"));
        ImGui.SameLine(120);
        if (ImGui.DragFloat2("##v_tile_size", (float*)&tileSize, 0.05f)) { tilemap.TileSize = tileSize; }
        
        ImGui.Text($"{L10n.Tr("label_tiles")}: {tilemap.Tiles.Count}");
        var tilePalette = _app.GetWindow<TilePaletteWindow>();
        if (ImGui.Button(L10n.Tr("btn_open_tile_palette"), new Vector2(-1, 0)) && tilePalette != null)
        {
            _app.OpenWindow(tilePalette);
        }
        if (ImGui.Button(L10n.Tr("btn_clear_tilemap"), new Vector2(-1, 0))) { _app.RecordUndo(); tilemap.Clear(); }
    }

    private bool ShouldShowMember(MemberInfo m) 
    {
        if (m.Name is "Tiles" or "RenderDirty" or "PhysicsDirty") return false;
        return HasAttribute(m, "SerializeFieldAttribute") || (m is FieldInfo f && f.IsPublic) || (m is PropertyInfo p && (p.GetGetMethod()?.IsPublic ?? false) && !HasAttribute(m, "HideInInspectorAttribute"));
    }

    private bool ShouldShowMember(MemberInfo m, object target)
    {
        if (!ShouldShowMember(m))
            return false;

        if (target is Light2D light)
            return ShouldShowLightMember(m.Name, light);

        if (target is CameraOutput cameraOutput)
            return ShouldShowCameraOutputMember(m.Name, cameraOutput);

        if (IsShadowCasterSettingsTarget(target))
            return ShouldShowShadowCasterMember(m.Name, target);

        return true;
    }

    private bool ShouldShowMember(MemberInfo m, IEnumerable<Component> targets)
    {
        if (!ShouldShowMember(m))
            return false;

        if (targets.All(target => target is Light2D))
            return targets.Cast<Light2D>().All(light => ShouldShowLightMember(m.Name, light));

        if (targets.All(target => target is CameraOutput))
            return targets.Cast<CameraOutput>().All(output => ShouldShowCameraOutputMember(m.Name, output));

        if (targets.All(IsShadowCasterSettingsTarget))
            return targets.All(target => ShouldShowShadowCasterMember(m.Name, target));

        return true;
    }

    private static bool ShouldShowCameraOutputMember(string memberName, CameraOutput output)
    {
        if (output.Target != CameraOutputTarget.Window && memberName == nameof(CameraOutput.WindowCloseQuitsApplication))
            return false;

        if (output.Target != CameraOutputTarget.MainWindow)
            return true;

        return memberName is not nameof(CameraOutput.WindowVisible)
            and not nameof(CameraOutput.WindowGroup)
            and not nameof(CameraOutput.WindowPosition)
            and not nameof(CameraOutput.WindowSize);
    }

    private static MemberInfo[] GetGenericInspectorMembers(Type type)
    {
        return GenericInspectorMembersCache.GetOrAdd(type, static currentType =>
        {
            var hierarchy = new List<Type>();
            var cursor = currentType;
            while (cursor != null && cursor.FullName != "Verity.Core.ECS.Component" && cursor != typeof(object))
            {
                hierarchy.Add(cursor);
                cursor = cursor.BaseType;
            }

            hierarchy.Reverse();
            return currentType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(static member => member.DeclaringType != null &&
                    member.DeclaringType.FullName != "System.Object" &&
                    member.DeclaringType.FullName != "Verity.Core.ECS.Component")
                .OrderBy(member => hierarchy.IndexOf(member.DeclaringType!))
                .ThenBy(member => member.MetadataToken)
                .ToArray();
        });
    }

    private static MemberInfo[] GetMultiInspectorMembers(Type type)
    {
        return MultiInspectorMembersCache.GetOrAdd(type, static currentType =>
            currentType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(static member => member.DeclaringType != null &&
                    member.DeclaringType.FullName != "System.Object" &&
                    member.DeclaringType.FullName != "Verity.Core.ECS.Component")
                .OrderBy(member => member.MetadataToken)
                .ToArray());
    }

    private static MemberInfo[] GetNestedInspectorMembers(Type type)
    {
        return NestedInspectorMembersCache.GetOrAdd(type, static currentType =>
            currentType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
                .Where(static member => member.DeclaringType != typeof(object))
                .OrderBy(member => member.MetadataToken)
                .ToArray());
    }

    private static ButtonMetadata? GetButtonMetadata(MethodInfo method)
    {
        return ButtonMetadataCache.GetOrAdd(method, static currentMethod =>
        {
            if (currentMethod.GetParameters().Length != 0)
                return null;

            ButtonAttribute? attribute = currentMethod.GetCustomAttribute<ButtonAttribute>();
            if (attribute == null)
                return null;

            return new ButtonMetadata
            {
                Label = attribute.Label ?? currentMethod.Name,
                Undoable = attribute.Undoable
            };
        });
    }

    private static AssetReferenceAttribute? GetAssetReferenceAttribute(MemberInfo member)
    {
        return AssetReferenceAttributeCache.GetOrAdd(member, static currentMember => currentMember.GetCustomAttribute<AssetReferenceAttribute>());
    }

    private void ProcessMember(string name, Type type, object? value, Action<object?> onUpdate, MemberInfo member, object target) {
        if (target is Light2D light && member.Name == nameof(Light2D.ShadowReceiverMask))
        {
            if (light.ShadowLayerSource == Light2DMaskSource.PhysicsGroup)
                DrawPhysicsGroupMaskDropdown(name, (ulong?)value ?? 0UL, onUpdate);
            else
                DrawSortingLayerMaskDropdown(name, (ulong?)value ?? 0UL, onUpdate);
            return;
        }

        if (type == typeof(ulong) && HasAttribute(member, "PhysicsGroupMaskSelectorAttribute")) {
            DrawPhysicsGroupMaskDropdown(name, (ulong?)value ?? 0UL, onUpdate);
            return;
        }

        if (type == typeof(ulong) && HasAttribute(member, "SortingLayerMaskSelectorAttribute")) {
            DrawSortingLayerMaskDropdown(name, (ulong?)value ?? 0UL, onUpdate);
            return;
        }

        if (target is Light2D lightTarget && type == typeof(FilterType))
        {
            Type? requiredType = GetLightFilterType(member.Name, lightTarget);
            if (requiredType != null)
            {
                DrawFilterField(name, value as FilterType, onUpdate, false, false, requiredType);
                return;
            }
        }

        if (target is CameraOutput cameraOutput && member.Name == nameof(CameraOutput.Target) && type == typeof(CameraOutputTarget))
        {
            DrawCameraOutputTargetField(name, cameraOutput, onUpdate);
            return;
        }

        if (type == typeof(string)) {
            if (HasAttribute(member, "PhysicsGroupSelectorAttribute")) { DrawPhysicsGroupDropdown(name, (string?)value ?? "", onUpdate); return; }
            if (HasAttribute(member, "SortingLayerSelectorAttribute")) { DrawSortingLayerDropdown(name, (string?)value ?? "", onUpdate); return; }
            if (HasAttribute(member, "TagSelectorAttribute")) { DrawTagDropdown(name, (string?)value ?? "", onUpdate); return; }
        }
        
        Action<object?> wrappedUpdate = val => {
            onUpdate(val);
            // Animation Recording Hook
            var animWindow = _app.GetWindow<AnimationWindow>();
            if (animWindow != null && animWindow.IsRecording && target is Component comp && comp.Owner == EditorSelection.SelectedEntity) {
                 animWindow.RecordKeyframe(comp.Owner, comp.GetType(), member.Name, val!);
            }
        };

        if (member.Name == "Scale" && target is Transform t) { DrawTransformScaleField(t); return; }
        AssetReferenceAttribute? assetReferenceAttribute = type == typeof(string) ? GetAssetReferenceAttribute(member) : null;
        if (assetReferenceAttribute != null) {
            DrawAssetReferenceField(name, (string?)value ?? "", assetReferenceAttribute.Extension, newVal => {
                string path = (string?)newVal ?? string.Empty;
                UpdateSiblingAssetGuid(target, member, path);
                wrappedUpdate(path);
            });
            return;
        }
        DrawValueEditor(name, type, value, wrappedUpdate, target);
    }

    private void DrawCameraOutputTargetField(string name, CameraOutput output, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        ImGui.Text(name);
        ImGui.SameLine(120);

        CameraOutputTarget[] options = [CameraOutputTarget.MainWindow, CameraOutputTarget.Window, CameraOutputTarget.RenderTexture];

        string preview = GetEnumDisplayName(typeof(CameraOutputTarget), output.Target.ToString());
        if (ImGui.BeginCombo("##v", preview))
        {
            foreach (var option in options)
            {
                bool selected = output.Target == option;
                string label = GetEnumDisplayName(typeof(CameraOutputTarget), option.ToString());
                if (ImGui.Selectable(label, selected))
                    onUpdate(option);

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        ImGui.PopID();
    }

    private bool HasAttribute(MemberInfo member, string attributeName)
    {
        HashSet<string> attributes = MemberAttributeCache.GetOrAdd(member, static currentMember =>
            currentMember.GetCustomAttributes(true)
                .Select(attribute => attribute.GetType().Name)
                .ToHashSet(StringComparer.Ordinal));
        return attributes.Contains(attributeName);
    }

    private void DrawPostProcessSettings(string name, PostProcessSettings? settings, Action<object?> onUpdate)
    {
        settings ??= new PostProcessSettings();
        settings.GetCustomEffects();

        ImGui.PushID(name);
        if (ImGui.TreeNodeEx(name, ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool enabled = settings.Enabled;
            if (ImGui.Checkbox(L10n.Tr("field_Enabled"), ref enabled))
            {
                settings.Enabled = enabled;
                onUpdate(settings);
            }

            string[] missingEffects = GetMissingPostProcessEffects(settings);
            if (missingEffects.Length > 0)
            {
                string addEffectLabel = L10n.Tr("postprocess_add_effect");
                if (ImGui.BeginCombo(addEffectLabel, addEffectLabel))
                {
                    foreach (string effectKey in missingEffects)
                    {
                        if (ImGui.Selectable(GetPostProcessEffectLabel(effectKey)))
                        {
                            _app.RecordUndo();
                            AddPostProcessEffect(settings, effectKey);
                            onUpdate(settings);
                            SaveWorldAfterStructuralInspectorChange();
                        }
                    }
                    ImGui.EndCombo();
                }
            }

            string[] addedEffects = GetAddedPostProcessEffects(settings);
            if (addedEffects.Length == 0)
            {
                ImGui.TextDisabled(L10n.Tr("postprocess_no_effects"));
            }
            else
            {
                foreach (string effectKey in addedEffects)
                {
                    object? effect = GetPostProcessEffect(settings, effectKey);
                    if (effect == null)
                        continue;

                    ImGui.PushID(effectKey);

                    bool open = false;
                    bool removed = false;
                    if (ImGui.BeginTable("##postprocess_effect_row", 2, ImGuiTableFlags.SizingStretchProp))
                    {
                        ImGui.TableSetupColumn(L10n.Tr("header_effect"), ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn(L10n.Tr("header_actions"), ImGuiTableColumnFlags.WidthFixed, 72f);
                        ImGui.TableNextRow();

                        ImGui.TableNextColumn();
                        open = ImGui.CollapsingHeader(GetPostProcessEffectLabel(effectKey), ImGuiTreeNodeFlags.DefaultOpen);

                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton(L10n.Tr("ctx_remove")))
                        {
                            _app.RecordUndo();
                            RemovePostProcessEffect(settings, effectKey);
                            onUpdate(settings);
                            SaveWorldAfterStructuralInspectorChange();
                            removed = true;
                        }

                        ImGui.EndTable();
                    }

                    if (removed)
                    {
                        ImGui.PopID();
                        continue;
                    }

                    if (open)
                        DrawNestedObject(L10n.Tr("label_settings"), effect, () => onUpdate(settings));

                    ImGui.PopID();
                }
            }

            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private void SaveWorldAfterStructuralInspectorChange()
    {
        if (WorldManager.ActiveWorld == null)
            return;

        _app.GetWindow<ProjectWindow>()?.SaveActiveWorldAsAsset();
    }

    private static string LocalizeTypeName(Type type) => LocalizeTypeName(type.Name);

    private static string LocalizeTypeName(string typeName)
    {
        string localized = L10n.Tr($"type_{typeName}");
        return localized == $"type_{typeName}" ? typeName : localized;
    }

    private static string LocalizeFieldName(string fieldName)
    {
        string localized = L10n.Tr($"field_{fieldName}");
        return localized == $"field_{fieldName}" ? fieldName : localized;
    }

    private static string[] GetMissingPostProcessEffects(PostProcessSettings settings)
    {
        return GetPostProcessEffectKeys()
            .Where(key => key == "Custom" || GetPostProcessEffect(settings, key) == null)
            .ToArray();
    }

    private static string[] GetAddedPostProcessEffects(PostProcessSettings settings)
    {
        settings.GetCustomEffects();

        string[] fixedEffects = GetPostProcessEffectKeys()
            .Where(key => key != "Custom")
            .Where(key => GetPostProcessEffect(settings, key) != null)
            .OrderBy(key => GetPostProcessEffectOrder(settings, key))
            .ThenBy(key => key, StringComparer.Ordinal)
            .ToArray();

        string[] customEffects = settings.Customs
            .Select((_, index) => GetCustomEffectKey(index))
            .OrderBy(key => GetPostProcessEffectOrder(settings, key))
            .ThenBy(key => key, StringComparer.Ordinal)
            .ToArray();

        return fixedEffects.Concat(customEffects).ToArray();
    }

    private static string[] GetPostProcessEffectKeys()
    {
        return ["Bloom", "Vignette", "ColorAdjustments", "MotionBlur", "Distortion", "ChromaticAberration", "Pixelate", "Custom"];
    }

    private static string GetPostProcessEffectLabel(string key)
    {
        if (TryParseCustomEffectIndex(key, out int customIndex))
            return string.Format(L10n.Tr("postprocess_effect_CustomIndexed"), customIndex + 1);

        string label = L10n.Tr($"postprocess_effect_{key}");
        return label == $"postprocess_effect_{key}" ? key : label;
    }

    private static object? GetPostProcessEffect(PostProcessSettings settings, string key) => key switch
    {
        "Bloom" => settings.Bloom,
        "Vignette" => settings.Vignette,
        "ColorAdjustments" => settings.ColorAdjustments,
        "MotionBlur" => settings.MotionBlur,
        "Distortion" => settings.Distortion,
        "ChromaticAberration" => settings.ChromaticAberration,
        "Pixelate" => settings.Pixelate,
        "Custom" => null,
        _ => null
    };

    private static int GetPostProcessEffectOrder(PostProcessSettings settings, string key)
    {
        if (TryParseCustomEffectIndex(key, out int customIndex))
        {
            List<CustomPostProcessSettings> customs = settings.GetCustomEffects();
            return customIndex >= 0 && customIndex < customs.Count ? customs[customIndex].Order : int.MaxValue;
        }

        return key switch
        {
            "Bloom" => settings.Bloom is BloomSettings bloom ? bloom.Order : int.MaxValue,
            "Vignette" => settings.Vignette is VignetteSettings vignette ? vignette.Order : int.MaxValue,
            "ColorAdjustments" => settings.ColorAdjustments is ColorAdjustmentsSettings colorAdjustments ? colorAdjustments.Order : int.MaxValue,
            "MotionBlur" => settings.MotionBlur is MotionBlurSettings motionBlur ? motionBlur.Order : int.MaxValue,
            "Distortion" => settings.Distortion is DistortionSettings distortion ? distortion.Order : int.MaxValue,
            "ChromaticAberration" => settings.ChromaticAberration is ChromaticAberrationSettings chromaticAberration ? chromaticAberration.Order : int.MaxValue,
            "Pixelate" => settings.Pixelate is PixelateSettings pixelate ? pixelate.Order : int.MaxValue,
            _ => int.MaxValue
        };
    }

    private static void AddPostProcessEffect(PostProcessSettings settings, string key)
    {
        switch (key)
        {
            case "Bloom": settings.Bloom ??= new BloomSettings(); break;
            case "Vignette": settings.Vignette ??= new VignetteSettings(); break;
            case "ColorAdjustments": settings.ColorAdjustments ??= new ColorAdjustmentsSettings(); break;
            case "MotionBlur": settings.MotionBlur ??= new MotionBlurSettings(); break;
            case "Distortion": settings.Distortion ??= new DistortionSettings(); break;
            case "ChromaticAberration": settings.ChromaticAberration ??= new ChromaticAberrationSettings(); break;
            case "Pixelate": settings.Pixelate ??= new PixelateSettings(); break;
            case "Custom": settings.GetCustomEffects().Add(new CustomPostProcessSettings()); break;
        }
    }

    private static void RemovePostProcessEffect(PostProcessSettings settings, string key)
    {
        if (TryParseCustomEffectIndex(key, out int customIndex))
        {
            List<CustomPostProcessSettings> customs = settings.GetCustomEffects();
            if (customIndex >= 0 && customIndex < customs.Count)
                customs.RemoveAt(customIndex);
            return;
        }

        switch (key)
        {
            case "Bloom": settings.Bloom = null; break;
            case "Vignette": settings.Vignette = null; break;
            case "ColorAdjustments": settings.ColorAdjustments = null; break;
            case "MotionBlur": settings.MotionBlur = null; break;
            case "Distortion": settings.Distortion = null; break;
            case "ChromaticAberration": settings.ChromaticAberration = null; break;
            case "Pixelate": settings.Pixelate = null; break;
        }
    }

    private static string GetCustomEffectKey(int index) => $"Custom:{index}";

    private static bool TryParseCustomEffectIndex(string key, out int index)
    {
        const string prefix = "Custom:";
        if (key.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(key[prefix.Length..], out index))
            return true;

        index = -1;
        return false;
    }

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

    private void DrawPhysicsGroupMaskDropdown(string label, ulong current, Action<object?> onUpdate, bool noLabel = false, bool mixed = false) {
        if (!noLabel) { ImGui.PushID(label.GetHashCode()); ImGui.Text(label); ImGui.SameLine(120); }
        bool openPopup = false;
        var groups = _app.ProjectSettings.PhysicsGroups;
        string preview = mixed ? L10n.Tr("msg_mixed") : GetPhysicsGroupMaskLabel(current, groups);

        if (ImGui.BeginCombo("##GroupMask", preview)) {
            ulong fullMask = BuildPhysicsGroupMask(groups);
            if (ImGui.Selectable(L10n.Tr("label_all"), current == ulong.MaxValue))
                onUpdate(ulong.MaxValue);
            if (ImGui.Selectable(L10n.Tr("msg_none"), current == 0))
                onUpdate(0UL);

            ImGui.Separator();
            ulong editableMask = current == ulong.MaxValue ? fullMask : current;
            foreach (var group in groups) {
                ulong bit = FilterRegistry.GetGroupMask(group);
                bool selected = (editableMask & bit) != 0;
                if (ImGui.Checkbox(group, ref selected)) {
                    ulong next = current == ulong.MaxValue ? fullMask : current;
                    if (selected) next |= bit;
                    else next &= ~bit;
                    onUpdate(next == fullMask ? ulong.MaxValue : next);
                }
            }

            ImGui.Separator();
            if (ImGui.Selectable(L10n.Tr("ctx_add_group"))) { _newGroupNameBuffer = ""; openPopup = true; }
            ImGui.EndCombo();
        }

        if (openPopup) ImGui.OpenPopup("AddPhysicsGroupMaskPopup_Local");
        if (ImGui.BeginPopup("AddPhysicsGroupMaskPopup_Local")) {
            ImGui.Text(L10n.Tr("msg_new_group_name"));
            if (ImGui.InputText("##newgroupmask", ref _newGroupNameBuffer, 32, ImGuiInputTextFlags.EnterReturnsTrue)) {
                if (!string.IsNullOrWhiteSpace(_newGroupNameBuffer) && !groups.Contains(_newGroupNameBuffer)) _app.ProjectSettings.PhysicsGroups.Add(_newGroupNameBuffer);
                _app.SaveProjectSettings();
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button(L10n.Tr("btn_add")) && !string.IsNullOrWhiteSpace(_newGroupNameBuffer)) {
                if (!groups.Contains(_newGroupNameBuffer)) _app.ProjectSettings.PhysicsGroups.Add(_newGroupNameBuffer);
                _app.SaveProjectSettings();
                ImGui.CloseCurrentPopup();
            }
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

    private static ulong BuildPhysicsGroupMask(IEnumerable<string> groups)
    {
        ulong mask = 0;
        foreach (var group in groups)
            mask |= FilterRegistry.GetGroupMask(group);
        return mask;
    }

    private static ulong BuildSortingLayerMask(IEnumerable<string> layers)
    {
        ulong mask = 0;
        foreach (var layer in layers)
            mask |= FilterRegistry.GetMask("SortingLayer", layer);
        return mask;
    }

    private string GetPhysicsGroupMaskLabel(ulong mask, List<string> groups)
    {
        if (mask == ulong.MaxValue)
            return L10n.Tr("label_all");
        if (mask == 0)
            return L10n.Tr("msg_none");

        var selected = groups.Where(group => (mask & FilterRegistry.GetGroupMask(group)) != 0).ToList();
        if (selected.Count == 0)
            return L10n.Tr("msg_none");
        if (selected.Count == groups.Count)
            return L10n.Tr("label_all");
        if (selected.Count == 1)
            return selected[0];
        return $"{selected[0]} +{selected.Count - 1}";
    }

    private void DrawSortingLayerMaskDropdown(string label, ulong current, Action<object?> onUpdate, bool noLabel = false, bool mixed = false) {
        if (!noLabel) { ImGui.PushID(label.GetHashCode()); ImGui.Text(label); ImGui.SameLine(120); }
        bool openPopup = false;
        var layers = _app.ProjectSettings.SortingLayers;
        string preview = mixed ? L10n.Tr("msg_mixed") : GetSortingLayerMaskLabel(current, layers);

        if (ImGui.BeginCombo("##SortingLayerMask", preview)) {
            ulong fullMask = BuildSortingLayerMask(layers);
            if (ImGui.Selectable(L10n.Tr("label_all"), current == ulong.MaxValue))
                onUpdate(ulong.MaxValue);
            if (ImGui.Selectable(L10n.Tr("msg_none"), current == 0))
                onUpdate(0UL);

            ImGui.Separator();
            foreach (var layer in layers) {
                ulong bit = FilterRegistry.GetMask("SortingLayer", layer);
                ulong editableMask = current == ulong.MaxValue ? fullMask : current;
                bool selected = (editableMask & bit) != 0;
                if (ImGui.Checkbox(layer, ref selected)) {
                    ulong next = current == ulong.MaxValue ? fullMask : current;
                    if (selected) next |= bit;
                    else next &= ~bit;
                    onUpdate(next == fullMask ? ulong.MaxValue : next);
                }
            }

            ImGui.Separator();
            if (ImGui.Selectable(L10n.Tr("ctx_add_layer"))) { _newLayerNameBuffer = ""; openPopup = true; }
            ImGui.EndCombo();
        }

        if (openPopup) ImGui.OpenPopup("AddSortingLayerMaskPopup_Local");
        if (ImGui.BeginPopup("AddSortingLayerMaskPopup_Local")) {
            ImGui.Text(L10n.Tr("msg_new_layer_name"));
            if (ImGui.InputText("##newlayermask", ref _newLayerNameBuffer, 32, ImGuiInputTextFlags.EnterReturnsTrue)) {
                if (!string.IsNullOrWhiteSpace(_newLayerNameBuffer) && !layers.Contains(_newLayerNameBuffer)) _app.ProjectSettings.SortingLayers.Add(_newLayerNameBuffer);
                _app.SaveProjectSettings();
                ImGui.CloseCurrentPopup();
            }
            if (ImGui.Button(L10n.Tr("btn_add")) && !string.IsNullOrWhiteSpace(_newLayerNameBuffer)) {
                if (!layers.Contains(_newLayerNameBuffer)) _app.ProjectSettings.SortingLayers.Add(_newLayerNameBuffer);
                _app.SaveProjectSettings();
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (!noLabel) ImGui.PopID();
    }

    private string GetSortingLayerMaskLabel(ulong mask, List<string> layers)
    {
        if (mask == ulong.MaxValue)
            return L10n.Tr("label_all");
        if (mask == 0)
            return L10n.Tr("msg_none");

        var selected = layers.Where(layer => (mask & FilterRegistry.GetMask("SortingLayer", layer)) != 0).ToList();
        if (selected.Count == 0)
            return L10n.Tr("msg_none");
        if (selected.Count == layers.Count)
            return L10n.Tr("label_all");
        if (selected.Count == 1)
            return selected[0];
        return $"{selected[0]} +{selected.Count - 1}";
    }

    private static bool SupportsLightShadows(Light2D light)
        => light.Type is Light2DType.Direction or Light2DType.Spot;

    private static bool IsLightShadowMember(string memberName)
        => memberName is nameof(Light2D.CastShadows)
            or nameof(Light2D.ShadowStrength)
            or nameof(Light2D.ShadowLayerSource)
            or nameof(Light2D.ShadowReceiverSelectionMode)
            or nameof(Light2D.ShadowReceiverMask)
            or nameof(Light2D.ShadowReceiverFilter);

    private static bool ShouldShowLightMember(string memberName, Light2D light)
    {
        if (memberName == nameof(Light2D.Spread))
            return light.Type == Light2DType.Direction;

        if (memberName == nameof(Light2D.AffectsCameraBackground))
            return light.Type == Light2DType.World;

        if (memberName == nameof(Light2D.AffectedSortingLayerMask))
            return light.AffectedSortingLayerSelectionMode == Light2DSelectionMode.Direct;

        if (memberName == nameof(Light2D.AffectedSortingLayerFilter))
            return light.AffectedSortingLayerSelectionMode == Light2DSelectionMode.Filter;

        if (IsLightShadowMember(memberName) && !SupportsLightShadows(light))
            return false;

        if (memberName == nameof(Light2D.ShadowReceiverMask))
            return light.ShadowReceiverSelectionMode == Light2DSelectionMode.Direct;

        if (memberName == nameof(Light2D.ShadowReceiverFilter))
            return light.ShadowReceiverSelectionMode == Light2DSelectionMode.Filter;

        return true;
    }

    private static bool IsShadowCasterSettingsTarget(object target)
        => target is SpriteRenderer or PolygonRenderer or TilemapRenderer or Verity.Core.Physics.PhysicalShape;

    private static bool UsesRendererShadowSource(ShadowCasterSourceMode mode)
        => mode is ShadowCasterSourceMode.Renderer or ShadowCasterSourceMode.Both or ShadowCasterSourceMode.PreferRenderer;

    private static bool ShouldShowShadowCasterMember(string memberName, object target)
    {
        return target switch
        {
            SpriteRenderer spriteRenderer => memberName switch
            {
                nameof(SpriteRenderer.ShadowSourceMode) => spriteRenderer.CastShadows,
                nameof(SpriteRenderer.ShadowSelfMode) => spriteRenderer.CastShadows,
                nameof(SpriteRenderer.ShadowAlphaThreshold) => spriteRenderer.CastShadows && UsesRendererShadowSource(spriteRenderer.ShadowSourceMode),
                _ => true
            },
            PolygonRenderer polygonRenderer => memberName switch
            {
                nameof(PolygonRenderer.ShadowSourceMode) => polygonRenderer.CastShadows,
                nameof(PolygonRenderer.ShadowSelfMode) => polygonRenderer.CastShadows,
                _ => true
            },
            TilemapRenderer tilemapRenderer => memberName switch
            {
                nameof(TilemapRenderer.ShadowSourceMode) => tilemapRenderer.CastShadows,
                nameof(TilemapRenderer.ShadowSelfMode) => tilemapRenderer.CastShadows,
                _ => true
            },
            Verity.Core.Physics.PhysicalShape physicalShape => memberName switch
            {
                nameof(Verity.Core.Physics.PhysicalShape.ShadowSelfMode) => physicalShape.CastShadows,
                _ => true
            },
            _ => true
        };
    }

    private static Type? GetLightFilterType(string memberName, Light2D light)
    {
        if (memberName == nameof(Light2D.AffectedSortingLayerFilter))
            return typeof(Verity.Core.SortingLayer);

        if (memberName == nameof(Light2D.ShadowReceiverFilter))
            return light.ShadowLayerSource == Light2DMaskSource.PhysicsGroup
                ? typeof(Verity.Core.PhysicsGroup)
                : typeof(Verity.Core.SortingLayer);

        return null;
    }

    private static Type? GetLightFilterType(string memberName, List<Light2D> lights)
    {
        Type? requiredType = null;
        foreach (var light in lights)
        {
            Type? nextType = GetLightFilterType(memberName, light);
            if (nextType == null)
                return null;
            if (requiredType != null && requiredType != nextType)
                return null;
            requiredType = nextType;
        }

        return requiredType;
    }

    private void DrawNullableFloat(string name, float? value, Action<object?> onUpdate) {
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

    private void DrawFilterField(string name, FilterType? current, Action<object?> onUpdate, bool noLabel = false, bool mixed = false, Type? requiredType = null) {
        if (!noLabel) { ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120); }
        else ImGui.PushID($"{name}_filter");
        string preview = mixed ? L10n.Tr("msg_mixed") : (current?.Name ?? L10n.Tr("msg_none"));
        if (ImGui.Button($"{preview}##box", new Vector2(-25, 0))) { }
        ImGui.SameLine();
        if (ImGui.Button("o##picker", new Vector2(20, 0)))
            ImGui.OpenPopup("Picker");
        if (ImGui.BeginPopup("Picker")) {
            if (ImGui.MenuItem(L10n.Tr("msg_none")))
                onUpdate(null);
            ImGui.Separator();
            foreach (var f in FilterManager.GetAllFilters())
            {
                if (requiredType != null && !IsCompatibleSingleTypeFilter(f, requiredType))
                    continue;
                if (ImGui.MenuItem(f.Name))
                    onUpdate(f);
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private static bool IsCompatibleSingleTypeFilter(FilterType filter, Type requiredType)
    {
        if (!string.IsNullOrWhiteSpace(filter.EnumTypeName))
            return FilterManager.ResolveTypeInternal(filter.EnumTypeName) == requiredType;

        if (filter.MixedValues.Count == 0)
            return false;

        bool anyValue = false;
        foreach (var value in filter.MixedValues)
        {
            if (FilterManager.ResolveTypeInternal(value.TypeName) != requiredType)
                return false;
            anyValue = true;
        }

        return anyValue;
    }

    private void DrawValueEditor(string name, Type type, object? value, Action<object?> onUpdate, object? target = null)
    {
        if (type == typeof(PostProcessSettings)) { DrawPostProcessSettings(name, value as PostProcessSettings, onUpdate); return; }
        if (type == typeof(AudioClip)) { DrawAudioClipField(name, value as AudioClip, onUpdate, target); return; }
        if (type == typeof(Sprite)) { DrawSpriteField(name, (Sprite?)value ?? default, onUpdate); return; }
        if (type == typeof(TextureAsset)) { DrawTextureAssetField(name, value as TextureAsset ?? new TextureAsset(), onUpdate); return; }
        if (type == typeof(CameraTextureAsset)) { DrawCameraTextureAssetField(name, value as CameraTextureAsset ?? new CameraTextureAsset(), onUpdate); return; }
        if (type == typeof(StyleAsset)) { DrawStyleField(name, (StyleAsset?)value ?? default, onUpdate); return; }
        if (type == typeof(ShaderAsset)) { DrawShaderField(name, (ShaderAsset?)value ?? default, onUpdate); return; }
        if (type == typeof(FilterType)) { DrawFilterField(name, value as FilterType, onUpdate); return; }
        if (type == typeof(Entity)) { DrawEntityReferenceField(name, value as Entity, onUpdate); return; }
        if (typeof(Component).IsAssignableFrom(type)) { DrawComponentReferenceField(name, value as Component, type, onUpdate); return; }
        if (TryGetDictionaryTypes(type, out var keyType, out var valueType)) { DrawDictionary(name, value, type, keyType, valueType, onUpdate); return; }
        if (TryGetCollectionElementType(type, out var elementType)) { DrawCollection(name, value, type, elementType, onUpdate); return; }
        if (type == typeof(float?)) { DrawNullableFloat(name, value is float floatValue ? floatValue : (float?)value, onUpdate); return; }
        if (IsNestedInspectableType(type))
        {
            object? instance = value;
            if (instance == null && type.GetConstructor(Type.EmptyTypes) != null)
            {
                instance = Activator.CreateInstance(type);
                onUpdate(instance);
            }

            if (instance != null)
            {
                DrawNestedObject(name, instance, () => onUpdate(instance));
                return;
            }
        }

        object? actualValue = value ?? CreateDefaultValue(type);
        if (actualValue != null)
            DrawField(name, actualValue, onUpdate);
    }

    private void DrawField(string name, object? value, Action<object?> onUpdate) {
        if (value == null) return; ImGui.PushID(name); ImGui.Text(name); ImGui.SameLine(120);
        bool changed = false; Type t = value.GetType();
        if (t == typeof(float)) { float f = (float)value; if (ImGui.DragFloat("##v", ref f, 0.1f)) { changed = true; value = f; } }
        else if (t == typeof(int)) { int i = (int)value; if (ImGui.DragInt("##v", ref i)) { changed = true; value = i; } }
        else if (t == typeof(ulong)) { string s = value.ToString() ?? "0"; if (ImGui.InputText("##v", ref s, 32) && ulong.TryParse(s, out ulong parsed)) { changed = true; value = parsed; } }
        else if (t == typeof(bool)) { bool b = (bool)value; if (ImGui.Checkbox("##v", ref b)) { changed = true; value = b; } }
        else if (t == typeof(string)) { string s = (string)value; if (ImGui.InputText("##v", ref s, 1024)) { changed = true; value = s; } }
        else if (t == typeof(Vector2)) { Vector2 v2 = (Vector2)value; if (ImGui.DragFloat2("##v", (float*)&v2, 0.1f)) { changed = true; value = v2; } }
        else if (t == typeof(Vector3)) { Vector3 v3 = (Vector3)value; var raw = (System.Numerics.Vector3)v3; if (ImGui.DragFloat3("##v", ref raw, 0.1f)) { changed = true; value = (Vector3)raw; } }
        else if (t == typeof(Vector4)) { Vector4 v4 = (Vector4)value; if (ImGui.DragFloat4("##v", ref v4, 0.1f)) { changed = true; value = v4; } }
        else if (t == typeof(Color)) { var c = (Color)value; var v4 = (Vector4)c; if (ImGui.ColorEdit4("##v", ref v4)) { changed = true; value = (Color)v4; } }
        else if (value is Enum) { string[] names = Enum.GetNames(t); string[] displayNames = names.Select(name => GetEnumDisplayName(t, name)).ToArray(); int curr = Array.IndexOf(names, value.ToString()); if (ImGui.Combo("##v", ref curr, displayNames, displayNames.Length)) { changed = true; value = Enum.Parse(t, names[curr]); } }
        else { ImGui.TextDisabled(value.ToString() ?? L10n.Tr("msg_none")); }
        if (changed) onUpdate(value); ImGui.PopID();
    }

    private static string GetEnumDisplayName(Type enumType, string enumName)
    {
        string key = $"enum_{enumType.Name}_{enumName}";
        string localized = L10n.Tr(key);
        return localized == key ? enumName : localized;
    }

    private static string GetUiNodeKindLabel(UiNodeKind kind)
    {
        return kind switch
        {
            UiNodeKind.Container => L10n.Tr("ui_node_container"),
            UiNodeKind.Panel => L10n.Tr("ui_node_panel"),
            UiNodeKind.Label => L10n.Tr("ui_node_label"),
            UiNodeKind.RichText => L10n.Tr("ui_node_rich_text"),
            UiNodeKind.Image => L10n.Tr("ui_node_image"),
            UiNodeKind.Button => L10n.Tr("ui_node_button"),
            UiNodeKind.IconButton => L10n.Tr("ui_node_icon_button"),
            UiNodeKind.Toggle => L10n.Tr("ui_node_toggle"),
            UiNodeKind.ToggleGroup => L10n.Tr("ui_node_toggle_group"),
            UiNodeKind.Dropdown => L10n.Tr("ui_node_dropdown"),
            UiNodeKind.InputField => L10n.Tr("ui_node_input_field"),
            UiNodeKind.TextArea => L10n.Tr("ui_node_text_area"),
            UiNodeKind.Slider => L10n.Tr("ui_node_slider"),
            UiNodeKind.ProgressBar => L10n.Tr("ui_node_progress_bar"),
            UiNodeKind.Scrollbar => L10n.Tr("ui_node_scrollbar"),
            UiNodeKind.ScrollView => L10n.Tr("ui_node_scroll_view"),
            UiNodeKind.ListView => L10n.Tr("ui_node_list_view"),
            UiNodeKind.GridView => L10n.Tr("ui_node_grid_view"),
            UiNodeKind.Window => L10n.Tr("ui_node_window"),
            UiNodeKind.Modal => L10n.Tr("ui_node_modal"),
            UiNodeKind.Tabs => L10n.Tr("ui_node_tabs"),
            UiNodeKind.Tooltip => L10n.Tr("ui_node_tooltip"),
            UiNodeKind.Spacer => L10n.Tr("ui_node_spacer"),
            UiNodeKind.DynamicArea => L10n.Tr("ui_node_dynamic_area"),
            _ => kind.ToString()
        };
    }

    private void DrawNestedObject(string name, object value, Action onChanged)
    {
        ImGui.PushID(name);
        if (ImGui.TreeNodeEx(name, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            ImGui.Indent();
            var type = value.GetType();
            foreach (var member in GetNestedInspectorMembers(type))
            {
                string localizedName = L10n.Tr($"field_{member.Name}");
                if (localizedName == $"field_{member.Name}") localizedName = member.Name;

                if (member is FieldInfo field && ShouldShowMember(field))
                {
                    ProcessMember(localizedName, field.FieldType, field.GetValue(value), val => { field.SetValue(value, val); onChanged(); }, field, value);
                }
                else if (member is PropertyInfo property && property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && ShouldShowMember(property))
                {
                    ProcessMember(localizedName, property.PropertyType, property.GetValue(value), val => { property.SetValue(value, val); onChanged(); }, property, value);
                }
            }

            ImGui.Unindent();
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private static bool IsNestedInspectableType(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive || type.IsEnum)
            return false;
        if (type == typeof(float) || type == typeof(float?) || type == typeof(double) || type == typeof(int) || type == typeof(bool))
            return false;
        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4) || type == typeof(Color))
            return false;
        if (type == typeof(Sprite) || type == typeof(StyleAsset) || type == typeof(ShaderAsset) || type == typeof(AudioClip) || type == typeof(FilterType))
            return false;
        if (type == typeof(Entity))
            return false;
        if (typeof(Component).IsAssignableFrom(type))
            return false;
        if (typeof(IEnumerable).IsAssignableFrom(type) || typeof(IDictionary).IsAssignableFrom(type))
            return false;
        return type.IsClass || (type.IsValueType && !type.IsPrimitive);
    }

    private void DrawCollection(string label, object? collection, Type collectionType, Type elementType, Action<object?> onUpdate)
    {
        if (collection == null)
            return;

        var items = ExtractCollectionItems(collection).ToList();
        ImGui.PushID(label);
        uint collectionId = ImGui.GetID("##collection_reorder");
        if (!ImGui.TreeNodeEx($"{label} [{items.Count}]", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            ImGui.PopID();
            return;
        }

        bool changed = false;
        for (int i = 0; i < items.Count; i++)
        {
            int index = i;
            object? currentValue = items[i] ?? CreateDefaultValue(elementType);
            bool removed = false;
            ImGui.BeginGroup();
            DrawValueEditor($"[{i}]", elementType, currentValue, newValue => { items[index] = newValue; changed = true; });

            float controlsX = ImGui.GetCursorPosX() + MathF.Max(0f, ImGui.GetContentRegionAvail().X - 44f);
            ImGui.SetCursorPosX(controlsX);
            if (DrawCollectionReorderHandle(collectionId, index, items))
                changed = true;

            ImGui.SameLine(0f, 4f);
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.20f, 0.20f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.24f, 0.24f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.16f, 0.16f, 1.0f));
            if (ImGui.SmallButton($"X##remove_{i}"))
            {
                items.RemoveAt(i);
                changed = true;
                removed = true;
                i--;
            }
            ImGui.PopStyleColor(3);
            ImGui.NewLine();
            ImGui.EndGroup();

            if (removed)
                continue;

            if (DrawCollectionItemContextMenu(index, items, $"{label}_{collectionId}", () => changed = true, value => CloneCollectionItem(value, elementType)))
                break;

            if (i < items.Count - 1)
                ImGui.Separator();
        }

        if (ImGui.Button($"+ {L10n.Tr("btn_add")}##add_{label}", new Vector2(-1, 0)))
        {
            items.Add(CreateDefaultValue(elementType));
            changed = true;
        }

        if (changed)
            onUpdate(RebuildCollection(collectionType, elementType, items));

        ImGui.TreePop();
        ImGui.PopID();
    }

    private bool DrawCollectionReorderHandle(uint collectionId, int index, List<object?> items)
    {
        bool changed = false;
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.5f, 0.5f, 0.5f, 0.7f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.7f, 0.7f, 0.7f, 0.9f));
        if (ImGui.SmallButton($"≡##reorder_{index}"))
        {
        }
        ImGui.PopStyleColor(3);

        if (ImGui.BeginDragDropSource())
        {
            _draggedCollectionId = collectionId;
            _draggedCollectionIndex = index;
            ImGui.SetDragDropPayload("INSPECTOR_COLLECTION_ITEM", null, 0);
            ImGui.Text($"[{index}]");
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("INSPECTOR_COLLECTION_ITEM");
            if (payload.Handle != null &&
                _draggedCollectionId == collectionId &&
                _draggedCollectionIndex >= 0 &&
                _draggedCollectionIndex != index &&
                _draggedCollectionIndex < items.Count)
            {
                object? draggedItem = items[_draggedCollectionIndex];
                items.RemoveAt(_draggedCollectionIndex);
                int insertIndex = _draggedCollectionIndex < index ? index - 1 : index;
                items.Insert(insertIndex, draggedItem);
                _draggedCollectionIndex = insertIndex;
                changed = true;
            }

            ImGui.EndDragDropTarget();
        }

        return changed;
    }

    private static bool DrawCollectionItemContextMenu<T>(int index, List<T> items, string id, Action onUpdate, Func<T, T> cloneItem, bool allowReorder = true)
    {
        if (!ImGui.BeginPopupContextItem($"##ctx_{id}_{index}"))
            return false;

        bool handled = false;
        if (ImGui.MenuItem(L10n.Tr("ctx_remove")))
        {
            items.RemoveAt(index);
            onUpdate();
            handled = true;
        }
        else if (ImGui.MenuItem(L10n.Tr("ctx_duplicate")))
        {
            items.Insert(index + 1, cloneItem(items[index]));
            onUpdate();
            handled = true;
        }

        if (!handled)
        {
            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("ctx_move_up"), string.Empty, false, allowReorder && index > 0))
            {
                (items[index - 1], items[index]) = (items[index], items[index - 1]);
                onUpdate();
                handled = true;
            }
            else if (ImGui.MenuItem(L10n.Tr("ctx_move_down"), string.Empty, false, allowReorder && index < items.Count - 1))
            {
                (items[index], items[index + 1]) = (items[index + 1], items[index]);
                onUpdate();
                handled = true;
            }
        }

        ImGui.EndPopup();
        return handled;
    }

    private static T CloneCollectionItem<T>(T item)
    {
        object? source = item;
        if (source == null)
            return item;

        Type cloneType = source.GetType();
        string json = JsonSerializer.Serialize(source, cloneType);
        return JsonSerializer.Deserialize(json, cloneType) is T clone ? clone : item;
    }

    private static object? CloneCollectionItem(object? item, Type elementType)
    {
        if (item == null)
            return CreateDefaultValue(elementType);

        Type cloneType = item.GetType();
        string json = JsonSerializer.Serialize(item, cloneType);
        return JsonSerializer.Deserialize(json, cloneType);
    }

    private void DrawDictionary(string label, object? dictionary, Type dictionaryType, Type keyType, Type valueType, Action<object?> onUpdate)
    {
        if (dictionary is not IDictionary rawDictionary)
            return;

        var entries = rawDictionary.Cast<DictionaryEntry>().ToList();
        ImGui.PushID(label);
        if (!ImGui.TreeNodeEx($"{label} [{entries.Count}]", ImGuiTreeNodeFlags.SpanAvailWidth))
        {
            ImGui.PopID();
            return;
        }

        bool changed = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            object? currentValue = entry.Value ?? CreateDefaultValue(valueType);
            DrawValueEditor($"[{entry.Key}]", valueType, currentValue, newValue => { entries[i] = new DictionaryEntry(entry.Key, newValue); changed = true; });

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.20f, 0.20f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.70f, 0.24f, 0.24f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.45f, 0.16f, 0.16f, 1.0f));
            if (ImGui.Button($"{L10n.Tr("btn_delete")}##remove_dict_{i}", new Vector2(-1, 0)))
            {
                entries.RemoveAt(i);
                changed = true;
                i--;
            }
            ImGui.PopStyleColor(3);

            if (i < entries.Count - 1)
                ImGui.Separator();
        }

        if (CanCreateDictionaryKey(keyType) && ImGui.Button($"+ {L10n.Tr("btn_add")}##dict_{label}", new Vector2(-1, 0)))
        {
            entries.Add(new DictionaryEntry(CreateDictionaryKeyDefaultValue(keyType), CreateDefaultValue(valueType)));
            changed = true;
        }

        if (changed)
            onUpdate(RebuildDictionary(dictionaryType, keyType, valueType, entries));

        ImGui.TreePop();
        ImGui.PopID();
    }

    private static bool TryGetCollectionElementType(Type type, out Type elementType)
    {
        elementType = null!;
        if (type == typeof(string) || typeof(IDictionary).IsAssignableFrom(type))
            return false;

        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        if (!type.IsGenericType)
            return false;

        Type generic = type.GetGenericTypeDefinition();
        if (generic == typeof(List<>) || generic == typeof(IList<>) || generic == typeof(IEnumerable<>) ||
            generic == typeof(ICollection<>) || generic == typeof(HashSet<>) || generic == typeof(Queue<>) ||
            generic == typeof(Stack<>) || generic == typeof(LinkedList<>))
        {
            elementType = type.GetGenericArguments()[0];
            return true;
        }

        return false;
    }

    private static bool TryGetDictionaryTypes(Type type, out Type keyType, out Type valueType)
    {
        keyType = null!;
        valueType = null!;
        if (!type.IsGenericType)
            return false;

        Type generic = type.GetGenericTypeDefinition();
        if (generic == typeof(Dictionary<,>) || generic == typeof(IDictionary<,>))
        {
            var args = type.GetGenericArguments();
            keyType = args[0];
            valueType = args[1];
            return true;
        }

        return false;
    }

    private static IEnumerable<object?> ExtractCollectionItems(object collection)
    {
        if (collection is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
                yield return item;
        }
    }

    private static object? RebuildCollection(Type collectionType, Type elementType, List<object?> items)
    {
        if (collectionType.IsArray)
        {
            Array array = Array.CreateInstance(elementType, items.Count);
            for (int i = 0; i < items.Count; i++)
                array.SetValue(items[i], i);
            return array;
        }

        if (collectionType.IsGenericType)
        {
            Type generic = collectionType.GetGenericTypeDefinition();
            if (generic == typeof(Queue<>))
            {
                var queue = Activator.CreateInstance(typeof(Queue<>).MakeGenericType(elementType));
                MethodInfo? enqueue = queue?.GetType().GetMethod("Enqueue");
                foreach (var item in items) enqueue?.Invoke(queue, [item]);
                return queue;
            }

            if (generic == typeof(Stack<>))
            {
                var stack = Activator.CreateInstance(typeof(Stack<>).MakeGenericType(elementType));
                MethodInfo? push = stack?.GetType().GetMethod("Push");
                for (int i = items.Count - 1; i >= 0; i--) push?.Invoke(stack, [items[i]]);
                return stack;
            }

            if (generic == typeof(HashSet<>))
            {
                var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(elementType));
                MethodInfo? add = set?.GetType().GetMethod("Add");
                foreach (var item in items) add?.Invoke(set, [item]);
                return set;
            }

            if (generic == typeof(LinkedList<>))
            {
                var linked = Activator.CreateInstance(typeof(LinkedList<>).MakeGenericType(elementType));
                MethodInfo? addLast = linked?.GetType().GetMethod("AddLast", [elementType]);
                foreach (var item in items) addLast?.Invoke(linked, [item]);
                return linked;
            }
        }

        var list = (IList?)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
        if (list != null)
        {
            foreach (var item in items)
                list.Add(item);
        }

        return list;
    }

    private static object? RebuildDictionary(Type dictionaryType, Type keyType, Type valueType, List<DictionaryEntry> entries)
    {
        var dictionary = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(keyType, valueType));
        MethodInfo? add = dictionary?.GetType().GetMethod("Add", [keyType, valueType]);
        if (add != null)
        {
            foreach (var entry in entries)
            {
                if (entry.Key != null)
                    add.Invoke(dictionary, [entry.Key, entry.Value]);
            }
        }

        return dictionary;
    }

    private static object? CreateDefaultValue(Type type)
    {
        if (type == typeof(string))
            return string.Empty;
        if (type == typeof(ulong))
            return 0UL;
        if (type == typeof(Sprite))
            return default(Sprite);
        if (type == typeof(TextureAsset))
            return new TextureAsset();
        if (type == typeof(CameraTextureAsset))
            return new CameraTextureAsset();
        if (type == typeof(StyleAsset))
            return default(StyleAsset);
        if (type == typeof(ShaderAsset))
            return default(ShaderAsset);
        if (type == typeof(AudioClip))
            return new AudioClip();
        if (type.IsValueType)
            return Activator.CreateInstance(type);
        return null;
    }

    private static bool CanCreateDictionaryKey(Type type) => type == typeof(string) || type == typeof(int) || type.IsEnum;

    private static object CreateDictionaryKeyDefaultValue(Type type)
    {
        if (type == typeof(string))
            return string.Empty;
        if (type == typeof(int))
            return 0;
        if (type.IsEnum)
            return Enum.GetValues(type).GetValue(0) ?? Activator.CreateInstance(type)!;
        return Activator.CreateInstance(type)!;
    }

    private static void UpdateSiblingAssetGuid(object target, MemberInfo member, string? assetPath)
    {
        string guidMemberName = member.Name.Replace("Path", "Guid", StringComparison.Ordinal);
        var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var sibling = (MemberInfo?)target.GetType().GetProperty(guidMemberName, flags) ?? target.GetType().GetField(guidMemberName, flags);
        string guid = Path.IsPathRooted(assetPath ?? string.Empty) ? AssetPathUtility.EnsureMetaAndGetGuid(assetPath) : string.Empty;

        if (sibling is PropertyInfo property && property.CanWrite && property.PropertyType == typeof(string))
            property.SetValue(target, guid);
        else if (sibling is FieldInfo field && field.FieldType == typeof(string))
            field.SetValue(target, guid);
    }

    private static bool IsAudioExtension(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".wav" or ".ogg" or ".mp3" or ".flac" or ".mod";
    }

    private bool DrawReferenceSlot(string? label, string displayText, string? tooltip = null)
    {
        if (!string.IsNullOrWhiteSpace(label))
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(label);
            ImGui.SameLine(120);
        }

        float pickerWidth = 22f;
        float slotWidth = MathF.Max(60f, ImGui.GetContentRegionAvail().X - pickerWidth - ImGui.GetStyle().ItemInnerSpacing.X);
        Vector2 size = new(MathF.Max(1f, slotWidth), MathF.Max(1f, ImGui.GetFrameHeight()));
        ImGui.InvisibleButton("##ref_slot", size);

        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);
        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();

        Vector4 background = hovered
            ? new Vector4(0.22f, 0.24f, 0.28f, 1.0f)
            : new Vector4(0.16f, 0.17f, 0.20f, 1.0f);
        Vector4 border = hovered
            ? new Vector4(0.78f, 0.80f, 0.84f, 0.38f)
            : new Vector4(1f, 1f, 1f, 0.14f);
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(background), 4f);
        drawList.AddRect(min, max, ImGui.GetColorU32(border), 4f);

        Vector2 textSize = ImGui.CalcTextSize(displayText);
        Vector2 textPos = new(min.X + 8f, min.Y + MathF.Max(0f, (size.Y - textSize.Y) * 0.5f));
        drawList.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), displayText);

        if (hovered && !string.IsNullOrWhiteSpace(tooltip))
            ImGui.SetTooltip(tooltip);

        ImGui.SameLine();
        return clicked;
    }

    private bool DrawReferencePickerButton()
    {
        return ImGui.Button("o##picker", new Vector2(22f, ImGui.GetFrameHeight()));
    }

    private static float GetReferencePickerMaxHeight()
    {
        var viewport = ImGui.GetMainViewport();
        float viewportHeight = viewport.Size.Y;
        if (viewportHeight <= 0f)
            return 360f;

        return MathF.Min(420f, MathF.Max(220f, viewportHeight * 0.45f));
    }

    private bool BeginReferencePickerPopup()
    {
        ImGui.SetNextWindowSizeConstraints(new Vector2(240f, 0f), new Vector2(float.MaxValue, GetReferencePickerMaxHeight()));
        return ImGui.BeginPopup("Picker");
    }

    private bool BeginReferencePickerList()
    {
        float reservedHeight = ImGui.GetTextLineHeightWithSpacing() * 3f + ImGui.GetStyle().ItemSpacing.Y * 2f;
        float availableHeight = ImGui.GetContentRegionAvail().Y;
        float listHeight = MathF.Max(140f, availableHeight - reservedHeight);
        return ImGui.BeginChild("PickerList", new Vector2(0f, listHeight), ImGuiChildFlags.Borders);
    }

    private void RevealAssetReference(string assetPath, string? spriteId = null)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
            return;

        string resolved = ResolveAssetPath(assetPath);
        var projectWindow = _app.GetWindow<ProjectWindow>();
        if (projectWindow != null)
        {
            _app.OpenWindow(projectWindow);
            if (string.IsNullOrWhiteSpace(spriteId))
                projectWindow.RevealAsset(resolved);
            else
                projectWindow.RevealSprite(resolved, spriteId);
        }

        if (string.IsNullOrWhiteSpace(spriteId))
            EditorSelection.SelectAsset(resolved);
        else
            EditorSelection.SelectSpriteAsset(_app.CreateSpriteReference(resolved, spriteId));
    }

    private void RevealComponentReference(Component? component)
    {
        if (component == null)
            return;

        EditorSelection.SelectedEntity = component.Owner;
        var hierarchy = _app.GetWindow<HierarchyWindow>();
        if (hierarchy != null)
            _app.OpenWindow(hierarchy);
    }

    private void DrawAudioClipField(string name, AudioClip? current, Action<object?> onUpdate, object? target = null)
    {
        current ??= new AudioClip();

        ImGui.PushID(name);
        string btnLabel = string.IsNullOrWhiteSpace(current.Path) ? L10n.Tr("msg_none") : AssetPathUtility.DisplayName(current.Path);
        if (DrawReferenceSlot(name, btnLabel, current.Path) && !string.IsNullOrWhiteSpace(current.Path))
            RevealAssetReference(current.Path);

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (payload.Handle != null && EditorSelection.DraggedAssetPath != null && IsAudioExtension(EditorSelection.DraggedAssetPath))
                onUpdate(AudioClip.FromPath(EditorSelection.DraggedAssetPath));
            ImGui.EndDragDropTarget();
        }

        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup())
        {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(null);
                foreach (var entry in GetAssetPickerEntries("audio", IsAudioExtension))
                {
                    if (!string.IsNullOrEmpty(_searchFilter) && !entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ImGui.MenuItem(entry.RelativePath))
                        onUpdate(AudioClip.FromPath(entry.FullPath));
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }

        string clipName = current.Name;
        if (ImGui.InputText(L10n.Tr("label_name"), ref clipName, 128))
        {
            current.Name = clipName;
            onUpdate(current);
        }

        AudioType type = current.Type;
        if (ImGui.BeginCombo(L10n.Tr("label_type"), GetEnumDisplayName(typeof(AudioType), type.ToString())))
        {
            foreach (AudioType option in Enum.GetValues<AudioType>())
            {
                if (ImGui.Selectable(GetEnumDisplayName(typeof(AudioType), option.ToString()), option == type))
                {
                    current.Type = option;
                    onUpdate(current);
                }
            }
            ImGui.EndCombo();
        }

        float defaultVolume = current.DefaultVolume;
        if (ImGui.DragFloat(L10n.Tr("field_DefaultVolume"), ref defaultVolume, 0.01f, 0f, 1f))
        {
            current.DefaultVolume = defaultVolume;
            onUpdate(current);
        }

        float defaultPitch = current.DefaultPitch;
        if (ImGui.DragFloat(L10n.Tr("field_DefaultPitch"), ref defaultPitch, 0.01f, 0.1f, 4f))
        {
            current.DefaultPitch = defaultPitch;
            onUpdate(current);
        }

        bool looping = current.IsLooping;
        if (ImGui.Checkbox(L10n.Tr("field_Looping"), ref looping))
        {
            current.IsLooping = looping;
            onUpdate(current);
        }

        if (ImGui.Button(L10n.Tr("btn_preview"), new Vector2(-1, 24)))
        {
            string resolved = ResolveAssetPath(current.Path);
            current.PostLoad(resolved);
            if (target is AudioSource audioSource)
                audioSource.PlayOneShot(current);
            else
                current.Preview();
        }

        ImGui.PopID();
    }

    private void DrawSpriteField(string name, Sprite current, Action<object?> onUpdate) 
    {
        ImGui.PushID(name);
        string btnLabel = GetSpriteButtonLabel(current);
        if (DrawReferenceSlot(name, btnLabel, current.Path) && !string.IsNullOrWhiteSpace(current.Path))
            RevealAssetReference(current.Path, current.SpriteId);
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) { var ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower(); if (ext is ".png" or ".jpg" or ".jpeg") onUpdate(EditorSelection.DraggedSpriteAsset ?? CreateSpriteFromAssetPath(EditorSelection.DraggedAssetPath)); } ImGui.EndDragDropTarget(); }
        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup()) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(Sprite));
                foreach (var entry in GetAssetPickerEntries("sprites", static path =>
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    return ext is ".png" or ".jpg" or ".jpeg";
                })) {
                    if (string.IsNullOrEmpty(_searchFilter) || entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                        DrawSpritePickerEntry(entry.FullPath, entry.RelativePath, onUpdate);
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private string GetSpriteFilterLabel(SpriteTextureFilter filter) => filter == SpriteTextureFilter.Point ? L10n.Tr("sprite_filter_point") : L10n.Tr("sprite_filter_linear");

    private string GetSpriteModeLabel(SpriteImportMode mode) => mode == SpriteImportMode.Single ? L10n.Tr("sprite_mode_single") : L10n.Tr("sprite_mode_multiple");

    private string GetSpriteButtonLabel(Sprite sprite)
    {
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return L10n.Tr("msg_none");

        string fileName = Path.GetFileName(sprite.Path);
        if (string.IsNullOrWhiteSpace(sprite.SpriteId))
            return fileName;

        string fullPath = ResolveAssetPath(sprite.Path);
        var settings = _app.TryGetSpriteImportSettings(fullPath, false);
        string sliceName = settings?.Slices.FirstOrDefault(slice => string.Equals(slice.Id, sprite.SpriteId, StringComparison.OrdinalIgnoreCase))?.Name ?? sprite.SpriteId;
        return $"{fileName} / {sliceName}";
    }

    private Sprite CreateSpriteFromAssetPath(string assetPath)
    {
        var sprite = _app.CreateSpriteReference(assetPath);
        string fullPath = ResolveAssetPath(sprite.Path);
        var settings = _app.TryGetSpriteImportSettings(fullPath);
        if (settings is { SpriteMode: SpriteImportMode.Multiple } && settings.Slices.Count > 0)
            return _app.CreateSpriteReference(assetPath, settings.Slices[0].Id);

        return sprite;
    }

    private void DrawSpritePickerEntry(string fullPath, string relativePath, Action<object?> onUpdate)
    {
        var settings = _app.TryGetSpriteImportSettings(fullPath);
        if (settings is { SpriteMode: SpriteImportMode.Multiple } && settings.Slices.Count > 0)
        {
            if (ImGui.BeginMenu(relativePath))
            {
                if (ImGui.MenuItem(L10n.Tr("menu_full_texture")))
                    onUpdate(_app.CreateSpriteReference(fullPath));

                foreach (var slice in settings.Slices)
                {
                    if (ImGui.MenuItem(slice.Name))
                        onUpdate(_app.CreateSpriteReference(fullPath, slice.Id));
                }
                ImGui.EndMenu();
            }
            return;
        }

        if (ImGui.MenuItem(relativePath))
            onUpdate(_app.CreateSpriteReference(fullPath));
    }

    private void DrawStyleField(string name, StyleAsset current, Action<object?> onUpdate) 
    {
        ImGui.PushID(name);
        string btnLabel = string.IsNullOrEmpty(current.Path) ? L10n.Tr("msg_none") : Path.GetFileName(current.Path);
        if (DrawReferenceSlot(name, btnLabel, current.Path) && !string.IsNullOrWhiteSpace(current.Path))
            RevealAssetReference(current.Path);
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) if (Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower() == ".style") onUpdate((StyleAsset)EditorSelection.DraggedAssetPath); ImGui.EndDragDropTarget(); }
        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup()) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(StyleAsset));
                foreach (var entry in GetAssetPickerEntries("style", static path => Path.GetExtension(path).Equals(".style", StringComparison.OrdinalIgnoreCase))) {
                    if ((string.IsNullOrEmpty(_searchFilter) || entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) && ImGui.MenuItem(entry.RelativePath)) onUpdate((StyleAsset)entry.FullPath);
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawShaderField(string name, ShaderAsset current, Action<object?> onUpdate) 
    {
        ImGui.PushID(name);
        string btnLabel = string.IsNullOrEmpty(current.Path) ? L10n.Tr("msg_none") : Path.GetFileName(current.Path);
        if (DrawReferenceSlot(name, btnLabel, current.Path) && !string.IsNullOrWhiteSpace(current.Path))
            RevealAssetReference(current.Path);
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) if (Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower() == ".shader") onUpdate((ShaderAsset)EditorSelection.DraggedAssetPath); ImGui.EndDragDropTarget(); }
        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup()) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(default(ShaderAsset));
                foreach (var entry in GetAssetPickerEntries("shader", static path => Path.GetExtension(path).Equals(".shader", StringComparison.OrdinalIgnoreCase))) {
                    if ((string.IsNullOrEmpty(_searchFilter) || entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) && ImGui.MenuItem(entry.RelativePath)) onUpdate((ShaderAsset)entry.FullPath);
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawCameraTextureAssetField(string name, CameraTextureAsset current, Action<object?> onUpdate)
        => DrawTextureAssetField(name, current, asset => onUpdate(asset is TextureAsset texture ? new CameraTextureAsset(texture.Path, texture.Guid) : new CameraTextureAsset()), cameraOnly: true);

    private void DrawTextureAssetField(string name, TextureAsset current, Action<object?> onUpdate, bool cameraOnly = false)
    {
        ImGui.PushID(name);
        string btnLabel = string.IsNullOrEmpty(current.Path) ? L10n.Tr("msg_none") : Path.GetFileName(current.Path);
        if (DrawReferenceSlot(name, btnLabel, current.Path) && !string.IsNullOrWhiteSpace(current.Path))
            RevealAssetReference(current.Path);
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null && IsTextureAssetPath(EditorSelection.DraggedAssetPath, cameraOnly)) onUpdate(CreateTextureAssetReference(EditorSelection.DraggedAssetPath, cameraOnly)); ImGui.EndDragDropTarget(); }
        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup()) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(cameraOnly ? new CameraTextureAsset() : new TextureAsset());
                string cacheKey = cameraOnly ? "texture:camera" : "texture:any";
                foreach (var entry in GetAssetPickerEntries(cacheKey, path => IsTextureAssetPath(path, cameraOnly))) {
                    if ((string.IsNullOrEmpty(_searchFilter) || entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) && ImGui.MenuItem(entry.RelativePath)) onUpdate(CreateTextureAssetReference(entry.FullPath, cameraOnly));
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private static bool IsTextureAssetPath(string path, bool cameraOnly)
    {
        string ext = Path.GetExtension(path);
        if (ext.Equals(".rendertexture", StringComparison.OrdinalIgnoreCase))
            return true;
        return !cameraOnly &&
               (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase));
    }

    private static TextureAsset CreateTextureAssetReference(string path, bool cameraOnly)
        => cameraOnly || Path.GetExtension(path).Equals(".rendertexture", StringComparison.OrdinalIgnoreCase)
            ? new CameraTextureAsset(path)
            : new TextureAsset(path);

    private void DrawAssetReferenceField(string name, string current, string exts, Action<object?> onUpdate) 
    {
        ImGui.PushID(name);
        string btnLabel = string.IsNullOrEmpty(current) ? L10n.Tr("msg_none") : Path.GetFileName(current);
        if (DrawReferenceSlot(name == "##v" ? null : name, btnLabel, current) && !string.IsNullOrWhiteSpace(current))
            RevealAssetReference(current);
        if (ImGui.BeginDragDropTarget()) { var p = ImGui.AcceptDragDropPayload("ASSET_PATH"); if (p.Handle != null && EditorSelection.DraggedAssetPath != null) { var ext = Path.GetExtension(EditorSelection.DraggedAssetPath).ToLower(); if (exts.Split(';').Any(e => e.Trim().ToLower() == ext)) onUpdate(EditorSelection.DraggedAssetPath); } ImGui.EndDragDropTarget(); }
        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup()) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(null);
                var normalizedExtensions = exts.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(static ext => ext.StartsWith('.') ? ext.ToLowerInvariant() : "." + ext.ToLowerInvariant())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                string cacheKey = "ext:" + string.Join(';', normalizedExtensions);
                foreach (var entry in GetAssetPickerEntries(cacheKey, path => normalizedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))) {
                    if ((string.IsNullOrEmpty(_searchFilter) || entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) && ImGui.MenuItem(entry.RelativePath)) onUpdate(entry.FullPath);
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawComponentReferenceField(string name, Component? current, Type targetType, Action<object?> onUpdate) 
    {
        ImGui.PushID(name);
        string btnLabel = current == null ? L10n.Tr("msg_none") : $"{current.Owner.Name} ({LocalizeTypeName(current.GetType())})";
        if (DrawReferenceSlot(name, btnLabel) && current != null)
            RevealComponentReference(current);
        if (DrawReferencePickerButton()) ImGui.OpenPopup("Picker");
        if (BeginReferencePickerPopup()) {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none"))) onUpdate(null);
                if (WorldManager.ActiveWorld != null) foreach (var e in WorldManager.ActiveWorld.GetAllEntities()) {
                    var c = e.GetComponent(targetType);
                    if (c != null && (string.IsNullOrEmpty(_searchFilter) || e.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))) if (ImGui.MenuItem($"{e.Name}##entity_{e.Id}")) onUpdate(c);
                }
                ImGui.EndChild();
            }
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawEntityReferenceField(string name, Entity? current, Action<object?> onUpdate)
    {
        ImGui.PushID(name);
        bool isBlueprintAssetReference = IsBlueprintAssetReference(current);
        string btnLabel = current == null
            ? L10n.Tr("msg_none")
            : isBlueprintAssetReference
                ? Path.GetFileNameWithoutExtension(current.BlueprintAssetPath)
                : current.Name;
        string? tooltip = isBlueprintAssetReference ? current?.BlueprintAssetPath : current?.Id.ToString();
        if (DrawReferenceSlot(name, btnLabel, tooltip) && current != null)
        {
            if (isBlueprintAssetReference)
                RevealBlueprintAssetReference(current);
            else
            {
                EditorSelection.SelectedEntity = current;
                EditorSelection.ClearAssetSelection();
            }
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
            if (payload.Handle != null &&
                EditorSelection.DraggedAssetPath != null &&
                EditorSelection.DraggedAssetPath.EndsWith(".blueprint", StringComparison.OrdinalIgnoreCase))
            {
                onUpdate(CreateBlueprintAssetReference(EditorSelection.DraggedAssetPath));
            }

            ImGui.EndDragDropTarget();
        }

        if (DrawReferencePickerButton())
            ImGui.OpenPopup("Picker");

        if (BeginReferencePickerPopup())
        {
            ImGui.InputText(L10n.Tr("label_search"), ref _searchFilter, 64);
            ImGui.Separator();

            if (BeginReferencePickerList())
            {
                if (ImGui.MenuItem(L10n.Tr("msg_none")))
                    onUpdate(null);

                if (WorldManager.ActiveWorld != null)
                {
                    ImGui.Separator();
                    ImGui.TextDisabled("World Entities");
                    foreach (var entity in WorldManager.ActiveWorld.GetAllEntities())
                    {
                        if (!string.IsNullOrEmpty(_searchFilter) &&
                            !entity.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (ImGui.MenuItem($"{entity.Name}##entity_{entity.Id}"))
                            onUpdate(entity);
                    }
                }

                ImGui.Separator();
                ImGui.TextDisabled("Blueprint Assets");
                foreach (var entry in GetAssetPickerEntries("ext:.blueprint", path => Path.GetExtension(path).Equals(".blueprint", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!string.IsNullOrEmpty(_searchFilter) &&
                        !entry.RelativePath.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (ImGui.MenuItem(entry.RelativePath))
                        onUpdate(CreateBlueprintAssetReference(entry.FullPath));
                }

                ImGui.EndChild();
            }

            ImGui.EndPopup();
        }

        ImGui.PopID();
    }

    private static bool IsBlueprintAssetReference(Entity? entity)
    {
        return entity != null &&
               !string.IsNullOrWhiteSpace(entity.BlueprintAssetPath) &&
               !entity.BlueprintSourceEntityId.HasValue;
    }

    private static Entity CreateBlueprintAssetReference(string path)
    {
        AssetReferenceData reference = AssetPathUtility.CreateReference(path);
        return new Entity(Path.GetFileNameWithoutExtension(path))
        {
            BlueprintAssetPath = reference.Path,
            BlueprintAssetGuid = reference.Guid
        };
    }

    private void RevealBlueprintAssetReference(Entity entity)
    {
        string resolvedPath = AssetPathUtility.ResolvePath(_app.ProjectPath ?? _app.AssetsPath, entity.BlueprintAssetPath, entity.BlueprintAssetGuid);
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return;

        var projectWindow = _app.GetWindow<ProjectWindow>();
        if (projectWindow != null)
        {
            _app.OpenWindow(projectWindow);
            projectWindow.RevealAsset(resolvedPath);
        }

        EditorSelection.SelectedEntity = null;
        EditorSelection.SelectAsset(resolvedPath);
    }
}
