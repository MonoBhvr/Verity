using Hexa.NET.ImGui;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public class HierarchyWindow : EditorWindow
{
    private readonly EditorApp _app;

    public HierarchyWindow(EditorApp app) : base("Hierarchy")
    {
        _app = app;
    }

    public override void OnGui()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null)
        {
            ImGui.Text("No active world");
            return;
        }

        ImGui.Text(world.Name);
        ImGui.Separator();

        foreach (var entity in world.RootEntities.ToArray())
            DrawEntityNode(entity);

        DrawRootDropZone(world);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
            EditorSelection.SelectedEntity = null;

        if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Delete) && EditorSelection.SelectedEntity != null)
        {
            DeleteEntity(EditorSelection.SelectedEntity, world);
        }

        if (ImGui.BeginPopupContextWindow())
        {
            if (ImGui.MenuItem("Create Empty"))
                world.CreateEntity("GameObject");

            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("Sprite"))
                {
                    var sprite = world.CreateEntity("Sprite");
                    sprite.AddComponent<SpriteRenderer>();
                }

                if (!WorldHasCamera(world))
                {
                    if (ImGui.MenuItem("Camera"))
                    {
                        var camera = world.CreateEntity("Camera");
                        camera.AddComponent<Camera>();
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }
    }

    private unsafe void DrawRootDropZone(World world)
    {
        ImGui.InvisibleButton("##rootdrop", new System.Numerics.Vector2(-1, ImGui.GetFrameHeight()));
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITY");
            if (payload.Handle != null && EditorSelection.DraggedEntity != null && EditorSelection.DraggedEntity.Transform.Parent != null)
            {
                SetParent(EditorSelection.DraggedEntity, null);
                EditorSelection.DraggedEntity = null;
            }
            ImGui.EndDragDropTarget();
        }
    }

    private static bool WorldHasCamera(World world)
    {
        foreach (var entity in world.RootEntities)
        {
            if (HasCameraRecursive(entity))
                return true;
        }
        return false;
    }

    private static bool HasCameraRecursive(Entity entity)
    {
        if (entity.GetComponent<Camera>() != null)
            return true;

        foreach (var child in entity.Transform.Children)
        {
            if (HasCameraRecursive(child.Owner))
                return true;
        }
        return false;
    }

    private unsafe void DrawEntityNode(Entity entity)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;

        if (entity.Transform.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        if (EditorSelection.SelectedEntity == entity)
            flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.PushID(entity.GetHashCode());
        bool opened = ImGui.TreeNodeEx(entity.Name, flags);

        if (ImGui.IsItemClicked())
            EditorSelection.SelectedEntity = entity;

        if (ImGui.BeginDragDropSource())
        {
            EditorSelection.DraggedEntity = entity;
            ImGui.SetDragDropPayload("HIERARCHY_ENTITY", null, 0);
            ImGui.Text(entity.Name);
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITY");
            if (payload.Handle != null && EditorSelection.DraggedEntity != null && EditorSelection.DraggedEntity != entity)
            {
                if (!IsDescendantOf(entity, EditorSelection.DraggedEntity))
                    SetParent(EditorSelection.DraggedEntity, entity);
                EditorSelection.DraggedEntity = null;
            }
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete"))
            {
                var world = WorldManager.ActiveWorld;
                if (world != null)
                    DeleteEntity(entity, world);
            }
            ImGui.EndPopup();
        }

        if (opened)
        {
            foreach (var child in entity.Transform.Children.ToArray())
                DrawEntityNode(child.Owner);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private static void DeleteEntity(Entity entity, World world)
    {
        if (EditorSelection.SelectedEntity == entity)
            EditorSelection.SelectedEntity = null;

        if (entity.Transform.Parent != null)
            entity.Transform.SetParent(null, preserveWorldPosition: false);

        world.DestroyEntity(entity);
        world.ProcessPendingDestroys();
    }

    private void SetParent(Entity child, Entity? newParent)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null) return;

        if (child.Transform.Parent == null)
            world.RemoveFromRoot(child);

        child.Transform.SetParent(newParent?.Transform, preserveWorldPosition: true);

        if (newParent == null)
            world.AddToRoot(child);
    }

    private static bool IsDescendantOf(Entity potentialDescendant, Entity ancestor)
    {
        var current = potentialDescendant.Transform.Parent;
        while (current != null)
        {
            if (current.Owner == ancestor)
                return true;
            current = current.Parent;
        }
        return false;
    }
}
