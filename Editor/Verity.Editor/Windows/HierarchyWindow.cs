using System.IO;
using System.Numerics;
using System.Text.Json.Nodes;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.Audio;
using Verity.Core.ECS;
using Verity.Core.Physics;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Editor.Windows;

public unsafe class HierarchyWindow : EditorWindow
{
    private readonly EditorApp _app;
    private readonly Dictionary<Guid, HashSet<Guid>> _blueprintOverrideCache = new();
    private Entity? _pendingClickSelectionEntity;
    private bool _pendingClickSelectionCtrl;
    private bool _pendingClickSelectionShift;

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

        if (_app.IsEditingBlueprint)
            DrawBlueprintModeHeader();

        DrawInputModal();
        HandleShortcuts(world);

        DrawHierarchy(world);
    }

    private void DrawBlueprintModeHeader()
    {
        string? lastWorldPath = _app.LastWorldAssetPath;
        bool canReturnToWorld = !string.IsNullOrWhiteSpace(lastWorldPath) && File.Exists(lastWorldPath);

        ImGui.TextDisabled(L10n.Tr("msg_blueprint_edit_mode"));
        if (!canReturnToWorld)
            ImGui.BeginDisabled();

        string worldLabel = canReturnToWorld
            ? Path.GetFileNameWithoutExtension(lastWorldPath!)
            : L10n.Tr("label_world");

        if (ImGui.Button(L10n.Tr("btn_back_to_world", worldLabel), new Vector2(-1, 0)) && canReturnToWorld)
        {
            if (_app.SaveActiveBlueprint())
                _app.GetWindow<ProjectWindow>()?.LoadWorldByPath(lastWorldPath!);
        }

        if (!canReturnToWorld)
        {
            ImGui.EndDisabled();
            ImGui.TextDisabled(L10n.Tr("msg_no_world_to_return"));
        }

        ImGui.Separator();
    }

    private void DrawHierarchy(World world)
    {
        _blueprintOverrideCache.Clear();
        bool showInsertionSlots = EditorSelection.DraggedEntity != null;

        if (!_app.IsEditingBlueprint)
            ImGui.Separator();

        var roots = world.RootEntities.ToArray();
        for (int i = 0; i < roots.Length; i++)
        {
            if (showInsertionSlots)
                DrawInsertionSlot(null, i, $"root-slot-{i}");
            var entity = roots[i];
            DrawEntityNode(entity);
        }

        if (showInsertionSlots)
            DrawInsertionSlot(null, roots.Length, "root-slot-end");

        DrawRootDropZone(world);

        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            ClearPendingClickSelection();

        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered())
            EditorSelection.ClearSelection();

        if (ImGui.BeginPopupContextWindow())
        {
            if (ImGui.MenuItem(L10n.Tr("ctx_create_empty"), "Ctrl+N"))
            {
                _app.RecordUndo();
                var ent = world.CreateEntity(L10n.Tr("CreationType_Entity"));
                _app.AttachToBlueprintDefaultParent(ent);
                EditorSelection.SelectedEntity = ent;
            }

            if (ImGui.BeginMenu(L10n.Tr("menu_create")))
            {
                if (ImGui.MenuItem(L10n.Tr("CreationType_Sprite")))
                {
                    CreateEntityPreset(world, L10n.Tr("CreationType_Sprite"), entity => entity.AddComponent<SpriteRenderer>());
                }

                if (ImGui.MenuItem(L10n.Tr("btn_add_tilemap_with_shape")))
                {
                    CreateEntityPreset(world, L10n.Tr("CreationType_Tilemap"), entity =>
                    {
                        entity.AddComponent<TilemapRenderer>();
                        entity.AddComponent<TilemapShape>();
                    });
                }

                if (ImGui.MenuItem(L10n.Tr("btn_add_tilemap_no_shape")))
                {
                    CreateEntityPreset(world, L10n.Tr("CreationType_Tilemap"), entity => entity.AddComponent<TilemapRenderer>());
                }

                if (ImGui.BeginMenu(L10n.Tr("menu_create_light")))
                {
                    if (ImGui.MenuItem(L10n.Tr("menu_create_light_spot")))
                    {
                        CreateEntityPreset(world, L10n.Tr("menu_create_light_spot"), entity =>
                        {
                            var light = entity.AddComponent<Light2D>();
                            light.Type = Light2DType.Spot;
                        });
                    }

                    if (ImGui.MenuItem(L10n.Tr("menu_create_light_directional")))
                    {
                        CreateEntityPreset(world, L10n.Tr("menu_create_light_directional"), entity =>
                        {
                            var light = entity.AddComponent<Light2D>();
                            light.Type = Light2DType.Direction;
                            light.Distance = 10.0f;
                            light.Spread = 0.0f;
                        });
                    }

                    if (ImGui.MenuItem(L10n.Tr("menu_create_light_global")))
                    {
                        CreateEntityPreset(world, L10n.Tr("menu_create_light_global"), entity =>
                        {
                            var light = entity.AddComponent<Light2D>();
                            light.Type = Light2DType.World;
                            light.AffectsCameraBackground = true;
                            light.CastShadows = false;
                        });
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.BeginMenu(L10n.Tr("menu_create_audio")))
                {
                    if (!WorldHasComponent<AudioListener>(world) && ImGui.MenuItem(L10n.Tr("type_AudioListener")))
                    {
                        CreateEntityPreset(world, L10n.Tr("type_AudioListener"), entity => entity.AddComponent<AudioListener>());
                    }

                    if (ImGui.MenuItem(L10n.Tr("type_AudioSource")))
                    {
                        CreateEntityPreset(world, L10n.Tr("type_AudioSource"), entity =>
                        {
                            var audioSource = entity.AddComponent<AudioSource>();
                            audioSource.PlayOnStart = false;
                        });
                    }

                    ImGui.EndMenu();
                }

                if (ImGui.MenuItem(L10n.Tr("CreationType_Camera")))
                {
                    CreateEntityPreset(world, L10n.Tr("CreationType_Camera"), entity =>
                    {
                        entity.AddComponent<Camera>();
                        entity.AddComponent<CameraOutput>();
                    });
                }

                if (ImGui.MenuItem(L10n.Tr("CreationType_Particle")))
                {
                    CreateEntityPreset(world, L10n.Tr("CreationType_Particle"), entity => entity.AddComponent<ParticleEmitter>());
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

    private void CreateEntityPreset(World world, string name, Action<Entity>? configure = null)
    {
        _app.RecordUndo();
        var entity = world.CreateEntity(name);
        configure?.Invoke(entity);
        _app.AttachToBlueprintDefaultParent(entity);
        EditorSelection.SelectedEntity = entity;
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_hierarchy"); }

    private static bool WorldHasComponent<T>(World world) where T : class
    {
        foreach (var entity in world.RootEntities)
        {
            if (HasComponentRecursive<T>(entity))
                return true;
        }
        return false;
    }

    private static bool HasComponentRecursive<T>(Entity entity) where T : class
    {
        if (entity.GetComponent<T>() != null)
            return true;

        foreach (var child in entity.Transform.Children)
        {
            if (HasComponentRecursive<T>(child.Owner))
                return true;
        }

        return false;
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
            var entity = world.CreateEntity(L10n.Tr("CreationType_Entity"));
            if (EditorSelection.SelectedEntity != null)
                SetParent(entity, EditorSelection.SelectedEntity.Transform.Parent?.Owner);
            _app.AttachToBlueprintDefaultParent(entity);
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
        
        // Sort by hierarchy depth to avoid duplicating children twice if parents are also selected
        var originals = EditorSelection.SelectedEntities
            .OrderBy(e => GetHierarchyDepth(e))
            .ToList();
            
        var clones = new List<Entity>();

        foreach (var original in originals)
        {
            // Skip if this entity is a descendant of another selected entity (it will be cloned by its ancestor)
            if (IsDescendantOfAny(original, originals)) continue;

            var clone = DuplicateEntityInternal(original, world, original.Transform.Parent?.Owner);
            if (clone != null) clones.Add(clone);
        }
        
        EditorSelection.ClearSelection();
        foreach (var clone in clones) EditorSelection.Select(clone, true);
    }

    private int GetHierarchyDepth(Entity e)
    {
        int depth = 0;
        var curr = e.Transform.Parent;
        while (curr != null) { depth++; curr = curr.Parent; }
        return depth;
    }

    private bool IsDescendantOfAny(Entity e, List<Entity> ancestors)
    {
        foreach (var a in ancestors)
        {
            if (e == a) continue;
            if (IsDescendantOf(e, a)) return true;
        }
        return false;
    }

    private Entity? DuplicateEntityInternal(Entity original, World world, Entity? targetParent)
    {
        Entity? rootClone = null;
        void CopyRecursive(Entity src, Entity? parent)
        {
            var clone = world.CreateEntity(src.Name + L10n.Tr("label_copy_suffix"));
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
                if (!clone.CanAddComponent(comp.GetType(), out _)) continue;
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
        _app.AttachToBlueprintDefaultParent(rootClone);
        return rootClone;
    }

    private static string? _copyBuffer;
    public void CopySelected()
    {
        if (EditorSelection.SelectedEntities.Count == 0) return;
        
        // Only copy top-level selected entities
        var toCopy = EditorSelection.SelectedEntities
            .Where(e => !IsDescendantOfAny(e, EditorSelection.SelectedEntities.ToList()))
            .ToList();

        var json = new System.Text.Json.Nodes.JsonArray();
        foreach (var ent in toCopy)
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

        // Paste as SIBLINGS of the current selection
        var targetParent = EditorSelection.SelectedEntity?.Transform.Parent?.Owner;
        
        var pasted = new List<Entity>();
        foreach (var node in array)
        {
            var ent = Verity.Core.Serialization.SceneSerializer.DeserializeEntity(world, node!.ToString(), _app.ScriptCompiler?.CompiledAssembly);
            if (ent != null)
            {
                if (targetParent != null) ent.Transform.SetParent(targetParent.Transform, false);
                _app.AttachToBlueprintDefaultParent(ent);
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
        if (remaining.X < 1) remaining.X = 1;
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

    private void DrawEntityNode(Entity entity)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;

        if (entity.Transform.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        if (EditorSelection.IsSelected(entity))
            flags |= ImGuiTreeNodeFlags.Selected;

        Vector4? hierarchyColor = GetBlueprintHierarchyColor(entity);

        ImGui.PushID(entity.GetHashCode());
        if (hierarchyColor.HasValue)
            ImGui.PushStyleColor(ImGuiCol.Text, hierarchyColor.Value);
        bool opened = ImGui.TreeNodeEx(entity.Name, flags);
        if (hierarchyColor.HasValue)
            ImGui.PopStyleColor();

        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            var io = ImGui.GetIO();
            _pendingClickSelectionEntity = entity;
            _pendingClickSelectionCtrl = io.KeyCtrl;
            _pendingClickSelectionShift = io.KeyShift;
        }

        if (_pendingClickSelectionEntity == entity && ImGui.IsItemHovered() && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        {
            ApplyClickSelection(entity, _pendingClickSelectionCtrl, _pendingClickSelectionShift);
            ClearPendingClickSelection();
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
                    MoveEntities(EditorSelection.SelectedEntities.ToArray(), entity, entity.Transform.Children.Count);
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
            var children = entity.Transform.Children.ToArray();
            bool showInsertionSlots = EditorSelection.DraggedEntity != null;
            for (int i = 0; i < children.Length; i++)
            {
                if (showInsertionSlots)
                    DrawInsertionSlot(entity, i, $"child-slot-{entity.Id}-{i}");
                DrawEntityNode(children[i].Owner);
            }

            if (showInsertionSlots)
                DrawInsertionSlot(entity, children.Length, $"child-slot-{entity.Id}-end");
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private void ApplyClickSelection(Entity entity, bool ctrl, bool shift)
    {
        if (ctrl)
        {
            if (EditorSelection.IsSelected(entity)) EditorSelection.Deselect(entity);
            else EditorSelection.Select(entity, true);
        }
        else if (shift && EditorSelection.SelectedEntity != null)
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

    private void ClearPendingClickSelection()
    {
        _pendingClickSelectionEntity = null;
        _pendingClickSelectionCtrl = false;
        _pendingClickSelectionShift = false;
    }

    private Vector4? GetBlueprintHierarchyColor(Entity entity)
    {
        if (!entity.IsBlueprintInstance)
            return null;

        return IsBlueprintEntityOverridden(entity)
            ? new Vector4(0.35f, 0.65f, 1.0f, 1.0f)
            : new Vector4(0.35f, 0.9f, 1.0f, 1.0f);
    }

    private bool IsBlueprintEntityOverridden(Entity entity)
    {
        if (!entity.BlueprintSourceEntityId.HasValue)
            return false;

        Entity? root = FindBlueprintInstanceRoot(entity);
        if (root == null)
            return false;

        if (!_blueprintOverrideCache.TryGetValue(root.Id, out HashSet<Guid>? overriddenSourceIds))
        {
            overriddenSourceIds = [];
            foreach (JsonNode? node in Verity.Core.Serialization.SceneSerializer.CaptureBlueprintInstanceOverrides(root))
            {
                if (Guid.TryParse((string?)node?["SourceId"], out Guid sourceId))
                    overriddenSourceIds.Add(sourceId);
            }

            _blueprintOverrideCache[root.Id] = overriddenSourceIds;
        }

        return overriddenSourceIds.Contains(entity.BlueprintSourceEntityId.Value);
    }

    private static Entity? FindBlueprintInstanceRoot(Entity entity)
    {
        Entity? current = entity;
        while (current != null)
        {
            if (current.IsBlueprintInstanceRoot)
                return current;

            current = current.Transform.Parent?.Owner;
        }

        return null;
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
        SetParent(child, newParent, null);
    }

    private void SetParent(Entity child, Entity? newParent, int? siblingIndex)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null) return;

        if (newParent == null)
        {
            if (child.Transform.Parent != null)
            {
                child.Transform.SetParent(null, preserveWorldPosition: true);
            }

            if (siblingIndex.HasValue)
                world.AddToRoot(child, siblingIndex.Value);
            else
                world.AddToRoot(child);
            return;
        }

        child.Transform.SetParent(newParent.Transform, preserveWorldPosition: true, siblingIndex ?? newParent.Transform.Children.Count);
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

    private void MoveEntities(IReadOnlyList<Entity> entities, Entity? newParent, int insertIndex)
    {
        var world = WorldManager.ActiveWorld;
        if (world == null)
            return;

        var orderedEntities = GetEntitiesInHierarchyOrder(world, entities)
            .Where(ent => CanMoveEntity(ent, newParent))
            .ToList();

        if (orderedEntities.Count == 0)
            return;

        _app.RecordUndo();

        int nextIndex = Math.Max(0, insertIndex);
        foreach (var entity in orderedEntities)
        {
            Entity? currentParent = entity.Transform.Parent?.Owner;
            bool sameParent = currentParent == newParent;
            int currentIndex = entity.Transform.GetSiblingIndex();
            if (sameParent && currentIndex >= 0 && currentIndex < nextIndex)
                nextIndex--;

            SetParent(entity, newParent, nextIndex);
            nextIndex++;
        }

        EditorSelection.DraggedEntity = null;
    }

    private void DrawInsertionSlot(Entity? parent, int insertIndex, string id)
    {
        ImGui.PushID(id);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0f));
        ImGui.InvisibleButton("##insert", new Vector2(-1, 3f));

        if (ImGui.IsItemHovered() && EditorSelection.DraggedEntity != null)
        {
            Vector2 min = ImGui.GetItemRectMin();
            Vector2 max = ImGui.GetItemRectMax();
            var drawList = ImGui.GetWindowDrawList();
            uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.7f, 1f, 0.9f));
            drawList.AddLine(new Vector2(min.X + 6f, (min.Y + max.Y) * 0.5f), new Vector2(max.X - 6f, (min.Y + max.Y) * 0.5f), color, 2f);
        }

        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("HIERARCHY_ENTITIES");
                if (payload.Handle != null)
                    MoveEntities(EditorSelection.SelectedEntities.ToArray(), parent, insertIndex);
            }

            ImGui.EndDragDropTarget();
        }

        ImGui.PopStyleVar();
        ImGui.PopID();
    }

    private static List<Entity> GetEntitiesInHierarchyOrder(World world, IReadOnlyList<Entity> entities)
    {
        var selected = entities.ToHashSet();
        return world.GetAllEntities().Where(selected.Contains).ToList();
    }

    private static bool CanMoveEntity(Entity entity, Entity? newParent)
    {
        if (newParent == null)
            return true;

        return entity != newParent && !IsDescendantOf(newParent, entity);
    }

}
