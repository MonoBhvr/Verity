using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core;
using Verity.Graphics;
using System.Diagnostics;
using Verity.Core.Physics;

namespace Verity.Editor.Windows;

public unsafe class WorldViewWindow : EditorWindow
{
    public enum GizmoTool { Move, Scale, Rotate, Rect }
    private enum ModalMode { None, Create, Rename }
    private enum CreationType { Script, World, Folder }

    private readonly EditorApp _app;
    private bool _isDragging;
    private bool _isBoxSelecting;
    private Vector2 _boxSelectionStart;
    private bool _gridSnap;
    private float _snapSize = 1.0f;
    private GizmoTool _activeTool = GizmoTool.Move;

    private const float HandleScreenSize = 10f;
    private const float DefaultEntitySize = 1f;

    private int _activeHandle = -1;
    private Vector2 _dragStartWorld;
    private List<(Entity ent, Vector2 startWorldPos, Vector2 startWorldScale, float startWorldRot)> _draggedEntities = [];

    private string _inputBuffer = "";
    private string? _targetPath;
    private ModalMode _activeMode = ModalMode.None;
    private CreationType _creationType = CreationType.Folder;
    private bool _shouldOpenPopup = false;

    private static readonly Verity.Core.Color SelectionColor = new(51, 204, 255, 255);
    private static readonly Verity.Core.Color HandleColor = Verity.Core.Color.White;
    private static readonly Verity.Core.Color HandleFillColor = new(51, 204, 255, 204);
    private static readonly Verity.Core.Color RotateHandleColor = new(102, 255, 102, 255);

    private Entity? _previewEntity;
    private string? _previewPath;

    public WorldViewWindow(EditorApp app) : base(L10n.Tr("window_worldview")) { _app = app; }

    public override void OnGui()
    {
        HandleShortcuts();
        DrawToolbar();

        if (_shouldOpenPopup) {
            ImGui.OpenPopup("WorldActionModal");
            _shouldOpenPopup = false;
        }
        DrawInputModal();

        var contentSize = ImGui.GetContentRegionAvail();
        if (contentSize.X <= 0 || contentSize.Y <= 0) return;

        _app.WorldCamera.SetViewportSize((int)contentSize.X, (int)contentSize.Y);
        _app.RenderPipeline.EnsureFbo((int)contentSize.X, (int)contentSize.Y);

        var world = WorldManager.ActiveWorld;
        if (world != null) {
            bool originalFixed = _app.WorldCamera.FixedAspectRatio;
            _app.WorldCamera.FixedAspectRatio = false;
            var originalColor = _app.WorldCamera.BackgroundColor;
            _app.WorldCamera.BackgroundColor = _app.ProjectSettings.EditorWorldBackgroundColor;

            var imgMin = ImGui.GetCursorScreenPos();
            bool isHovered = ImGui.IsMouseHoveringRect(imgMin, imgMin + contentSize);
            UpdatePreviewEntity(world, isHovered, imgMin);

            _app.RenderPipeline.RenderWorld(world, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            DrawGrid(_app.RenderPipeline.WorldFbo);
            RenderEditorGizmos(world);
            
            _app.WorldCamera.BackgroundColor = originalColor;
            _app.WorldCamera.FixedAspectRatio = originalFixed;
        }

        var colorTex = _app.RenderPipeline.WorldColorTexture;
        if (colorTex is OpenGlTexture glTex) {
            unsafe {
                var texRef = new ImTextureRef(null, new ImTextureID((nint)glTex.Id));
                ImGui.Image(texRef, contentSize, new Vector2(0, 1), new Vector2(1, 0));
            }
            var imgMin = ImGui.GetItemRectMin();
            var imgSize = ImGui.GetItemRectSize();
            HandleWorldInteraction(world, imgMin, imgSize, ImGui.IsItemHovered());
        }
        HandleCameraControls();
    }

    public override void RefreshTitle() { Title = L10n.Tr("window_worldview"); }

    private void UpdatePreviewEntity(World world, bool hovered, Vector2 imgMin)
    {
        string? draggedPath = EditorSelection.DraggedAssetPath;
        bool isDraggingBlueprint = draggedPath != null && draggedPath.EndsWith(".blueprint");
        bool isDraggingImage = draggedPath != null && (draggedPath.EndsWith(".png") || draggedPath.EndsWith(".jpg") || draggedPath.EndsWith(".jpeg"));

        if ((isDraggingBlueprint || isDraggingImage) && hovered)
        {
            var io = ImGui.GetIO();
            var worldMouse = _app.WorldCamera.ScreenToWorld(io.MousePos - imgMin);
            var pos = _gridSnap ? SnapToGrid(worldMouse) : worldMouse;

            if (_previewEntity == null || _previewPath != draggedPath)
            {
                if (_previewEntity != null) world.DestroyEntity(_previewEntity);
                
                if (isDraggingBlueprint)
                {
                    _previewEntity = _app.InstantiateBlueprint(draggedPath!);
                }
                else if (isDraggingImage && draggedPath != null)
                {
                    _previewEntity = world.CreateEntity(Path.GetFileNameWithoutExtension(draggedPath) ?? "New Entity");
                    var sr = _previewEntity.AddComponent<SpriteRenderer>();
                    sr.Sprite = (Sprite)draggedPath!;
                    
                    // Set texture and adjust size for aspect ratio
                    var tex = _app.TextureManager.Load(draggedPath!);
                    sr.Texture = tex;
                    if (tex != null)
                    {
                        float aspect = (float)tex.Width / tex.Height;
                        if (aspect >= 1.0f) sr.Size = new Vector2(1.0f, 1.0f / aspect);
                        else sr.Size = new Vector2(aspect, 1.0f);
                    }
                }

                _previewPath = draggedPath;
                if (_previewEntity != null) SetAlphaRecursive(_previewEntity, 0.5f);
            }

            if (_previewEntity != null)
            {
                _previewEntity.Transform.Position = pos;
            }
        }
        else if (_previewEntity != null)
        {
            world.DestroyEntity(_previewEntity);
            world.ProcessPendingDestroys();
            _previewEntity = null;
            _previewPath = null;
        }
    }

    private void SetAlphaRecursive(Entity e, float alpha)
    {
        var sr = e.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            var c = sr.Color;
            sr.Color = new Verity.Core.Color(c.R, c.G, c.B, alpha);
        }
        foreach (var child in e.Transform.Children) SetAlphaRecursive(child.Owner, alpha);
    }

