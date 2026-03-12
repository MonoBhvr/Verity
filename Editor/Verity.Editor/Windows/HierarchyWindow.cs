using Hexa.NET.ImGui;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public unsafe class HierarchyWindow : EditorWindow
{
    private readonly EditorApp _app;

    public HierarchyWindow(EditorApp app) : base(L10n.Tr("window_hierarchy"))
    {
        _app = app;
    }
    public override void OnGui()
    {
        var world = WorldManager.ActiveWorld;
        if (world == null)
        {
            ImGui.Text(L10n.Tr("msg_no_active_world"));
            return;
        }

        DrawInputModal();
        HandleShortcuts(world);

        DrawHierarchy(world);
    }

    private void DrawHierarchy(World world)
    {
        ImGui.Separator();

        foreach (var entity in world.RootEntities.ToArray())
            DrawEntityNode(entity);

        DrawRootDropZone(world);

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
            EditorSelection.ClearSelection();

        if (ImGui.BeginPopupContextWindow())
        {
            if (ImGui.MenuItem(L10n.Tr("ctx_create_empty"), "Ctrl+N"))
            {
                _app.RecordUndo();
                var ent = world.CreateEntity(L10n.Tr("CreationType_Entity"));
                EditorSelection.SelectedEntity = ent;
            }

            if (ImGui.BeginMenu(L10n.Tr("menu_create")))
            {
                if (ImGui.MenuItem(L10n.Tr("CreationType_Sprite")))
                {
                    _app.RecordUndo();
                    var sprite = world.CreateEntity(L10n.Tr("CreationType_Sprite"));
                    sprite.AddComponent<SpriteRenderer>();
                    EditorSelection.SelectedEntity = sprite;
                }

                if (!WorldHasCamera(world))
                {
                    if (ImGui.MenuItem(L10n.Tr("CreationType_Camera")))
                    {
                        _app.RecordUndo();
                        var camera = world.CreateEntity(L10n.Tr("CreationType_Camera"));
                        camera.AddComponent<Camera>();
                        EditorSelection.SelectedEntity = camera;
                    }
                }

                ImGui.EndMenu();
            }

            if (EditorSelection.SelectedEntities.Count > 0)
            {
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("ctx_copy"), "Ctrl+C")) CopySelected();
                if (ImGui.MenuItem(L10n.Tr("ctx_paste"), "Ctrl+V")) Paste(world);
                if (ImGui.MenuItem(L10n.Tr("ctx_duplicate"), "Ctrl+D")) DuplicateSelected(world);
                if (ImGui.MenuItem(L10n.Tr("ctx_delete"), "Del")) DeleteSelected(world);
            }
            else if (CanPaste())
            {
                ImGui.Separator();
                if (ImGui.MenuItem(L10n.Tr("ctx_paste"), "Ctrl+V")) Paste(world);
            }

            ImGui.EndPopup();
        }
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_hierarchy"); }

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
            var entity = world.CreateEntity(L10n.Tr("CreationType_Entity"));
            if (EditorSelection.SelectedEntity != null)
                SetParent(entity, EditorSelection.SelectedEntity);
            EditorSelection.SelectedEntity = entity;
        }

        // Delete: Delete Entity
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && EditorSelection.SelectedEntities.Count > 0)
        {
            DeleteSelected(world);
        }

        // F: Focus selected entity
        if (ImGui.IsKeyPressed(ImGuiKey.F) && EditorSelection.SelectedEntity != null)
        {
            _app.FocusEntity(EditorSelection.SelectedEntity);
        }

        // F2: Rename Entity
        if (ImGui.IsKeyPressed(ImGuiKey.F2) && EditorSelection.SelectedEntity != null)
        {
            OpenRenamePopup(EditorSelection.SelectedEntity);
        }

        // Ctrl + D: Duplicate Entity
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.D) && EditorSelection.SelectedEntities.Count > 0)
        {
            DuplicateSelected(world);
        }

        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.C)) CopySelected();
        if (ctrl && ImGui.IsKeyPressed(ImGuiKey.V)) Paste(world);
    }

    public void DeleteSelected(World world)
    {
        _app.RecordUndo();
        var toDelete = EditorSelection.SelectedEntities.ToList();
        EditorSelection.ClearSelection();
        foreach (var entity in toDelete)
        {
            DeleteEntity(entity, world);
        }
    }

    public void DuplicateSelected(World world)
    {
        _app.RecordUndo();
        var originals = EditorSelection.SelectedEntities.ToList();
        var clones = new List<Entity>();
        foreach (var original in originals)
        {
            var clone = DuplicateEntityInternal(original, world, original.Transform.Parent?.Owner);
            if (clone != null) clones.Add(clone);
        }
        EditorSelection.ClearSelection();
        foreach (var clone in clones) EditorSelection.Select(clone, true);
    }

    private Entity? DuplicateEntityInternal(Entity original, World world, Entity? targetParent)
    {
        Entity? rootClone = null;
        void CopyRecursive(Entity src, Entity? parent)
        {
            var clone = world.CreateEntity(src.Name + " (Copy)");
            if (rootClone == null) rootClone = clone;

            // 1. Set Parent First
            if (parent != null)
                clone.Transform.SetParent(parent.Transform, preserveWorldPosition: false);

            // 2. Set Local Transforms
            clone.Transform.Position = src.Transform.Position;
            clone.Transform.Rotation = src.Transform.Rotation;
            clone.Transform.Scale = src.Transform.Scale;

            foreach (var comp in src.GetAllComponents())
            {
                if (comp is Transform) continue;
                var cloneComp = clone.AddComponent(comp.GetType());
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

        CopyRecursive(original, targetParent);
        return rootClone;
    }

    private static string? _copyBuffer;
    public void CopySelected()
    {
        if (EditorSelection.SelectedEntities.Count == 0) return;
        var json = new System.Text.Json.Nodes.JsonArray();
        foreach (var ent in EditorSelection.SelectedEntities)
            json.Add(System.Text.Json.Nodes.JsonNode.Parse(Verity.Core.Serialization.SceneSerializer.SerializeEntity(ent)));
        _copyBuffer = json.ToString();
    }

    public bool CanPaste() => !string.IsNullOrEmpty(_copyBuffer);

    public void Paste(World world)
    {
        if (string.IsNullOrEmpty(_copyBuffer)) return;
        _app.RecordUndo();
        var array = System.Text.Json.Nodes.JsonNode.Parse(_copyBuffer)?.AsArray();
        if (array == null) return;

        var targetParent = EditorSelection.SelectedEntity;
        var pasted = new List<Entity>();
        foreach (var node in array)
        {
            var ent = Verity.Core.Serialization.SceneSerializer.DeserializeEntity(world, node!.ToString(), _app.ScriptCompiler?.CompiledAssembly);
            if (ent != null)
            {
                if (targetParent != null) ent.Transform.SetParent(targetParent.Transform, false);
                pasted.Add(ent);
            }
        }
        EditorSelection.ClearSelection();
        foreach (var ent in pasted) EditorSelection.Select(ent, true);
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
            ImGui.Text(L10n.Tr("msg_rename_entity"));
            ImGui.Separator();
            if (ImGui.IsWindowAppearing()) { 
                ImGui.SetKeyboardFocusHere();
                _app.BeginUndoAction(); 
            }
            
            ImGui.InputText(L10n.Tr("label_name"), ref _renameBuffer, 64);
            
            var btnSize = new System.Numerics.Vector2(120, 0);
            if (ImGui.Button(L10n.Tr("btn_ok"), btnSize) || ImGui.IsKeyPressed(ImGuiKey.Enter))
            {
                if (_renameTarget != null && !string.IsNullOrWhiteSpace(_renameBuffer))
                {
                    _renameTarget.Name = _renameBuffer;
                    _app.EndUndoAction();
                }
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(L10n.Tr("btn_cancel"), btnSize) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            {
                _app.EndUndoAction(); // Close without changes
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private void DrawRootDropZone(World world)
    {
        var remaining = ImGui.GetContentRegionAvail();
        if (remaining.Y < 50) remaining.Y = 50;
        ImGui.InvisibleButton("##rootdrop", remaining);
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
                if (payload.Handle != null)
                {
                    _app.RecordUndo();
                    foreach (var ent in EditorSelection.SelectedEntities.ToArray())
                    {
                        if (ent.Transform.Parent != null)
                            SetParent(ent, null);
                    }
                    EditorSelection.DraggedEntity = null;
                }

                var assetPayload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (assetPayload.Handle != null && EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint"))
                {
                    _app.RecordUndo();
                    _app.InstantiateBlueprint(EditorSelection.DraggedAssetPath, null, null);
                    EditorSelection.DraggedAssetPath = null;
                }
                else if (EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint"))
                {
                    ImGui.SetTooltip(L10n.Tr("msg_add_blueprint_to_world", System.IO.Path.GetFileNameWithoutExtension(EditorSelection.DraggedAssetPath)));
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

        if (EditorSelection.IsSelected(entity))
            flags |= ImGuiTreeNodeFlags.Selected;

        ImGui.PushID(entity.GetHashCode());
        bool opened = ImGui.TreeNodeEx(entity.Name, flags);

        if (ImGui.IsItemClicked())
        {
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
            {
                if (EditorSelection.IsSelected(entity)) EditorSelection.Deselect(entity);
                else EditorSelection.Select(entity, true);
            }
            else if (io.KeyShift && EditorSelection.SelectedEntity != null)
            {
                var world = WorldManager.ActiveWorld;
                if (world != null)
                {
                    var all = world.GetAllEntities().ToList();
                    int start = all.IndexOf(EditorSelection.SelectedEntity);
                    int end = all.IndexOf(entity);
                    if (start != -1 && end != -1)
                    {
                        for (int i = Math.Min(start, end); i <= Math.Max(start, end); i++)
                            EditorSelection.Select(all[i], true);
                    }
                }
            }
            else
            {
                EditorSelection.SelectedEntity = entity;
            }
        }

        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            _app.FocusEntity(entity);
        }

        if (ImGui.BeginDragDropSource())
        {
            if (!EditorSelection.IsSelected(entity)) EditorSelection.SelectedEntity = entity;
            EditorSelection.DraggedEntity = entity;
            
            ImGui.SetDragDropPayload("HIERARCHY_ENTITIES", null, 0);
            ImGui.Text(L10n.Tr("msg_moving_entities", EditorSelection.SelectedEntities.Count));
            ImGui.EndDragDropSource();
        }

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
                if (payload.Handle != null)
                {
                    _app.RecordUndo();
                    foreach (var ent in EditorSelection.SelectedEntities.ToArray())
                    {
                        if (ent != entity && !IsDescendantOf(entity, ent))
                            SetParent(ent, entity);
                    }
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
            if (ImGui.MenuItem(L10n.Tr("ctx_save_as_blueprint")))
            {
                foreach (var ent in EditorSelection.SelectedEntities) _app.SaveEntityAsBlueprint(ent);
            }
            ImGui.Separator();
            if (ImGui.MenuItem(L10n.Tr("ctx_duplicate"), "Ctrl+D")) DuplicateSelected(WorldManager.ActiveWorld!);
            if (ImGui.MenuItem(L10n.Tr("ctx_delete"), "Del")) DeleteSelected(WorldManager.ActiveWorld!);
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
