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
        IsOpen = false;
    }

    public override void OnGui()
    {
        ImGui.SetNextWindowSize(new Vector2(600, 400), ImGuiCond.FirstUseEver);

        DrawFilterEditor(ref _selectedFilter, ref _newFilterName, ref _newEnumTypeName, ref _newFilterMode, ref _createAsMixed, ref _editValueBuffer, ref _editValueTypeBuffer);
    }

    public static void DrawFilterEditor(ref Filter? selectedFilter, ref string newFilterName, ref string newEnumTypeName, ref FilterMode newFilterMode, ref bool createAsMixed, ref string editValueBuffer, ref string editValueTypeBuffer)
    {
        ImGui.Columns(2, "FilterEditorColumns", true);
        
        ImGui.Text("Filters");
        ImGui.Separator();
        
        if (ImGui.BeginChild("FilterList"))
        {
            foreach (var filter in FilterManager.GetAllFilters())
            {
                bool isSelected = selectedFilter == filter;
                string label = filter is MixedFilter ? $"[M] {filter.Name}" : filter.Name;
                if (ImGui.Selectable($"{label} ({filter.Mode})", isSelected))
                {
                    selectedFilter = filter;
                }
            }
            ImGui.EndChild();
        }
        
        ImGui.NextColumn();
        
        if (selectedFilter != null)
        {
            var currentFilter = selectedFilter;
            bool shouldClearSelection = false;
            DrawFilterDetails(currentFilter, ref editValueBuffer, ref editValueTypeBuffer, () => { shouldClearSelection = true; });
            if (shouldClearSelection) selectedFilter = null;
        }
        else
        {
            DrawCreateFilter(ref newFilterName, ref newEnumTypeName, ref newFilterMode, ref createAsMixed);
        }
        
        ImGui.Columns(1);
    }

    public static void DrawFilterDetails(Filter selectedFilter, ref string editValueBuffer, ref string editValueTypeBuffer, Action onBack)
    {
        ImGui.Text($"Editing: {selectedFilter.Name}");
        if (ImGui.Button("Back to Create")) { onBack(); return; }
        ImGui.SameLine();
        if (ImGui.Button("Delete Filter"))
        {
            FilterManager.Remove(selectedFilter.Name);
            onBack();
            return;
        }
        
        ImGui.Separator();
        
        string name = selectedFilter.Name;
        if (ImGui.InputText("Name", ref name, 64)) selectedFilter.Name = name;
        
        int mode = (int)selectedFilter.Mode;
        if (ImGui.Combo("Mode", ref mode, "Whitelist\0Blacklist\0")) selectedFilter.Mode = (FilterMode)mode;
        
        if (!(selectedFilter is MixedFilter))
            ImGui.Text($"Enum Type: {selectedFilter.EnumTypeName}");
        
        ImGui.Separator();
        ImGui.Text("Values:");
        
        // ImGuiChildFlags.None 사용 및 boolean 파라미터 제거 (버전 호환성)
        if (ImGui.BeginChild("ValuesList", new Vector2(0, 150), ImGuiChildFlags.None, ImGuiWindowFlags.ChildWindow))
        {
            for (int i = 0; i < selectedFilter.Values.Count; i++)
            {
                ImGui.PushID($"v_{i}");
                ImGui.Text($"[Single] {selectedFilter.Values[i]}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 30);
                if (ImGui.Button("X")) { selectedFilter.Values.RemoveAt(i); selectedFilter.UpdateCache(); FilterManager.Save(); }
                ImGui.PopID();
            }

            for (int i = 0; i < selectedFilter.MixedValues.Count; i++)
            {
                ImGui.PushID($"m_{i}");
                var mv = selectedFilter.MixedValues[i];
                string typeName = mv.TypeName.Split(',')[0].Split('.').Last();
                ImGui.Text($"[{typeName}] {mv.Value}");
                ImGui.SameLine(ImGui.GetWindowWidth() - 30);
                if (ImGui.Button("X")) { selectedFilter.MixedValues.RemoveAt(i); selectedFilter.UpdateCache(); FilterManager.Save(); }
                ImGui.PopID();
            }
            ImGui.EndChild();
        }
        
        if (selectedFilter is MixedFilter || selectedFilter.MixedValues.Count > 0)
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##TypeAdd", "Enum Type...", ref editValueTypeBuffer, 128);
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##ValueAdd", "Value...", ref editValueBuffer, 64);
            if (ImGui.Button("Add Mixed Value", new Vector2(-1, 0)))
            {
                if (!string.IsNullOrWhiteSpace(editValueBuffer))
                {
                    selectedFilter.MixedValues.Add(new FilterValue { TypeName = editValueTypeBuffer, Value = editValueBuffer });
                    selectedFilter.UpdateCache();
                    editValueBuffer = "";
                    FilterManager.Save();
                }
            }
        }
        else
        {
            ImGui.InputTextWithHint("##ValueAdd", "Value...", ref editValueBuffer, 64);
            if (ImGui.Button("Add Value", new Vector2(-1, 0)))
            {
                if (!string.IsNullOrWhiteSpace(editValueBuffer))
                {
                    selectedFilter.Values.Add(editValueBuffer);
                    selectedFilter.UpdateCache();
                    editValueBuffer = "";
                    FilterManager.Save();
                }
            }
        }
        
        if (ImGui.Button("Save Changes", new Vector2(-1, 30))) FilterManager.Register(selectedFilter);
    }

    public static void DrawCreateFilter(ref string newFilterName, ref string newEnumTypeName, ref FilterMode newFilterMode, ref bool createAsMixed)
    {
        ImGui.Text("Create New Filter");
        ImGui.Separator();
        
        ImGui.InputText("Filter Name", ref newFilterName, 64);
        ImGui.Checkbox("Mixed Type Filter (Recommended)", ref createAsMixed);
        
        if (!createAsMixed)
            ImGui.InputText("Enum Type", ref newEnumTypeName, 128);
        
        int mode = (int)newFilterMode;
        if (ImGui.Combo("Mode", ref mode, "Whitelist\0Blacklist\0")) newFilterMode = (FilterMode)mode;
        
        if (ImGui.Button("Create Filter", new Vector2(-1, 40)))
        {
            if (!string.IsNullOrWhiteSpace(newFilterName))
            {
                Filter filter;
                if (createAsMixed)
                {
                    filter = new MixedFilter(newFilterName, newFilterMode);
                }
                else
                {
                    var type = FilterManager.ResolveTypeInternal(newEnumTypeName);
                    filter = type != null ? new Filter(newFilterName, type, Array.CreateInstance(type, 0), newFilterMode) 
                                         : new Filter { Name = newFilterName, EnumTypeName = newEnumTypeName, Mode = newFilterMode };
                }
                FilterManager.Register(filter);
            }
        }
    }
}
