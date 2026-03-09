using Verity.Core.ECS;

namespace Verity.Editor;

public static class EditorSelection
{
    public static Entity? SelectedEntity { get; set; }
    public static string? SelectedAssetPath { get; set; }

    // Drag and Drop shared state
    public static string? DraggedAssetPath { get; set; }
    public static Entity? DraggedEntity { get; set; }
}
