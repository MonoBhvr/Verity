using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Filter;
using FilterType = Verity.Filter.Filter;
using Verity.Core.Engine;
using Verity.Input;

namespace Verity.Editor.Windows;

public unsafe class FilterEditorWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string _newFilterName = L10n.Tr("creation_default_filter");
    private string _newEnumTypeName = "Verity.Input.KeyCode, Verity.Input";
    private FilterMode _newFilterMode = FilterMode.Whitelist;
    private bool _createAsMixed = true;
    
    private FilterType? _selectedFilter;
    private string _editValueBuffer = "";
    private string _editValueTypeBuffer = ""; 
    private string _enumSearchFilter = "";

    public FilterEditorWindow(EditorApp app) : base(L10n.Tr("label_filters"))
    {
        _app = app;
        IsOpen = false;
    }

    public override void OnGui()
    {
        ImGui.SetNextWindowSize(new Vector2(750, 550), ImGuiCond.FirstUseEver);
        DrawFilterEditor(false);
    }

    public override void RefreshTitle() { Title = L10n.Tr("label_filters"); }

    public void DrawFilterEditor(bool vertical = false)
    {
        if (vertical) DrawFilterEditorVertical();
        else DrawFilterEditorHorizontal();
    }

    private void DrawFilterEditorHorizontal()
    {
        ImGui.Columns(2, "FilterEditorColumns", true);
        ImGui.SetColumnWidth(0, 250);
        
        ImGui.Text(L10n.Tr("label_filters"));
        ImGui.Separator();
        
        if (ImGui.BeginChild("FilterList"))
        {
            if (ImGui.Selectable(L10n.Tr("btn_create_new") ?? "[+ Create New Filter]", _selectedFilter == null))
            {
                _selectedFilter = null;
            }
            ImGui.Separator();

            foreach (var filter in FilterManager.GetAllFilters())
            {
                ImGui.PushID(filter.Name);
                bool isSelected = _selectedFilter == filter;
                string mixedBadge = filter.MixedValues.Count > 0 || string.IsNullOrEmpty(filter.EnumTypeName)
                    ? L10n.Tr("ui_filter_mixed_badge") + " "
                    : string.Empty;
                string label = mixedBadge + filter.Name;
                string modeLabel = GetFilterModeLabel(filter.Mode);
                
                if (ImGui.Selectable($"{label} ({modeLabel})##sel", isSelected, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X - 35, 0)))
                {
                    _selectedFilter = filter;
                    _editValueTypeBuffer = filter.EnumTypeName ?? "";
                    _editValueBuffer = "";
                }
                
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
                if (ImGui.Button("X##del", new Vector2(25, 0)))
                {
                    _app.RequestDeleteFilter(filter);
                }
                ImGui.PopStyleColor();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        
        ImGui.NextColumn();
        if (_selectedFilter != null)
        {
            bool shouldClearSelection = false;
            DrawFilterDetails(_selectedFilter, () => { shouldClearSelection = true; });
            if (shouldClearSelection) _selectedFilter = null;
        }
        else DrawCreateFilter();
        
        ImGui.Columns(1);
    }

    private void DrawFilterEditorVertical()
    {
        string currentLabel = _selectedFilter == null ? L10n.Tr("btn_create_new") : _selectedFilter.Name;
        
        ImGui.Columns(2, "FilterSelectVerticalCols", false);
        ImGui.SetColumnWidth(0, ImGui.GetWindowWidth() - 50);
        
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##FilterSelectVertical", currentLabel))
        {
            if (ImGui.Selectable(L10n.Tr("btn_create_new"), _selectedFilter == null)) _selectedFilter = null;
            ImGui.Separator();
            foreach (var filter in FilterManager.GetAllFilters())
            {
                if (ImGui.Selectable($"{filter.Name} ({GetFilterModeLabel(filter.Mode)})", _selectedFilter == filter))
                {
                    _selectedFilter = filter;
                    _editValueTypeBuffer = filter.EnumTypeName ?? "";
                    _editValueBuffer = "";
                }
            }
            ImGui.EndCombo();
        }

        ImGui.NextColumn();
        if (_selectedFilter != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.6f, 0.2f, 0.2f, 1.0f));
            if (ImGui.Button("X##del_vert", new Vector2(-1, 0)))
            {
                _app.RequestDeleteFilter(_selectedFilter);
            }
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("X##del_vert_dis", new Vector2(-1, 0));
            ImGui.EndDisabled();
        }
        ImGui.Columns(1);

        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 10));

        if (_selectedFilter != null)
        {
            bool shouldClearSelection = false;
            DrawFilterDetails(_selectedFilter, () => { shouldClearSelection = true; });
            if (shouldClearSelection) _selectedFilter = null;
        }
        else DrawCreateFilter();
    }

    private void DrawFilterDetails(FilterType selectedFilter, Action onBack)
    {
        ImGui.Text($"{L10n.Tr("menu_edit")}: {selectedFilter.Name}");
        ImGui.SameLine(ImGui.GetWindowWidth() - 120);
        if (ImGui.Button(L10n.Tr("btn_back"))) { onBack(); return; }
        
        ImGui.Separator();
        
        string name = selectedFilter.Name;
        ImGui.Text(L10n.Tr("label_name")); ImGui.SameLine(100);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##FilterName", ref name, 64)) selectedFilter.Name = name;
        
        int mode = (int)selectedFilter.Mode;
        ImGui.Text(L10n.Tr("ui_filter_mode")); ImGui.SameLine(100);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.Combo("##FilterMode", ref mode, $"{L10n.Tr("label_whitelist")}\0{L10n.Tr("label_blacklist")}\0")) selectedFilter.Mode = (FilterMode)mode;
        
        ImGui.Separator();
        ImGui.Text(L10n.Tr("label_values"));
        
        float footerHeight = ImGui.GetFrameHeightWithSpacing() * 5 + 20;
        if (ImGui.BeginChild("ValuesList", new Vector2(0, -footerHeight), ImGuiChildFlags.Borders))
        {
            for (int i = 0; i < selectedFilter.Values.Count; i++)
            {
                ImGui.PushID($"v_{i}");
                ImGui.Text($"{L10n.Tr("ui_filter_static_badge")} {selectedFilter.Values[i]}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 35);
                if (ImGui.Button("X")) { selectedFilter.Values.RemoveAt(i); selectedFilter.UpdateCache(); FilterManager.Save(); ImGui.PopID(); break; }
                ImGui.PopID();
            }

            for (int i = 0; i < selectedFilter.MixedValues.Count; i++)
            {
                ImGui.PushID($"m_{i}");
                var mv = selectedFilter.MixedValues[i];
                string typeDisplayName = mv.TypeName.Split(',')[0].Split('.').Last();
                ImGui.Text($"[{typeDisplayName}] {mv.Value}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 35);
                if (ImGui.Button("X")) { selectedFilter.MixedValues.RemoveAt(i); selectedFilter.UpdateCache(); FilterManager.Save(); ImGui.PopID(); break; }
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        
        ImGui.Separator();
        ImGui.TextDisabled(L10n.Tr("ui_filter_add_new_rule"));

        bool isSingleType = !string.IsNullOrEmpty(selectedFilter.EnumTypeName);
        if (isSingleType) ImGui.BeginDisabled();
        DrawUnifiedTypePicker("##AddTypePicker", ref _editValueTypeBuffer);
        if (isSingleType) ImGui.EndDisabled();

        var resolvedType = string.IsNullOrEmpty(_editValueTypeBuffer) ? null : FilterManager.ResolveTypeInternal(_editValueTypeBuffer);
        if (resolvedType != null)
        {
            ImGui.TextDisabled(L10n.Tr("ui_filter_select_value"));
            if (resolvedType.IsEnum)
            {
                DrawEnumValueCombo("##ValueEnumCombo", resolvedType, ref _editValueBuffer);
            }
            else if (resolvedType.Name == "Tag") DrawSimpleStringCombo("##ValueTagCombo", _app.ProjectSettings.Tags, ref _editValueBuffer);
            else if (resolvedType.Name == "PhysicsGroup") DrawSimpleStringCombo("##ValuePhysicsCombo", _app.ProjectSettings.PhysicsGroups, ref _editValueBuffer);
            else if (resolvedType.Name == "SortingLayer") DrawSimpleStringCombo("##ValueLayerCombo", _app.ProjectSettings.SortingLayers, ref _editValueBuffer);
        }

        ImGui.Dummy(new Vector2(0, 5));
        bool canAdd = !string.IsNullOrEmpty(_editValueTypeBuffer) && !string.IsNullOrEmpty(_editValueBuffer);
        if (!canAdd) ImGui.BeginDisabled();
        if (ImGui.Button(L10n.Tr("btn_add"), new Vector2(-1, 30)))
        {
            if (string.IsNullOrEmpty(selectedFilter.EnumTypeName)) selectedFilter.MixedValues.Add(new FilterValue { TypeName = _editValueTypeBuffer, Value = _editValueBuffer });
            else selectedFilter.Values.Add(_editValueBuffer);
            selectedFilter.UpdateCache();
            _editValueBuffer = "";
            FilterManager.Save();
        }
        if (!canAdd) ImGui.EndDisabled();
        
        ImGui.Separator();
        if (ImGui.Button(L10n.Tr("btn_delete_filter"), new Vector2(-1, 25))) 
        { 
            _app.RequestDeleteFilter(selectedFilter);
        }
    }

    private void DrawUnifiedTypePicker(string id, ref string currentType)
    {
        Type? resolved = string.IsNullOrEmpty(currentType) ? null : FilterManager.ResolveTypeInternal(currentType);
        string preview = resolved != null ? (resolved.Name == "Tag" || resolved.Name == "PhysicsGroup" || resolved.Name == "SortingLayer" ? resolved.Name : resolved.FullName!) : L10n.Tr("ui_filter_select_type");
        
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(id, preview))
        {
            if (ImGui.Selectable(L10n.Tr("ui_filter_system_tag"), currentType == "Verity.Core.Tag, Verity.Core")) { currentType = "Verity.Core.Tag, Verity.Core"; _editValueBuffer = ""; }
            if (ImGui.Selectable(L10n.Tr("ui_filter_system_physics_group"), currentType == "Verity.Core.PhysicsGroup, Verity.Core")) { currentType = "Verity.Core.PhysicsGroup, Verity.Core"; _editValueBuffer = ""; }
            if (ImGui.Selectable(L10n.Tr("ui_filter_system_sorting_layer"), currentType == "Verity.Core.SortingLayer, Verity.Core")) { currentType = "Verity.Core.SortingLayer, Verity.Core"; _editValueBuffer = ""; }
            if (ImGui.Selectable("System: Key Code", currentType == typeof(KeyCode).AssemblyQualifiedName)) { currentType = typeof(KeyCode).AssemblyQualifiedName ?? typeof(KeyCode).FullName!; _editValueBuffer = ""; }
            if (ImGui.Selectable("System: Mouse Button", currentType == typeof(MouseButton).AssemblyQualifiedName)) { currentType = typeof(MouseButton).AssemblyQualifiedName ?? typeof(MouseButton).FullName!; _editValueBuffer = ""; }
            
            ImGui.Separator();
            ImGui.TextDisabled(L10n.Tr("ui_filter_search_enums"));
            ImGui.InputText("##EnumSearchSub", ref _enumSearchFilter, 64);
            
            var enumTypes = _app.ScriptCompiler?.GetAllEnumTypes() ?? new List<Type>();
            if (ImGui.BeginChild("EnumListSub", new Vector2(0, 200)))
            {
                foreach (var et in enumTypes)
                {
                    if (string.IsNullOrEmpty(_enumSearchFilter) || et.FullName!.Contains(_enumSearchFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        if (ImGui.Selectable(et.FullName ?? et.Name, currentType == et.AssemblyQualifiedName))
                        {
                            currentType = et.AssemblyQualifiedName ?? et.FullName!;
                            _editValueBuffer = "";
                        }
                    }
                }
            }
            ImGui.EndChild();
            ImGui.EndCombo();
        }
    }

    private void DrawSimpleStringCombo(string id, List<string> items, ref string current)
    {
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo(id, string.IsNullOrEmpty(current) ? L10n.Tr("ui_filter_select_value_combo") : current))
        {
            foreach (var item in items) if (ImGui.Selectable(item, item == current)) current = item;
            ImGui.EndCombo();
        }
    }

    private void DrawEnumValueCombo(string id, Type enumType, ref string current)
    {
        string[] names = Enum.GetNames(enumType);
        string preview = string.IsNullOrEmpty(current) ? L10n.Tr("ui_filter_select_value_combo") : current;

        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo(id, preview))
            return;

        foreach (string name in names)
        {
            bool isSelected = string.Equals(name, current, StringComparison.Ordinal);
            if (ImGui.Selectable(name, isSelected))
                current = name;

            if (isSelected)
                ImGui.SetItemDefaultFocus();
        }

        ImGui.EndCombo();
    }

    private void DrawCreateFilter()
    {
        ImGui.Text(L10n.Tr("btn_create_filter"));
        ImGui.Separator();
        
        ImGui.Text(L10n.Tr("label_name"));
        ImGui.InputText("##NewName", ref _newFilterName, 64);
        
        ImGui.Checkbox(L10n.Tr("ui_filter_mixed_type_recommended"), ref _createAsMixed);
        
        if (!_createAsMixed)
        {
            ImGui.Text(L10n.Tr("ui_filter_target_type"));
            DrawUnifiedTypePicker("##NewTypePicker", ref _newEnumTypeName);
        }
        
        int mode = (int)_newFilterMode;
        ImGui.Text(L10n.Tr("ui_filter_default_mode"));
        if (ImGui.Combo("##NewMode", ref mode, $"{L10n.Tr("label_whitelist")}\0{L10n.Tr("label_blacklist")}\0")) _newFilterMode = (FilterMode)mode;
        
        ImGui.Dummy(new Vector2(0, 10));
        if (ImGui.Button(L10n.Tr("btn_create"), new Vector2(-1, 40)))
        {
            if (!string.IsNullOrWhiteSpace(_newFilterName))
            {
                FilterType filter;
                if (_createAsMixed) filter = new MixedFilter(_newFilterName, _newFilterMode);
                else
                {
                    var type = FilterManager.ResolveTypeInternal(_newEnumTypeName);
                    filter = type != null ? new FilterType(_newFilterName, type, Array.CreateInstance(type, 0), _newFilterMode) 
                                         : new FilterType { Name = _newFilterName, EnumTypeName = _newEnumTypeName, Mode = _newFilterMode };
                }
                FilterManager.Register(filter);
                _selectedFilter = filter;
            }
        }
    }

    public void SelectFilter(FilterType? filter) 
    { 
        _selectedFilter = filter;
        if (filter != null)
        {
            _editValueTypeBuffer = filter.EnumTypeName ?? "";
            _editValueBuffer = "";
        }
    }

    private static string GetFilterModeLabel(FilterMode mode)
    {
        return mode == FilterMode.Whitelist
            ? L10n.Tr("label_whitelist")
            : L10n.Tr("label_blacklist");
    }
}
