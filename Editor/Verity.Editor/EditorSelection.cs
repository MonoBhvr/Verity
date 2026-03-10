using Verity.Core.ECS;

namespace Verity.Editor;

public static class EditorSelection
{
    private static readonly HashSet<Entity> _selectedEntities = [];
    
    public static Entity? SelectedEntity 
    { 
        get => _selectedEntities.FirstOrDefault();
        set 
        {
            _selectedEntities.Clear();
            if (value != null) _selectedEntities.Add(value);
        }
    }

    public static IReadOnlySet<Entity> SelectedEntities => _selectedEntities;

    public static void Select(Entity entity, bool add = false)
    {
        if (!add) _selectedEntities.Clear();
        if (entity != null) _selectedEntities.Add(entity);
    }

    public static void Deselect(Entity entity) => _selectedEntities.Remove(entity);
    public static void ClearSelection() => _selectedEntities.Clear();
    public static bool IsSelected(Entity entity) => _selectedEntities.Contains(entity);

    public static string? SelectedAssetPath { get; set; }

    // Drag and Drop shared state
    public static string? DraggedAssetPath { get; set; }
    public static Entity? DraggedEntity { get; set; }

    // Collider Editing state
    public static bool IsEditingCollider { get; set; }
}
