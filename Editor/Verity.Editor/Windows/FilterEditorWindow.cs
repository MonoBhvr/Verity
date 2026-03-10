using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Input;

namespace Verity.Editor.Windows;

public class FilterEditorWindow : EditorWindow
{
    private string _newFilterName = "NewFilter";
    private string _newEnumTypeName = "Verity.Input.KeyCode, Verity.Input";
    private FilterMode _newFilterMode = FilterMode.Whitelist;
    
    private Filter? _selectedFilter;
    private string _editValueBuffer = "";

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
                if (ImGui.Selectable($"{filter.Name} ({filter.Mode})", selected))
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
        if (ImGui.InputText("Name", ref name, 64))
        {
            // Renaming is tricky because it's the key in dictionary.
            // For simplicity, we just update the object and let the user Save.
            _selectedFilter.Name = name;
        }
        
        int mode = (int)_selectedFilter.Mode;
        if (ImGui.Combo("Mode", ref mode, "Whitelist\0Blacklist\0"))
        {
            _selectedFilter.Mode = (FilterMode)mode;
        }
        
        ImGui.Text($"Enum Type: {_selectedFilter.EnumTypeName}");
        
        ImGui.Separator();
        ImGui.Text("Values:");
        
        if (ImGui.BeginChild("ValuesList", new Vector2(0, 200), ImGuiChildFlags.Border))
        {
            for (int i = 0; i < _selectedFilter.Values.Count; i++)
            {
                ImGui.PushID(i);
                ImGui.Text(_selectedFilter.Values[i]);
                ImGui.SameLine(ImGui.GetWindowWidth() - 30);
                if (ImGui.Button("X"))
                {
                    _selectedFilter.Values.RemoveAt(i);
                    _selectedFilter.UpdateCache();
                    FilterManager.Save();
                }
                ImGui.PopID();
            }
            ImGui.EndChild();
        }
        
        ImGui.InputText("##AddValue", ref _editValueBuffer, 64);
        ImGui.SameLine();
        if (ImGui.Button("Add Value"))
        {
            if (!string.IsNullOrWhiteSpace(_editValueBuffer))
            {
                _selectedFilter.Values.Add(_editValueBuffer);
                _selectedFilter.UpdateCache();
                _editValueBuffer = "";
                FilterManager.Save();
            }
        }
        
        if (ImGui.Button("Save Changes", new Vector2(-1, 0)))
        {
            FilterManager.Register(_selectedFilter); // This handles Save()
        }
    }

    private void DrawCreateFilter()
    {
        ImGui.Text("Create New Filter");
        ImGui.Separator();
        
        ImGui.InputText("Filter Name", ref _newFilterName, 64);
        ImGui.InputText("Enum Type (Fully Qualified)", ref _newEnumTypeName, 128);
        
        int mode = (int)_newFilterMode;
        if (ImGui.Combo("Mode", ref mode, "Whitelist\0Blacklist\0"))
        {
            _newFilterMode = (FilterMode)mode;
        }
        
        if (ImGui.Button("Create Filter", new Vector2(-1, 0)))
        {
            if (!string.IsNullOrWhiteSpace(_newFilterName))
            {
                var type = Type.GetType(_newEnumTypeName);
                if (type == null)
                {
                    // Try to find it in assemblies
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType(_newEnumTypeName) ?? asm.GetType(_newEnumTypeName.Split(',')[0]);
                        if (type != null) break;
                    }
                }

                if (type != null && type.IsEnum)
                {
                    var filter = new Filter(_newFilterName, type, Array.CreateInstance(type, 0), _newFilterMode);
                    FilterManager.Register(filter);
                }
                else
                {
                    // Type not found or not enum
                    // We can still create it as raw strings, Filter class will try to parse later
                    var filter = new Filter { Name = _newFilterName, EnumTypeName = _newEnumTypeName, Mode = _newFilterMode };
                    FilterManager.Register(filter);
                }
            }
        }
        
        ImGui.Separator();
        ImGui.TextWrapped("Tip: For KeyCode, use 'Verity.Input.KeyCode, Verity.Input'.");
    }
}