    private void DrawGrid(Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        var cam = _app.WorldCamera;
        float hH = cam.VisibleHalfHeight; float hW = cam.VisibleHalfWidth;
        Vector2 camPos = cam.Position;
        float left = camPos.X - hW; float right = camPos.X + hW;
        float top = camPos.Y + hH; float bottom = camPos.Y - hH;
        float pixel = GetWorldPixelSize();

        float minPixels = 50f;
        float log = MathF.Log10(pixel * minPixels);
        float floorLog = MathF.Floor(log);
        float baseStep = MathF.Pow(10, floorLog);

        DrawSpatialGridLines(baseStep / 10f, 0.12f, baseStep * 1.5f, cam, fbo, left, right, top, bottom, pixel);
        DrawSpatialGridLines(baseStep,       0.20f, baseStep * 15.0f, cam, fbo, left, right, top, bottom, pixel);
        DrawSpatialGridLines(baseStep * 10f,  0.35f, baseStep * 150.0f, cam, fbo, left, right, top, bottom, pixel);

        _app.RenderPipeline.RenderGizmoLine(new Vector2(0, bottom), new Vector2(0, top), pixel * 2.0f, Verity.Core.Color.FromRgba(100, 100, 255, 160), cam, fbo);
        _app.RenderPipeline.RenderGizmoLine(new Vector2(left, 0), new Vector2(right, 0), pixel * 2.0f, Verity.Core.Color.FromRgba(255, 100, 100, 160), cam, fbo);
    }

    private void DrawSpatialGridLines(float step, float maxAlpha, float visibleRadius, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo, float left, float right, float top, float bottom, float pixel)
    {
        float screenDist = step / pixel;
        if (screenDist < 10f) return;
        float zoomFade = Math.Clamp((screenDist - 10f) / 30f, 0f, 1f);
        float baseAlpha = maxAlpha * zoomFade;
        if (baseAlpha < 0.01f) return;
        Vector2 camPos = cam.Position;
        float startX = MathF.Floor(left / step) * step;
        for (float x = startX; x <= right + step; x += step) {
            if (Math.Abs(x) < 0.001f) continue;
            float dist = Math.Abs(x - camPos.X);
            float spatialAlpha = baseAlpha * Math.Clamp(1.0f - (dist / visibleRadius), 0f, 1f);
            if (spatialAlpha < 0.01f) continue;
            _app.RenderPipeline.RenderGizmoLine(new Vector2(x, bottom), new Vector2(x, top), pixel, new Verity.Core.Color(1f, 1f, 1f, spatialAlpha), cam, fbo);
        }
        float startY = MathF.Floor(bottom / step) * step;
        for (float y = startY; y <= top + step; y += step) {
            if (Math.Abs(y) < 0.001f) continue;
            float dist = Math.Abs(y - camPos.Y);
            float spatialAlpha = baseAlpha * Math.Clamp(1.0f - (dist / visibleRadius), 0f, 1f);
            if (spatialAlpha < 0.01f) continue;
            _app.RenderPipeline.RenderGizmoLine(new Vector2(left, y), new Vector2(right, y), pixel, new Verity.Core.Color(1f, 1f, 1f, spatialAlpha), cam, fbo);
        }
    }

