using Hexa.NET.ImGui;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public unsafe class HierarchyWindow : EditorWindow
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

        DrawInputModal();
        HandleShortcuts(world);

        ImGui.Text(world.Name);
        ImGui.Separator();

        foreach (var entity in world.RootEntities.ToArray())
            DrawEntityNode(entity);

        DrawRootDropZone(world);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
            EditorSelection.SelectedEntity = null;

        if (ImGui.BeginPopupContextWindow())
        {
            if (ImGui.MenuItem("Create Empty", "Ctrl+N"))
            {
                _app.RecordUndo();
                world.CreateEntity("GameObject");
            }

            if (ImGui.BeginMenu("Create"))
            {
                if (ImGui.MenuItem("Sprite"))
                {
                    _app.RecordUndo();
                    var sprite = world.CreateEntity("Sprite");
                    sprite.AddComponent<SpriteRenderer>();
                }

                if (!WorldHasCamera(world))
                {
                    if (ImGui.MenuItem("Camera"))
                    {
                        _app.RecordUndo();
                        var camera = world.CreateEntity("Camera");
                        camera.AddComponent<Camera>();
                    }
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }
    }

    private void HandleShortcuts(World world)
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;
        var io = ImGui.GetIO();
        if (io.WantCaptureKeyboard) return;

        bool ctrl = io.KeyCtrl;

        // Ctrl + N: Create Empty
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.N))
        {
            _app.RecordUndo();
            var entity = world.CreateEntity("GameObject");
            if (EditorSelection.SelectedEntity != null)
                SetParent(entity, EditorSelection.SelectedEntity);
            EditorSelection.SelectedEntity = entity;
        }

        // Delete: Delete Entity
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && EditorSelection.SelectedEntity != null)
        {
            _app.RecordUndo();
            DeleteEntity(EditorSelection.SelectedEntity, world);
        }

        // F: Focus selected entity
        if (ImGui.IsKeyPressed(ImGuiKey.F) && EditorSelection.SelectedEntity != null)
        {
            _app.WorldCamera.Position = EditorSelection.SelectedEntity.Transform.WorldPosition;
        }

        // F2: Rename Entity
        if (ImGui.IsKeyPressed(ImGuiKey.F2) && EditorSelection.SelectedEntity != null)
        {
            OpenRenamePopup(EditorSelection.SelectedEntity);
        }

        // Ctrl + D: Duplicate Entity
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.D) && EditorSelection.SelectedEntity != null)
        {
            _app.RecordUndo();
            DuplicateEntity(EditorSelection.SelectedEntity, world);
        }
    }

    private void DuplicateEntity(Entity original, World world)
    {
        void CopyRecursive(Entity src, Entity? targetParent)
        {
            var clone = world.CreateEntity(src.Name + " (Copy)");
            clone.Transform.Position = src.Transform.Position;
            clone.Transform.Rotation = src.Transform.Rotation;
            clone.Transform.Scale = src.Transform.Scale;
            
            if (targetParent != null)
                clone.Transform.SetParent(targetParent.Transform, preserveWorldPosition: true);

            foreach (var comp in src.GetAllComponents())
            {
                if (comp is Transform) continue;
                var cloneComp = clone.AddComponent(comp.GetType());
                
                // Copy properties via reflection
                foreach (var prop in comp.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    if (prop.CanRead && prop.CanWrite && prop.DeclaringType != typeof(Component))
                    {
                        try { prop.SetValue(cloneComp, prop.GetValue(comp)); } catch { }
                    }
                }
                foreach (var field in comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                {
                    try { field.SetValue(cloneComp, field.GetValue(comp)); } catch { }
                }
            }

            foreach (var child in src.Transform.Children.ToArray())
                CopyRecursive(child.Owner, clone);
        }

        CopyRecursive(original, original.Transform.Parent?.Owner);
    }

    private string _renameBuffer = "";
    private Entity? _renameTarget;
    private bool _shouldOpenRenamePopup;

    private void OpenRenamePopup(Entity entity)
    {
        _renameTarget = entity;
        _renameBuffer = entity.Name;
        _shouldOpenRenamePopup = true;
    }

    private void DrawInputModal()
    {
        if (_shouldOpenRenamePopup) { ImGui.OpenPopup("RenameEntityModal"); _shouldOpenRenamePopup = false; }
        
        var viewport = ImGui.GetMainViewport();
        var center = new System.Numerics.Vector2(viewport.Pos.X + viewport.Size.X * 0.5f, viewport.Pos.Y + viewport.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new System.Numerics.Vector2(0.5f, 0.5f));

        if (ImGui.BeginPopupModal("RenameEntityModal", null, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text("Rename Entity");
            ImGui.Separator();
            if (ImGui.IsWindowAppearing()) { 
                ImGui.SetKeyboardFocusHere();
                _app.BeginUndoAction(); 
            }
            
            ImGui.InputText("Name", ref _renameBuffer, 64);
            
            var btnSize = new System.Numerics.Vector2(120, 0);
            if (ImGui.Button("OK", btnSize) || ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                if (_renameTarget != null && !string.IsNullOrWhiteSpace(_renameBuffer))
                {
                    _renameTarget.Name = _renameBuffer;
                    _app.EndUndoAction();
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", btnSize) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _app.EndUndoAction(); // Close without changes
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawRootDropZone(World world)
    {
        ImGui.InvisibleButton("##rootdrop", new System.Numerics.Vector2(-1, ImGui.GetFrameHeight()));
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITY");
                if (payload.Handle != null && EditorSelection.DraggedEntity != null && EditorSelection.DraggedEntity.Transform.Parent != null)
                {
                    _app.RecordUndo();
                    SetParent(EditorSelection.DraggedEntity, null);
                    EditorSelection.DraggedEntity = null;
                }

                var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint"))
                {
                    _app.RecordUndo();
                    _app.InstantiateBlueprint(EditorSelection.DraggedAssetPath, null, null);
                    EditorSelection.DraggedAssetPath = null;
                }
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

    private void DrawEntityNode(Entity entity)
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
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITY");
                if (payload.Handle != null && EditorSelection.DraggedEntity != null && EditorSelection.DraggedEntity != entity)
                {
                    if (!IsDescendantOf(entity, EditorSelection.DraggedEntity))
                    {
                        _app.RecordUndo();
                        SetParent(EditorSelection.DraggedEntity, entity);
                    }
                    EditorSelection.DraggedEntity = null;
                }

                var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint"))
                {
                    _app.RecordUndo();
                    _app.InstantiateBlueprint(EditorSelection.DraggedAssetPath, null, entity);
                    EditorSelection.DraggedAssetPath = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Save as Blueprint"))
            {
                _app.SaveEntityAsBlueprint(entity);
            }
            ImGui.Separator();
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
        if (entity.GetComponent<Camera>() != null)
        {
            Verity.Core.Debug.LogWarning($"[Hierarchy] Cannot delete entity '{entity.Name}' because it has a Camera component.");
            return;
        }

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
