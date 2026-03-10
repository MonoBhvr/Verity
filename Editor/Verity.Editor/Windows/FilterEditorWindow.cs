using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Input;

namespace Verity.Editor.Windows;

public class FilterEditorWindow : EditorWindow
{
    private string _newFilterName = "NewFilter";
    private string _newEnumTypeName = "Verity.Input.KeyCode, Verity.Input";
    private FilterMode _newFilterMode = FilterMode.Whitelist;
    private bool _createAsMixed = true;
    
    private Filter? _selectedFilter;
    private string _editValueBuffer = "";
    private string _editValueTypeBuffer = "Verity.Input.KeyCode, Verity.Input";

    public FilterEditorWindow() : base("Filter Editor")
    {
    }

    public override void OnGui()
    {
        ImGui.Columns(2, "FilterEditorColumns", true);
        
        // Left column: List of filters
        ImGui.Text("Filters");
        ImGui.Separator();
        
        if (ImGui.BeginChild("FilterList"))
        {
            foreach (var filter in FilterManager.GetAllFilters())
            {
                bool selected = _selectedFilter == filter;
                string label = filter is MixedFilter ? $"[M] {filter.Name}" : filter.Name;
                if (ImGui.Selectable($"{label} ({filter.Mode})", selected))
                {
                    _selectedFilter = filter;
                }
            }
            ImGui.EndChild();
        }
        
        ImGui.NextColumn();
        
        // Right column: Filter details & Creation
        if (_selectedFilter != null)
        {
            DrawFilterDetails();
        }
        else
        {
            DrawCreateFilter();
        }
        
        ImGui.Columns(1);
    }

    private void DrawFilterDetails()
    {
        ImGui.Text($"Editing: {_selectedFilter!.Name}");
        if (ImGui.Button("Back to Create")) { _selectedFilter = null; return; }
        ImGui.SameLine();
        if (ImGui.Button("Delete Filter"))
        {
            FilterManager.Remove(_selectedFilter.Name);
            _selectedFilter = null;
            return;
        }
        
        ImGui.Separator();
        
        string name = _selectedFilter.Name;
        if (ImGui.InputText("Name", ref name, 64)) _selectedFilter.Name = name;
        
        int mode = (int)_selectedFilter.Mode;
        if (ImGui.Combo("Mode", ref mode, "Whitelist\0Blacklist\0")) _selectedFilter.Mode = (FilterMode)mode;
        
        if (!(_selectedFilter is MixedFilter))
            ImGui.Text($"Enum Type: {_selectedFilter.EnumTypeName}");
        
        ImGui.Separator();
        ImGui.Text("Values:");
        
        if (ImGui.BeginChild("ValuesList", new Vector2(0, 200), ImGuiChildFlags.Border))
        {
            // Simple Values
            for (int i = 0; i < _selectedFilter.Values.Count; i++)
            {
                ImGui.PushID($"v_{i}");
                ImGui.Text($"[Single] {_selectedFilter.Values[i]}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 30);
                if (ImGui.Button("X")) { _selectedFilter.Values.RemoveAt(i); _selectedFilter.UpdateCache(); FilterManager.Save(); }
                ImGui.PopID();
            }

            // Mixed Values
            for (int i = 0; i < _selectedFilter.MixedValues.Count; i++)
            {
                ImGui.PushID($"m_{i}");
                var mv = _selectedFilter.MixedValues[i];
                string typeName = mv.TypeName.Split(',')[0].Split('.').Last();
                ImGui.Text($"[{typeName}] {mv.Value}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 30);
                if (ImGui.Button("X")) { _selectedFilter.MixedValues.RemoveAt(i); _selectedFilter.UpdateCache(); FilterManager.Save(); }
                ImGui.PopID();
            }
            ImGui.EndChild();
        }
        
        if (_selectedFilter is MixedFilter || _selectedFilter.MixedValues.Count > 0)
        {
            ImGui.InputText("Type##Add", ref _editValueTypeBuffer, 128);
            ImGui.InputText("Value##Add", ref _editValueBuffer, 64);
            if (ImGui.Button("Add Mixed Value", new Vector2(-1, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_editValueBuffer))
                {
                    _selectedFilter.MixedValues.Add(new FilterValue { TypeName = _editValueTypeBuffer, Value = _editValueBuffer });
                    _selectedFilter.UpdateCache();
                    _editValueBuffer = "";
                    FilterManager.Save();
                }
            }
        }
        else
        {
            ImGui.InputText("Value##Add", ref _editValueBuffer, 64);
            if (ImGui.Button("Add Value", new Vector2(-1, 0)))
            {
                if (!string.IsNullOrWhiteSpace(_editValueBuffer))
                {
                    _selectedFilter.Values.Add(_editValueBuffer);
                    _selectedFilter.UpdateCache();
                    _editValueBuffer = "";
                    FilterManager.Save();
                }
            }
        }
        
        if (ImGui.Button("Save Changes", new Vector2(-1, 30))) FilterManager.Register(_selectedFilter);
    }

    private void DrawCreateFilter()
    {
        ImGui.Text("Create New Filter");
        ImGui.Separator();
        
        ImGui.InputText("Filter Name", ref _newFilterName, 64);
        ImGui.Checkbox("Mixed Type Filter (Recommended)", ref _createAsMixed);
        
        if (!_createAsMixed)
            ImGui.InputText("Enum Type", ref _newEnumTypeName, 128);
        
        int mode = (int)_newFilterMode;
        if (ImGui.Combo("Mode", ref mode, "Whitelist\0Blacklist\0")) _newFilterMode = (FilterMode)mode;
        
        if (ImGui.Button("Create Filter", new Vector2(-1, 40)))
        {
            if (!string.IsNullOrWhiteSpace(_newFilterName))
            {
                Filter filter;
                if (_createAsMixed)
                {
                    filter = new MixedFilter(_newFilterName, _newFilterMode);
                }
                else
                {
                    var type = Type.GetType(_newEnumTypeName);
                    if (type == null)
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            type = asm.GetType(_newEnumTypeName) ?? asm.GetType(_newEnumTypeName.Split(',')[0]);
                            if (type != null) break;
                        }
                    }
                    filter = type != null ? new Filter(_newFilterName, type, Array.CreateInstance(type, 0), _newFilterMode) 
                                         : new Filter { Name = _newFilterName, EnumTypeName = _newEnumTypeName, Mode = _newFilterMode };
                }
                FilterManager.Register(filter);
            }
        }
    }
}