    private void DrawToolbar()
    {
        void ToolButton(string label, GizmoTool tool, ImGuiKey key) {
            bool active = _activeTool == tool;
            if (active) ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.6f, 1.0f, 0.6f));
            if (ImGui.Button(label)) _activeTool = tool;
            if (active) ImGui.PopStyleColor();
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(key)) _activeTool = tool;
            ImGui.SameLine();
        }
        ToolButton($"{L10n.Tr("Gizmo_Move")} (W)", GizmoTool.Move, ImGuiKey.W);
        ToolButton($"{L10n.Tr("Gizmo_Scale")} (E)", GizmoTool.Scale, ImGuiKey.E);
        ToolButton($"{L10n.Tr("Gizmo_Rotate")} (R)", GizmoTool.Rotate, ImGuiKey.R);
        ToolButton($"{L10n.Tr("Gizmo_Rect")} (T)", GizmoTool.Rect, ImGuiKey.T);
        ImGui.Dummy(new Vector2(20, 0)); ImGui.SameLine();
        ImGui.Checkbox(L10n.Tr("label_snap"), ref _gridSnap); ImGui.SameLine();
        ImGui.SetNextItemWidth(60f); ImGui.DragFloat("##SnapSize", ref _snapSize, 0.1f, 0.01f, 100f, "S: %.2f");
        if (_snapSize <= 0.01f) _snapSize = 0.01f;
        ImGui.Separator();
    }

    private void RenderEditorGizmos(World world)
    {
        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            foreach (var script in entity.GetComponents<Script>())
            {
                if (script.Enabled) script._onDrawGizmosDelegate?.Invoke();
            }
        }

        foreach (var selected in EditorSelection.SelectedEntities)
        {
            if (!selected.Active) continue;
            foreach (var script in selected.GetComponents<Script>())
            {
                if (script.Enabled) script._onDrawGizmosSelectedDelegate?.Invoke();
            }

            var (center, size, rotation) = GetEntityBounds(selected);
            float pixel = GetWorldPixelSize();
            _app.RenderPipeline.RenderGizmoRect(center, size + new Vector2(pixel * 6f), rotation, pixel * 2.5f, SelectionColor, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            _app.RenderPipeline.RenderGizmoRect(center, size, rotation, pixel * 1.0f, Verity.Core.Color.White, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            
            if (selected == EditorSelection.SelectedEntity)
            {
                if (_activeTool == GizmoTool.Scale) RenderScaleHandles(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
                if (_activeTool == GizmoTool.Rotate) RenderRotateHandle(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
                if (_activeTool == GizmoTool.Rect) RenderRectHandles(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);

                if (EditorSelection.IsEditingPolygon)
                {
                    var targetComp = EditorSelection.EditingPolygonComponent;
                    if (targetComp is PolygonShape poly) RenderPolygonEditor(poly.Vertices, poly.GetVertices());
                    else if (targetComp is PolygonRenderer polyR) RenderPolygonEditor(polyR.Vertices, polyR.GetWorldVertices());
                }
            }
        }
    }

    private void RenderRectHandles(Vector2 center, Vector2 size, float rotation, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        var handles = GetHandlePositions(center, size, rotation);
        float pixel = GetWorldPixelSize();
        float handleSize = pixel * HandleScreenSize;
        for (int i = 0; i < handles.Length; i++) {
            var color = (i == _activeHandle) ? Verity.Core.Color.Yellow : Verity.Core.Color.Cyan;
            _app.RenderPipeline.RenderGizmoQuad(handles[i], new Vector2(handleSize), color, cam, fbo);
        }
    }

    private void RenderScaleHandles(Vector2 center, Vector2 size, float rotation, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        var handles = GetHandlePositions(center, size, rotation);
        float pixel = GetWorldPixelSize();
        float handleSize = pixel * HandleScreenSize;
        for (int i = 0; i < handles.Length; i++) {
            var color = (i == _activeHandle) ? Verity.Core.Color.Yellow : HandleColor;
            _app.RenderPipeline.RenderGizmoQuad(handles[i], new Vector2(handleSize), color, cam, fbo);
            _app.RenderPipeline.RenderGizmoQuad(handles[i], new Vector2(handleSize - pixel * 2f), HandleFillColor, cam, fbo);
        }
    }

    private void RenderRotateHandle(Vector2 center, Vector2 size, float rotation, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        float pixel = GetWorldPixelSize();
        float rad = rotation * MathF.PI / 180f;
        var dir = new Vector2(-MathF.Sin(rad), MathF.Cos(rad));
        var start = center + dir * (size.Y * 0.5f);
        var end = center + dir * (size.Y * 0.5f + pixel * 30f);
        _app.RenderPipeline.RenderGizmoLine(start, end, pixel * 2f, RotateHandleColor, cam, fbo);
        var color = (_activeHandle == 88) ? Verity.Core.Color.Yellow : RotateHandleColor;
        _app.RenderPipeline.RenderGizmoQuad(end, new Vector2(pixel * HandleScreenSize * 1.5f), color, cam, fbo);
    }

    private int _draggedVertexIndex = -1;
    private Vector2 _vertexStartPos;
    private int _hoveredEdgeIndex = -1;

    private void RenderPolygonEditor(List<Vector2> localVertices, Vector2[] worldVertices)
    {
        float pixel = GetWorldPixelSize();
        float handleSize = pixel * 10f;
        var cam = _app.WorldCamera;
        var fbo = _app.RenderPipeline.WorldFbo;

        var io = ImGui.GetIO();
        var imgMin = ImGui.GetItemRectMin();
        var imgSize = ImGui.GetItemRectSize();
        var worldMouse = ToWorldMousePosition(imgMin, imgSize, io.MousePos);

        _hoveredEdgeIndex = -1;
        if (io.KeyShift)
        {
            for (int i = 0; i < worldVertices.Length; i++)
            {
                var p1 = worldVertices[i];
                var p2 = worldVertices[(i + 1) % worldVertices.Length];
                var closest = GetClosestPointOnSegment(worldMouse, p1, p2);
                if (Vector2.Distance(worldMouse, closest) < handleSize)
                {
                    _hoveredEdgeIndex = i;
                    break;
                }
            }
        }

        for (int i = 0; i < worldVertices.Length; i++) {
            var p1 = worldVertices[i];
            var p2 = worldVertices[(i + 1) % worldVertices.Length];
            var color = (i == _hoveredEdgeIndex) ? Verity.Core.Color.Red : new Verity.Core.Color(0, 255, 0, 200);
            _app.RenderPipeline.RenderGizmoLine(p1, p2, pixel * 2f, color, cam, fbo);
        }
        for (int i = 0; i < worldVertices.Length; i++) {
            bool isToDelete = false;
            if (_hoveredEdgeIndex != -1)
            {
                if (i == _hoveredEdgeIndex || i == (_hoveredEdgeIndex + 1) % worldVertices.Length)
                    isToDelete = true;
            }

            var color = isToDelete ? Verity.Core.Color.Red : ((i == _draggedVertexIndex) ? Verity.Core.Color.Yellow : Verity.Core.Color.Green);
            _app.RenderPipeline.RenderGizmoRect(worldVertices[i], new Vector2(handleSize), 0, pixel * 1.5f, color, cam, fbo);
            _app.RenderPipeline.RenderGizmoQuad(worldVertices[i], new Vector2(handleSize), new Verity.Core.Color(color.R, color.G, color.B, 50), cam, fbo);
        }
    }

    private void HandlePolygonInteraction(List<Vector2> localVertices, Vector2[] worldVertices, Func<Vector2, Vector2> worldToLocal, Func<Vector2, Vector2> worldDeltaToLocal, Vector2 worldMouse)
    {
        if (localVertices == null || worldVertices == null || worldVertices.Length == 0) return;

        float pixel = GetWorldPixelSize();
        float handleSize = pixel * 10f;
        var io = ImGui.GetIO();
        if (ImGui.IsMouseClicked(0)) {
            for (int i = 0; i < worldVertices.Length; i++) {
                if (Vector2.Distance(worldMouse, worldVertices[i]) < handleSize) {
                    if (io.KeyCtrl) { if (localVertices.Count > 3) { _app.RecordUndo(); localVertices.RemoveAt(i); } }
                    else { _app.BeginUndoAction(); _draggedVertexIndex = i; _vertexStartPos = localVertices[i]; _dragStartWorld = worldMouse; }
                    return;
                }
            }
            for (int i = 0; i < worldVertices.Length; i++) {
                var p1 = worldVertices[i]; var p2 = worldVertices[(i + 1) % worldVertices.Length];
                var closest = GetClosestPointOnSegment(worldMouse, p1, p2);
                if (Vector2.Distance(worldMouse, closest) < handleSize) {
                    if (io.KeyShift) {
                        if (localVertices.Count >= 5) {
                            _app.RecordUndo();
                            int i1 = i;
                            int i2 = (i + 1) % localVertices.Count;
                            if (i1 < i2) { localVertices.RemoveAt(i2); localVertices.RemoveAt(i1); }
                            else { localVertices.RemoveAt(i1); localVertices.RemoveAt(i2); }
                        }
                    } else {
                        _app.RecordUndo();
                        var localNew = worldToLocal(closest);
                        if (i + 1 >= localVertices.Count) localVertices.Add(localNew);
                        else localVertices.Insert(i + 1, localNew);
                        _app.BeginUndoAction(); _draggedVertexIndex = (i + 1) % localVertices.Count; _vertexStartPos = localNew; _dragStartWorld = worldMouse;
                    }
                    return;
                }
            }
        }
        if (ImGui.IsMouseDown(0) && _draggedVertexIndex != -1 && _draggedVertexIndex < localVertices.Count) {
            var deltaWorld = worldMouse - _dragStartWorld;
            var localDelta = worldDeltaToLocal(deltaWorld);
            localVertices[_draggedVertexIndex] = _vertexStartPos + localDelta;
        }
        if (ImGui.IsMouseReleased(0) && _draggedVertexIndex != -1) { _app.EndUndoAction(); _draggedVertexIndex = -1; }
    }

    private Vector2 WorldToLocalRendererSpace(PolygonRenderer poly, Vector2 worldPos) {
        var t = poly.Owner.Transform; float rad = -t.Rotation * MathF.PI / 180f; var s = t.Scale;
        var rel = worldPos - t.Position; float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var rotated = new Vector2(rel.X * cos - rel.Y * sin, rel.X * sin + rel.Y * cos);
        return rotated / s;
    }

    private Vector2 WorldDeltaToLocalRendererDelta(PolygonRenderer poly, Vector2 worldDelta) {
        var t = poly.Owner.Transform; float rad = -t.Rotation * MathF.PI / 180f; var s = t.Scale;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var rotated = new Vector2(worldDelta.X * cos - worldDelta.Y * sin, worldDelta.X * sin + worldDelta.Y * cos);
        return rotated / s;
    }

    private void HandleWorldInteraction(World? world, Vector2 imgMin, Vector2 imgSize, bool hovered)
    {
        var io = ImGui.GetIO();
        if (ImGui.IsMouseReleased(0)) { 
            if (_isDragging || _activeHandle >= 0) _app.EndUndoAction(); 
            if (_isBoxSelecting) FinalizeBoxSelection(world);
            _isDragging = false; _isBoxSelecting = false; _activeHandle = -1; 
        }
        if (world == null) return;
        var worldMouse = ToWorldMousePosition(imgMin, imgSize, io.MousePos);

        if (EditorSelection.IsEditingPolygon && EditorSelection.SelectedEntity != null) {
            var targetComp = EditorSelection.EditingPolygonComponent;
            
            if (targetComp is PolygonShape poly) { 
                HandlePolygonInteraction(poly.Vertices, poly.GetVertices(), (v) => WorldToLocalPolygonSpace(poly, v), (v) => WorldDeltaToLocalPolygonDelta(poly, v), worldMouse); 
                if (_draggedVertexIndex != -1) return; 
            }
            else if (targetComp is PolygonRenderer polyR) { 
                HandlePolygonInteraction(polyR.Vertices, polyR.GetWorldVertices(), (v) => WorldToLocalRendererSpace(polyR, v), (v) => WorldDeltaToLocalRendererDelta(polyR, v), worldMouse); 
                if (_draggedVertexIndex != -1) return; 
            }
        }

        if (ImGui.BeginDragDropTarget()) {
            if (EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint")) {
                DrawBlueprintPreview(worldMouse, Path.GetFileNameWithoutExtension(EditorSelection.DraggedAssetPath), imgMin);
                if (ImGui.AcceptDragDropPayload("ASSET_PATH").Handle != null) {
                    _app.RecordUndo();
                    if (_previewEntity != null) { SetAlphaRecursive(_previewEntity, 1.0f); _previewEntity = null; _previewPath = null; }
                    else _app.InstantiateBlueprint(EditorSelection.DraggedAssetPath, _gridSnap ? SnapToGrid(worldMouse) : worldMouse);
                    EditorSelection.DraggedAssetPath = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (!hovered && !_isDragging && !_isBoxSelecting) return;

        if (ImGui.IsMouseClicked(0)) {
            var primary = EditorSelection.SelectedEntity;
            if (primary != null) {
                if (_activeTool == GizmoTool.Scale) _activeHandle = HitTestScaleHandles(primary, worldMouse);
                else if (_activeTool == GizmoTool.Rotate) _activeHandle = HitTestRotateHandle(primary, worldMouse) ? 88 : -1;
                else if (_activeTool == GizmoTool.Rect) _activeHandle = HitTestRectHandles(primary, worldMouse);
                
                if (_activeHandle >= 0) {
                    _app.BeginUndoAction(); 
                    _dragStartWorld = worldMouse;
                    _draggedEntities.Clear();
                    foreach (var ent in EditorSelection.SelectedEntities)
                        _draggedEntities.Add((ent, ent.Transform.WorldPosition, ent.Transform.WorldScale, ent.Transform.WorldRotation));
                    return;
                }
            }
            var picked = PickEntity(world, worldMouse);
            if (picked != null) {
                if (io.KeyCtrl) { if (EditorSelection.IsSelected(picked)) EditorSelection.Deselect(picked); else EditorSelection.Select(picked, true); }
                else if (!EditorSelection.IsSelected(picked)) EditorSelection.SelectedEntity = picked;
                
                _isDragging = true; 
                _app.BeginUndoAction(); 
                _dragStartWorld = worldMouse;
                _draggedEntities.Clear();
                foreach (var ent in EditorSelection.SelectedEntities)
                    _draggedEntities.Add((ent, ent.Transform.WorldPosition, ent.Transform.WorldScale, ent.Transform.WorldRotation));
            } else {
                if (!io.KeyCtrl) EditorSelection.ClearSelection();
                _isBoxSelecting = true; _boxSelectionStart = io.MousePos;
            }
        }
        if (_isBoxSelecting) {
            var dl = ImGui.GetWindowDrawList();
            dl.AddRect(_boxSelectionStart, io.MousePos, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.5f)), 0, 0, 2f);
            dl.AddRectFilled(_boxSelectionStart, io.MousePos, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.1f)));
        }
        if (ImGui.IsMouseDown(0)) {
            if (_activeHandle >= 0 || _isDragging || _isBoxSelecting) _app.StopFocusInterpolation();
            if (_activeHandle == 88) HandleRotateDrag(worldMouse);
            else if (_activeTool == GizmoTool.Rect && _activeHandle >= 0) HandleRectDrag(worldMouse);
            else if (_activeHandle >= 0) HandleScaleDragPainterStyle(worldMouse);
            else if (_isDragging) HandleMoveDrag(worldMouse);
        }
        
        if (ImGui.BeginPopupContextWindow()) {
            if (EditorSelection.SelectedEntities.Count > 0) {
                if (ImGui.MenuItem(L10n.Tr("ctx_copy"))) _app.GetWindow<HierarchyWindow>()?.CopySelected();
                if (ImGui.MenuItem(L10n.Tr("ctx_duplicate"))) DuplicateSelected();
                if (ImGui.MenuItem(L10n.Tr("ctx_delete"))) DeleteSelected();
            }
            if (_app.GetWindow<HierarchyWindow>()?.CanPaste() ?? false) {
                if (ImGui.MenuItem(L10n.Tr("ctx_paste"))) _app.GetWindow<HierarchyWindow>()?.Paste(world);
            }
            ImGui.EndPopup();
        }
    }

    private void FinalizeBoxSelection(World? world) {
        if (world == null) return;
        var io = ImGui.GetIO(); var min = Vector2.Min(_boxSelectionStart, io.MousePos); var max = Vector2.Max(_boxSelectionStart, io.MousePos);
        var imgMin = ImGui.GetItemRectMin(); var cam = _app.WorldCamera;
        if (!io.KeyCtrl) EditorSelection.ClearSelection();
        foreach (var ent in world.GetAllEntities()) {
            var bounds = GetEntityBounds(ent);
            var screenPos = imgMin + cam.WorldToScreen(bounds.center);
            if (screenPos.X >= min.X && screenPos.X <= max.X && screenPos.Y >= min.Y && screenPos.Y <= max.Y) EditorSelection.Select(ent, true);
        }
    }

    private void HandleMoveDrag(Vector2 worldMouse) {
        var delta = worldMouse - _dragStartWorld;
        foreach (var (ent, startWorldPos, _, _) in _draggedEntities) {
            var next = startWorldPos + delta;
            ent.Transform.WorldPosition = _gridSnap ? SnapToGrid(next) : next;
        }
    }

    private void HandleRotateDrag(Vector2 worldMouse) {
        var main = EditorSelection.SelectedEntity; if (main == null) return;
        var center = main.Transform.WorldPosition;
        float a1 = MathF.Atan2(worldMouse.Y - center.Y, worldMouse.X - center.X);
        float a2 = MathF.Atan2(_dragStartWorld.Y - center.Y, _dragStartWorld.X - center.X);
        float deltaDeg = (a1 - a2) * 180f / MathF.PI;
        foreach (var (ent, _, _, startWorldRot) in _draggedEntities) ent.Transform.WorldRotation = startWorldRot + deltaDeg;
    }

    private void HandleScaleDragPainterStyle(Vector2 worldMouse) {
        var main = EditorSelection.SelectedEntity; if (main == null) return;
        
        var delta = worldMouse - _dragStartWorld;
        float rad = main.Transform.WorldRotation * MathF.PI / 180f;
        float cos = MathF.Cos(-rad), sin = MathF.Sin(-rad);
        var localDelta = new Vector2(delta.X * cos - delta.Y * sin, delta.X * sin + delta.Y * cos);

        // Scaling factor calculation
        // Handles: 0:TL, 1:TC, 2:TR, 3:RC, 4:BR, 5:BC, 6:BL, 7:LC
        Vector2 scaleMultiplier = Vector2.Zero;
        switch (_activeHandle)
        {
            case 0: scaleMultiplier = new Vector2(-1, 1); break;
            case 1: scaleMultiplier = new Vector2(0, 1); break;
            case 2: scaleMultiplier = new Vector2(1, 1); break;
            case 3: scaleMultiplier = new Vector2(1, 0); break;
            case 4: scaleMultiplier = new Vector2(1, -1); break;
            case 5: scaleMultiplier = new Vector2(0, -1); break;
            case 6: scaleMultiplier = new Vector2(-1, -1); break;
            case 7: scaleMultiplier = new Vector2(-1, 0); break;
        }

        foreach (var (ent, _, startWorldScale, _) in _draggedEntities)
        {
            var newScale = startWorldScale + localDelta * scaleMultiplier * 2.0f;
            if (_gridSnap)
            {
                newScale.X = MathF.Round(newScale.X / _snapSize) * _snapSize;
                newScale.Y = MathF.Round(newScale.Y / _snapSize) * _snapSize;
            }
            ent.Transform.WorldScale = newScale;
        }
    }

    private int HitTestRectHandles(Entity e, Vector2 m) => HitTestScaleHandles(e, m);

    private void HandleRectDrag(Vector2 worldMouse) {
        var main = EditorSelection.SelectedEntity; if (main == null) return;
        var delta = worldMouse - _dragStartWorld;
        float rad = main.Transform.WorldRotation * MathF.PI / 180f;
        float cos = MathF.Cos(-rad), sin = MathF.Sin(-rad);
        var localDelta = new Vector2(delta.X * cos - delta.Y * sin, delta.X * sin + delta.Y * cos);

        foreach (var (ent, startWorldPos, startWorldScale, startWorldRot) in _draggedEntities) {
            float sCos = MathF.Cos(startWorldRot * MathF.PI / 180f), sSin = MathF.Sin(startWorldRot * MathF.PI / 180f);
            Vector2 scaleMultiplier = Vector2.Zero;
            Vector2 pivotOffset = Vector2.Zero; // This is the direction the center should move relative to the scale change

            switch (_activeHandle) {
                case 0: scaleMultiplier = new Vector2(-1, 1); pivotOffset = new Vector2(-0.5f, 0.5f); break;
                case 1: scaleMultiplier = new Vector2(0, 1); pivotOffset = new Vector2(0, 0.5f); break;
                case 2: scaleMultiplier = new Vector2(1, 1); pivotOffset = new Vector2(0.5f, 0.5f); break;
                case 3: scaleMultiplier = new Vector2(1, 0); pivotOffset = new Vector2(0.5f, 0); break;
                case 4: scaleMultiplier = new Vector2(1, -1); pivotOffset = new Vector2(0.5f, -0.5f); break;
                case 5: scaleMultiplier = new Vector2(0, -1); pivotOffset = new Vector2(0, -0.5f); break;
                case 6: scaleMultiplier = new Vector2(-1, -1); pivotOffset = new Vector2(-0.5f, -0.5f); break;
                case 7: scaleMultiplier = new Vector2(-1, 0); pivotOffset = new Vector2(-0.5f, 0); break;
            }

            var deltaScale = localDelta * scaleMultiplier;
            var nextScale = startWorldScale + deltaScale;
            if (_gridSnap) { nextScale.X = MathF.Round(nextScale.X / _snapSize) * _snapSize; nextScale.Y = MathF.Round(nextScale.Y / _snapSize) * _snapSize; }
            var actualDeltaScale = nextScale - startWorldScale;
            
            // To keep the opposite side fixed, the center must move by half of the scale change in the local space
            var localMove = actualDeltaScale * pivotOffset;
            var worldMove = new Vector2(localMove.X * sCos - localMove.Y * sSin, localMove.X * sSin + localMove.Y * sCos);
            
            ent.Transform.WorldScale = nextScale;
            ent.Transform.WorldPosition = startWorldPos + worldMove;
        }
    }

    private void HandleShortcuts() {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return;
        var io = ImGui.GetIO(); if (io.WantCaptureKeyboard) return;
        if (ImGui.IsKeyPressed(ImGuiKey.F) && EditorSelection.SelectedEntity != null) _app.FocusEntity(EditorSelection.SelectedEntity);
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) && EditorSelection.SelectedEntities.Count > 0) DeleteSelected();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.D) && EditorSelection.SelectedEntities.Count > 0) DuplicateSelected();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C)) _app.GetWindow<HierarchyWindow>()?.CopySelected();
        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.V)) _app.GetWindow<HierarchyWindow>()?.Paste(WorldManager.ActiveWorld!);
        if (ImGui.IsKeyPressed(ImGuiKey.W)) _activeTool = GizmoTool.Move;
        if (ImGui.IsKeyPressed(ImGuiKey.E)) _activeTool = GizmoTool.Scale;
        if (ImGui.IsKeyPressed(ImGuiKey.R)) _activeTool = GizmoTool.Rotate;
        if (ImGui.IsKeyPressed(ImGuiKey.T)) _activeTool = GizmoTool.Rect;
    }

    private void DuplicateSelected() { var w = WorldManager.ActiveWorld; if (w != null) _app.GetWindow<HierarchyWindow>()?.DuplicateSelected(w); }
    private void DeleteSelected() { var w = WorldManager.ActiveWorld; if (w != null) _app.GetWindow<HierarchyWindow>()?.DeleteSelected(w); }

    private void DrawBlueprintPreview(Vector2 worldPos, string name, Vector2 imgMin) {
        var cam = _app.WorldCamera; var drawList = ImGui.GetWindowDrawList();
        var pos = _gridSnap ? SnapToGrid(worldPos) : worldPos; var screenPos = imgMin + cam.WorldToScreen(pos);
        var guidelineColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.4f)); var boxColor = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 1.0f, 0.7f));
        drawList.AddLine(new Vector2(screenPos.X, imgMin.Y), new Vector2(screenPos.X, imgMin.Y + ImGui.GetItemRectSize().Y), guidelineColor);
        drawList.AddLine(new Vector2(imgMin.X, screenPos.Y), new Vector2(imgMin.X + ImGui.GetItemRectSize().X, screenPos.Y), guidelineColor);
        float halfSize = (1.0f / GetWorldPixelSize()) * 0.5f;
        drawList.AddRect(screenPos - new Vector2(halfSize), screenPos + new Vector2(halfSize), boxColor, 0, 0, 2.0f);
        var label = $"{L10n.Tr("btn_add")}: {name}"; var labelSize = ImGui.CalcTextSize(label); var labelPos = screenPos + new Vector2(-labelSize.X * 0.5f, -labelSize.Y - 15);
        drawList.AddRectFilled(labelPos - new Vector2(5, 2), labelPos + labelSize + new Vector2(5, 2), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 4f);
        drawList.AddText(labelPos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), label);
    }

    private Vector2 WorldToLocalPolygonSpace(PolygonShape poly, Vector2 worldPos) {
        var t = poly.Owner.Transform; float rad = -t.Rotation * MathF.PI / 180f; var s = t.Scale;
        var rel = worldPos - t.Position; float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var rotated = new Vector2(rel.X * cos - rel.Y * sin, rel.X * sin + rel.Y * cos);
        return (rotated / s) - poly.Offset;
    }

    private Vector2 WorldDeltaToLocalPolygonDelta(PolygonShape poly, Vector2 worldDelta) {
        var t = poly.Owner.Transform; float rad = -t.Rotation * MathF.PI / 180f; var s = t.Scale;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var rotated = new Vector2(worldDelta.X * cos - worldDelta.Y * sin, worldDelta.X * sin + worldDelta.Y * cos);
        return rotated / s;
    }

    private static Vector2 GetClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b) {
        Vector2 ab = b - a; float t = Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab);
        return a + Math.Clamp(t, 0, 1) * ab;
    }

    private int HitTestScaleHandles(Entity e, Vector2 m) { var (c, s, r) = GetEntityBounds(e); var h = GetHandlePositions(c, s, r); float d = GetWorldPixelSize() * HandleScreenSize; for (int i = 0; i < h.Length; i++) if (Vector2.Distance(m, h[i]) < d) return i; return -1; }
    private bool HitTestRotateHandle(Entity e, Vector2 m) { var (c, s, r) = GetEntityBounds(e); float rad = r * MathF.PI / 180f; var p = c + new Vector2(-MathF.Sin(rad), MathF.Cos(rad)) * (s.Y * 0.5f + GetWorldPixelSize() * 30f); return Vector2.Distance(m, p) < GetWorldPixelSize() * HandleScreenSize * 1.5f; }
    private static Vector2[] GetHandlePositions(Vector2 c, Vector2 s, float r) { float rad = r * MathF.PI / 180f; float cos = MathF.Cos(rad), sin = MathF.Sin(rad); float hx = s.X * 0.5f, hy = s.Y * 0.5f; Vector2 Rot(float lx, float ly) => c + new Vector2(lx * cos - ly * sin, lx * sin + ly * cos); return [ Rot(-hx, hy), Rot(0, hy), Rot(hx, hy), Rot(hx, 0), Rot(hx, -hy), Rot(0, -hy), Rot(-hx, -hy), Rot(-hx, 0) ]; }
    private Entity? PickEntity(World world, Vector2 mouse) { var renderers = CollectRenderers(world); SortRenderers(renderers); for (int i = renderers.Count - 1; i >= 0; i--) if (IsPointInsideSpriteAabb(renderers[i], mouse)) return renderers[i].Owner; return PickEmptyEntity(world, mouse); }
    private Entity? PickEmptyEntity(World world, Vector2 mouse) { Entity? res = null; foreach (var e in world.RootEntities) PickEmptyEntityRecursive(e, mouse, ref res); return res; }
    private static void PickEmptyEntityRecursive(Entity e, Vector2 m, ref Entity? r) { if (!e.Active) return; if (e.GetComponent<SpriteRenderer>() == null) { var p = e.Transform.WorldPosition; var s = e.Transform.Scale * DefaultEntitySize * 0.5f; if (m.X >= p.X - MathF.Abs(s.X) && m.X <= p.X + MathF.Abs(s.X) && m.Y >= p.Y - MathF.Abs(s.Y) && m.Y <= p.Y + MathF.Abs(s.Y)) r = e; } foreach (var c in e.Transform.Children) PickEmptyEntityRecursive(c.Owner, m, ref r); }
    private static bool IsPointInsideSpriteAabb(SpriteRenderer sr, Vector2 p) { var t = sr.Owner.Transform; var wp = t.WorldPosition; var s = t.WorldScale * sr.Size; var min = wp - sr.Pivot * s; var max = wp + (Vector2.One - sr.Pivot) * s; return p.X >= MathF.Min(min.X, max.X) && p.X <= MathF.Max(min.X, max.X) && p.Y >= MathF.Min(min.Y, max.Y) && p.Y <= MathF.Max(min.Y, max.Y); }
    private static (Vector2 center, Vector2 size, float rotation) GetEntityBounds(Entity e) 
    { 
        var sr = e.GetComponent<SpriteRenderer>(); 
        var t = e.Transform; 
        var wp = t.WorldPosition; 
        var s = t.WorldScale; 
        var r = t.WorldRotation; 
        if (sr != null) 
        { 
            var effS = new Vector2(MathF.Abs(s.X * sr.Size.X), MathF.Abs(s.Y * sr.Size.Y)); 
            // Avoid divide by zero if scale or size is near zero
            if (effS.X < 0.0001f) effS.X = 0.0001f;
            if (effS.Y < 0.0001f) effS.Y = 0.0001f;

            var off = (Vector2.One * 0.5f - sr.Pivot) * (s * sr.Size); 
            float rad = r * MathF.PI / 180f; 
            var roff = new Vector2(off.X * MathF.Cos(rad) - off.Y * MathF.Sin(rad), off.X * MathF.Sin(rad) + off.Y * MathF.Cos(rad)); 
            return (wp + roff, effS, r); 
        } 
        var absS = new Vector2(MathF.Max(0.0001f, MathF.Abs(s.X)), MathF.Max(0.0001f, MathF.Abs(s.Y))); 
        return (wp, absS * DefaultEntitySize, r); 
    }
    private float GetWorldPixelSize() { float h = _app.WorldCamera.VisibleHalfHeight * 2f; return _app.WorldCamera.ViewportHeight > 0 ? h / _app.WorldCamera.ViewportHeight : 0.01f; }
    private Vector2 ToWorldMousePosition(Vector2 min, Vector2 sz, Vector2 abs) { var l = abs - min; l.X = Math.Clamp(l.X, 0f, sz.X); l.Y = Math.Clamp(l.Y, 0f, sz.Y); return _app.WorldCamera.ScreenToWorld(l); }
    private static List<SpriteRenderer> CollectRenderers(World w) { var r = new List<SpriteRenderer>(); foreach (var e in w.RootEntities) CollectRenderersRecursive(e, r); return r; }
    private static void CollectRenderersRecursive(Entity e, List<SpriteRenderer> r) { if (!e.Active) return; var sr = e.GetComponent<SpriteRenderer>(); if (sr != null) r.Add(sr); foreach (var c in e.Transform.Children) CollectRenderersRecursive(c.Owner, r); }
    private void SortRenderers(List<SpriteRenderer> r) { r.Sort((a, b) => { int lc = Verity.Graphics.SortingLayer.GetLayerIndex(a.SortingLayerName).CompareTo(Verity.Graphics.SortingLayer.GetLayerIndex(b.SortingLayerName)); if (lc != 0) return lc; return a.OrderInLayer.CompareTo(b.OrderInLayer); }); }
    private Vector2 SnapToGrid(Vector2 p) => new(MathF.Round(p.X / _snapSize) * _snapSize, MathF.Round(p.Y / _snapSize) * _snapSize);
    private void HandleCameraControls() { 
        if (!ImGui.IsWindowHovered()) return; 
        var io = ImGui.GetIO(); 
        var cam = _app.WorldCamera; 
        if (io.MouseWheel != 0) { 
            _app.StopFocusInterpolation();
            var imgMin = ImGui.GetItemRectMin(); 
            var imgSize = ImGui.GetItemRectSize(); 
            Vector2 mouseWorldBefore = ToWorldMousePosition(imgMin, imgSize, io.MousePos); 
            float zoomFactor = 1.0f - io.MouseWheel * 0.1f; 
            cam.Zoom = MathF.Max(0.01f, cam.Zoom * zoomFactor); 
            Vector2 mouseWorldAfter = ToWorldMousePosition(imgMin, imgSize, io.MousePos); 
            cam.Position += (mouseWorldBefore - mouseWorldAfter); 
        } 
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)) { 
            _app.StopFocusInterpolation();
            var d = io.MouseDelta; 
            float pixel = GetWorldPixelSize(); 
            cam.Position -= new Vector2(d.X * pixel, -d.Y * pixel); 
        } 
    }
    private unsafe void DrawInputModal() { 
        var v = ImGui.GetMainViewport(); var c = new Vector2(v.Pos.X + v.Size.X * 0.5f, v.Pos.Y + v.Size.Y * 0.5f); 
        ImGui.SetNextWindowPos(c, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f)); 
        if (ImGui.BeginPopupModal("WorldActionModal", null, ImGuiWindowFlags.AlwaysAutoResize)) { 
            string title = _activeMode == ModalMode.Create ? L10n.Tr("msg_create_asset", L10n.Tr($"CreationType_{_creationType}")) : L10n.Tr("msg_rename_asset");
            ImGui.Text(title); ImGui.Separator(); 
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere(); 
            ImGui.InputText(L10n.Tr("label_name"), ref _inputBuffer, 64); 
            var size = new Vector2(120, 0); 
            if (ImGui.Button(L10n.Tr("btn_ok"), size) || ImGui.IsKeyPressed(ImGuiKey.Enter)) { FinalizeAction(); ImGui.CloseCurrentPopup(); } 
            ImGui.SameLine(); 
            if (ImGui.Button(L10n.Tr("btn_cancel"), size)) ImGui.CloseCurrentPopup(); 
            ImGui.EndPopup(); 
        } 
    }
    private void FinalizeAction() { if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer)) return; if (_activeMode == ModalMode.Create) { if (_creationType == CreationType.Script) { var p = System.IO.Path.Combine(_targetPath, _inputBuffer + ".cs"); System.IO.File.WriteAllText(p, $"using Verity.Core.ECS;\n\npublic class {_inputBuffer} : Script\n{{\n    void Start()\n    {{\n    }}\n\n    void Update()\n    {{\n    }}\n}}"); } else if (_creationType == CreationType.World) { var p = System.IO.Path.Combine(_targetPath, _inputBuffer + ".verity"); var w = new World(_inputBuffer); var camEnt = w.CreateEntity("Main Camera"); camEnt.AddComponent<Camera>(); System.IO.File.WriteAllText(p, Verity.Core.Serialization.SceneSerializer.Serialize(w)); LoadWorldByPath(p); } } }
    public void CreateWorldInProject() => OpenCreatePopup(_app.AssetsPath!, CreationType.World);
    public void LoadWorldByPath(string path) { 
        if (!System.IO.File.Exists(path)) return; 
        if (_app.IsPlaying) _app.ExitPlayMode(); 
        
        // Reset editor state for the new world
        EditorSelection.EditingPolygonComponent = null;
        EditorSelection.ClearSelection();
        
        var w = WorldManager.CreateOrReplaceWorld(Path.GetFileNameWithoutExtension(path)); 
        Verity.Core.Serialization.SceneSerializer.Deserialize(w, File.ReadAllText(path), _app.ScriptCompiler?.CompiledAssembly); 
        foreach (var entity in w.GetAllEntities()) { 
            var sr = entity.GetComponent<SpriteRenderer>(); 
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) { 
                var fullPath = System.IO.Path.Combine(_app.ProjectPath!, sr.Sprite.Path); 
                if (System.IO.File.Exists(fullPath)) sr.Texture = _app.TextureManager.Load(fullPath); 
            } 
        } 
        WorldManager.SetActiveWorld(w); 
    }
    public void SaveActiveWorldAsAsset() { if (WorldManager.ActiveWorld == null || _app.AssetsPath == null) return; var path = System.IO.Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity"); System.IO.File.WriteAllText(path, Verity.Core.Serialization.SceneSerializer.Serialize(WorldManager.ActiveWorld)); }
    public void CompileScriptsForActiveWorld() => _app.ScriptCompiler?.Compile();
    private void OpenCreatePopup(string dir, CreationType type) { _activeMode = ModalMode.Create; _creationType = type; _targetPath = dir; _inputBuffer = type == CreationType.Script ? "NewScript" : "NewWorld"; _shouldOpenPopup = true; }
}
