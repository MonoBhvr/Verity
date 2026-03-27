using Verity.Core;
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
    public static Sprite? SelectedSpriteAsset { get; set; }

    public static void SelectAsset(string? path)
    {
        SelectedAssetPath = path;
        SelectedSpriteAsset = null;
    }

    public static void SelectSpriteAsset(Sprite sprite)
    {
        SelectedAssetPath = AssetPathUtility.Normalize(sprite.Path);
        SelectedSpriteAsset = sprite;
    }

    public static void ClearAssetSelection()
    {
        SelectedAssetPath = null;
        SelectedSpriteAsset = null;
    }

    // Drag and Drop shared state
    public static string? DraggedAssetPath { get; set; }
    public static Sprite? DraggedSpriteAsset { get; set; }
    public static Entity? DraggedEntity { get; set; }

    public static void BeginAssetDrag(string? path)
    {
        DraggedAssetPath = path;
        DraggedSpriteAsset = null;
    }

    public static void BeginSpriteDrag(Sprite sprite)
    {
        DraggedAssetPath = AssetPathUtility.Normalize(sprite.Path);
        DraggedSpriteAsset = sprite;
    }

    public static void ClearAssetDrag()
    {
        DraggedAssetPath = null;
        DraggedSpriteAsset = null;
    }

    // Tilemap Editing state
    public static Verity.Core.World.TileBase? SelectedTile { get; set; }
    public static Verity.Core.World.TilemapEditor.Tool SelectedTool { get; set; } = Verity.Core.World.TilemapEditor.Tool.Brush;
    public static int TileBrushSize { get; set; } = 1;
    public static Verity.Core.World.TilemapEditor.BrushShape TileBrushShape { get; set; } = Verity.Core.World.TilemapEditor.BrushShape.Rectangle;

    // Polygon Editing state
    public static Component? EditingPolygonComponent { get; set; }
    public static bool IsEditingPolygon => EditingPolygonComponent != null;
}
