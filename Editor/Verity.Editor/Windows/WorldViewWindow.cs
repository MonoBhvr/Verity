using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using System.Diagnostics;
using Verity.Core.Physics;

namespace Verity.Editor.Windows;

public unsafe class WorldViewWindow : EditorWindow
{
    public enum GizmoTool { Move, Scale, Rotate }
    private enum ModalMode { None, Create, Rename }
    private enum CreationType { Script, World, Folder }

    private readonly EditorApp _app;
    private bool _isDragging;
    private bool _gridSnap;
    private float _snapSize = 1.0f;
    private GizmoTool _activeTool = GizmoTool.Move;

    private const float HandleScreenSize = 10f;
    private const float DefaultEntitySize = 1f;

    private int _activeHandle = -1;
    private Vector2 _dragStartWorld;
    private Vector2 _entityStartPos;
    private Vector2 _entityStartScale;
    private float _entityStartRotation;

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

    public WorldViewWindow(EditorApp app) : base("World") { _app = app; }

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

            // Use direct mouse check because IsWindowHovered can be unreliable during drag-drop
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

    private void UpdatePreviewEntity(World world, bool hovered, Vector2 imgMin)
    {
        bool isDraggingBlueprint = EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint");

        if (isDraggingBlueprint && hovered)
        {
            var io = ImGui.GetIO();
            var worldMouse = _app.WorldCamera.ScreenToWorld(io.MousePos - imgMin);
            var pos = _gridSnap ? SnapToGrid(worldMouse) : worldMouse;

            if (_previewEntity == null || _previewPath != EditorSelection.DraggedAssetPath)
            {
                if (_previewEntity != null) world.DestroyEntity(_previewEntity);
                _previewEntity = _app.InstantiateBlueprint(EditorSelection.DraggedAssetPath!);
                _previewPath = EditorSelection.DraggedAssetPath;
                // Preview is now Opaque (Alpha 1.0) as requested
                if (_previewEntity != null) SetAlphaRecursive(_previewEntity, 1.0f);
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
        ToolButton("Move (W)", GizmoTool.Move, ImGuiKey.W);
        ToolButton("Scale (E)", GizmoTool.Scale, ImGuiKey.E);
        ToolButton("Rotate (R)", GizmoTool.Rotate, ImGuiKey.R);
        ImGui.Dummy(new Vector2(20, 0)); ImGui.SameLine();
        ImGui.Checkbox("Snap", ref _gridSnap); ImGui.SameLine();
        ImGui.SetNextItemWidth(60f); ImGui.DragFloat("##SnapSize", ref _snapSize, 0.1f, 0.01f, 100f, "S: %.2f");
        if (_snapSize <= 0.01f) _snapSize = 0.01f;
        ImGui.Separator();
    }

    private void RenderEditorGizmos(World world)
    {
        // Invoke OnDrawGizmos for all scripts
        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            foreach (var script in entity.GetComponents<Script>())
            {
                if (script.Enabled) script._onDrawGizmosDelegate?.Invoke();
            }
        }

        var selected = EditorSelection.SelectedEntity;
        if (selected == null || !selected.Active) return;

        // Collider Editing Mode
        if (EditorSelection.IsEditingCollider)
        {
            var poly = selected.GetComponent<PolygonShape>();
            if (poly != null)
            {
                RenderPolygonEditor(poly);
                return; // Override other gizmos when editing collider
            }
        }

        // Invoke OnDrawGizmosSelected for selected entity's scripts
        foreach (var script in selected.GetComponents<Script>())
        {
            if (script.Enabled) script._onDrawGizmosSelectedDelegate?.Invoke();
        }

        var (center, size, rotation) = GetEntityBounds(selected);
        float pixel = GetWorldPixelSize();
        if (selected.Transform.Parent != null) {
            Vector2 parentPos = selected.Transform.Parent.WorldPosition;
            _app.RenderPipeline.RenderGizmoLine(parentPos, center, pixel * 1.5f, new Verity.Core.Color(255, 255, 255, 100), _app.WorldCamera, _app.RenderPipeline.WorldFbo);
        }
        _app.RenderPipeline.RenderGizmoRect(center, size + new Vector2(pixel * 6f), rotation, pixel * 2.5f, SelectionColor, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
        _app.RenderPipeline.RenderGizmoRect(center, size, rotation, pixel * 1.0f, Verity.Core.Color.White, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
        if (_activeTool == GizmoTool.Scale) RenderScaleHandles(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
        if (_activeTool == GizmoTool.Rotate) RenderRotateHandle(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
    }

    private void RenderScaleHandles(Vector2 center, Vector2 size, float rotation, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        var handles = GetHandlePositions(center, size, rotation);
        float pixel = GetWorldPixelSize();
        float handleSize = pixel * HandleScreenSize;

        for (int i = 0; i < handles.Length; i++)
        {
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

    private void RenderPolygonEditor(PolygonShape poly)
    {
        var worldVertices = poly.GetVertices();
        float pixel = GetWorldPixelSize();
        float handleSize = pixel * 8f;
        var cam = _app.WorldCamera;
        var fbo = _app.RenderPipeline.WorldFbo;

        // Draw Edges
        for (int i = 0; i < worldVertices.Length; i++)
        {
            var p1 = worldVertices[i];
            var p2 = worldVertices[(i + 1) % worldVertices.Length];
            _app.RenderPipeline.RenderGizmoLine(p1, p2, pixel * 2f, new Verity.Core.Color(0, 255, 0, 200), cam, fbo);
        }

        // Draw Vertices
        for (int i = 0; i < worldVertices.Length; i++)
        {
            var color = (i == _draggedVertexIndex) ? Verity.Core.Color.Yellow : Verity.Core.Color.Green;
            _app.RenderPipeline.RenderGizmoQuad(worldVertices[i], new Vector2(handleSize), color, cam, fbo);
        }
    }

    private void HandleColliderInteraction(PolygonShape poly, Vector2 worldMouse)
    {
        var worldVertices = poly.GetVertices();
        float pixel = GetWorldPixelSize();
        float handleSize = pixel * 10f;
        var io = ImGui.GetIO();

        if (ImGui.IsMouseClicked(0))
        {
            // 1. Check Vertex for Move or Delete
            for (int i = 0; i < worldVertices.Length; i++)
            {
                if (Vector2.Distance(worldMouse, worldVertices[i]) < handleSize)
                {
                    if (io.KeyCtrl) // Delete
                    {
                        if (poly.Vertices.Count > 3)
                        {
                            _app.RecordUndo();
                            poly.Vertices.RemoveAt(i);
                        }
                    }
                    else // Start Drag
                    {
                        _app.BeginUndoAction();
                        _draggedVertexIndex = i;
                        _vertexStartPos = poly.Vertices[i];
                        _dragStartWorld = worldMouse;
                    }
                    return;
                }
            }

            // 2. Check Edge for Add
            for (int i = 0; i < worldVertices.Length; i++)
            {
                var p1 = worldVertices[i];
                var p2 = worldVertices[(i + 1) % worldVertices.Length];
                
                var closest = GetClosestPointOnSegment(worldMouse, p1, p2);
                if (Vector2.Distance(worldMouse, closest) < handleSize)
                {
                    _app.RecordUndo();
                    var localNew = WorldToLocalPolygonSpace(poly, closest);
                    poly.Vertices.Insert(i + 1, localNew);
                    
                    // Immediately start dragging the new vertex
                    _app.BeginUndoAction();
                    _draggedVertexIndex = i + 1;
                    _vertexStartPos = localNew;
                    _dragStartWorld = worldMouse;
                    return;
                }
            }
        }

        if (ImGui.IsMouseDown(0) && _draggedVertexIndex != -1)
        {
            var deltaWorld = worldMouse - _dragStartWorld;
            var localDelta = WorldDeltaToLocalPolygonDelta(poly, deltaWorld);
            poly.Vertices[_draggedVertexIndex] = _vertexStartPos + localDelta;
        }

        if (ImGui.IsMouseReleased(0) && _draggedVertexIndex != -1)
        {
            _app.EndUndoAction();
            _draggedVertexIndex = -1;
        }
    }

    private Vector2 WorldToLocalPolygonSpace(PolygonShape poly, Vector2 worldPos)
    {
        var t = poly.Owner.Transform;
        float rad = -t.Rotation * MathF.PI / 180f;
        var s = t.Scale;
        if (Math.Abs(s.X) < 0.001f) s.X = 0.001f;
        if (Math.Abs(s.Y) < 0.001f) s.Y = 0.001f;

        var rel = worldPos - t.Position;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var rotated = new Vector2(rel.X * cos - rel.Y * sin, rel.X * sin + rel.Y * cos);
        return (rotated / s) - poly.Offset;
    }

    private Vector2 WorldDeltaToLocalPolygonDelta(PolygonShape poly, Vector2 worldDelta)
    {
        var t = poly.Owner.Transform;
        float rad = -t.Rotation * MathF.PI / 180f;
        var s = t.Scale;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        var rotated = new Vector2(worldDelta.X * cos - worldDelta.Y * sin, worldDelta.X * sin + worldDelta.Y * cos);
        return rotated / s;
    }

    private static Vector2 GetClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab);
        return a + Math.Clamp(t, 0, 1) * ab;
    }

    private void HandleWorldInteraction(World? world, Vector2 imgMin, Vector2 imgSize, bool hovered)
    {
        if (ImGui.IsMouseReleased(0)) { if (_isDragging || _activeHandle >= 0) _app.EndUndoAction(); _isDragging = false; _activeHandle = -1; }
        if (world == null) return;

        var io = ImGui.GetIO();
        var worldMouse = ToWorldMousePosition(imgMin, imgSize, io.MousePos);

        // Collider Editing Mode Interaction
        if (EditorSelection.IsEditingCollider && EditorSelection.SelectedEntity != null)
        {
            var poly = EditorSelection.SelectedEntity.GetComponent<PolygonShape>();
            if (poly != null)
            {
                HandleColliderInteraction(poly, worldMouse);
                if (_draggedVertexIndex != -1) return; // Consume input
            }
        }

        // Drag and Drop Blueprint Support
        if (ImGui.BeginDragDropTarget())
        {
            if (EditorSelection.DraggedAssetPath != null && EditorSelection.DraggedAssetPath.EndsWith(".blueprint"))
            {
                var fileName = Path.GetFileNameWithoutExtension(EditorSelection.DraggedAssetPath);
                DrawBlueprintPreview(worldMouse, fileName, imgMin);

                var payload = ImGui.AcceptDragDropPayload("ASSET_PATH");
                if (payload.Handle != null)
                {
                    _app.RecordUndo();
                    if (_previewEntity != null)
                    {
                        SetAlphaRecursive(_previewEntity, 1.0f);
                        _previewEntity = null; // Let it stay in world
                        _previewPath = null;
                    }
                    else
                    {
                        _app.InstantiateBlueprint(EditorSelection.DraggedAssetPath, _gridSnap ? SnapToGrid(worldMouse) : worldMouse);
                    }
                    EditorSelection.DraggedAssetPath = null;
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (!hovered) return;

        if (ImGui.IsMouseClicked(0)) {
            var selected = EditorSelection.SelectedEntity;
            if (selected != null) {
                if (_activeTool == GizmoTool.Scale) _activeHandle = HitTestScaleHandles(selected, worldMouse);
                else if (_activeTool == GizmoTool.Rotate) _activeHandle = HitTestRotateHandle(selected, worldMouse) ? 88 : -1;
                if (_activeHandle >= 0) { _app.BeginUndoAction(); _dragStartWorld = worldMouse; _entityStartPos = selected.Transform.Position; _entityStartScale = selected.Transform.Scale; _entityStartRotation = selected.Transform.Rotation; return; }
            }
            var picked = PickEntity(world, worldMouse);
            if (picked != selected) EditorSelection.SelectedEntity = picked;
            _isDragging = EditorSelection.SelectedEntity != null;
            if (_isDragging) { _app.BeginUndoAction(); _dragStartWorld = worldMouse; _entityStartPos = EditorSelection.SelectedEntity!.Transform.Position; }
        }
        if (ImGui.IsMouseDown(0) && EditorSelection.SelectedEntity != null) { if (_activeHandle == 88) HandleRotateDrag(worldMouse); else if (_activeHandle >= 0) HandleScaleDragPainterStyle(worldMouse); else if (_isDragging) HandleMoveDrag(worldMouse); }
    }

    private void DrawBlueprintPreview(Vector2 worldPos, string name, Vector2 imgMin)
    {
        var cam = _app.WorldCamera;
        var drawList = ImGui.GetWindowDrawList();
        var pos = _gridSnap ? SnapToGrid(worldPos) : worldPos;
        var screenPos = imgMin + cam.WorldToScreen(pos);
        
        uint guidelineColor = ImGui.GetColorU32(new Vector4(1, 1, 1, 0.4f));
        uint boxColor = ImGui.GetColorU32(new Vector4(0.2f, 0.8f, 1.0f, 0.7f));

        // 1. Guidelines (Screen Space)
        var winPos = ImGui.GetWindowPos();
        var winSize = ImGui.GetWindowSize();
        drawList.AddLine(new Vector2(screenPos.X, imgMin.Y), new Vector2(screenPos.X, imgMin.Y + ImGui.GetItemRectSize().Y), guidelineColor);
        drawList.AddLine(new Vector2(imgMin.X, screenPos.Y), new Vector2(imgMin.X + ImGui.GetItemRectSize().X, screenPos.Y), guidelineColor);

        // 2. Preview Box (Screen Space)
        float halfSize = (1.0f / GetWorldPixelSize()) * 0.5f; // 1 unit in pixels
        drawList.AddRect(screenPos - new Vector2(halfSize), screenPos + new Vector2(halfSize), boxColor, 0, 0, 2.0f);

        // 3. Entity Name (Label)
        var label = $"Create: {name}";
        var labelSize = ImGui.CalcTextSize(label);
        var labelPos = screenPos + new Vector2(-labelSize.X * 0.5f, -labelSize.Y - 15);
        
        drawList.AddRectFilled(labelPos - new Vector2(5, 2), labelPos + labelSize + new Vector2(5, 2), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 4f);
        drawList.AddText(labelPos, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), label);
    }

    private void HandleMoveDrag(Vector2 worldMouse) { var selected = EditorSelection.SelectedEntity; if (selected == null) return; var delta = worldMouse - _dragStartWorld; var next = _entityStartPos + delta; selected.Transform.Position = _gridSnap ? SnapToGrid(next) : next; }
    private void HandleRotateDrag(Vector2 worldMouse) { var selected = EditorSelection.SelectedEntity; if (selected == null) return; var center = selected.Transform.WorldPosition; float a1 = MathF.Atan2(worldMouse.Y - center.Y, worldMouse.X - center.X); float a2 = MathF.Atan2(_dragStartWorld.Y - center.Y, _dragStartWorld.X - center.X); selected.Transform.Rotation = _entityStartRotation + (a1 - a2) * 180f / MathF.PI; }
    private void HandleScaleDragPainterStyle(Vector2 worldMouse) { var selected = EditorSelection.SelectedEntity; if (selected == null) return; float rad = -_entityStartRotation * MathF.PI / 180f; float cos = MathF.Cos(rad), sin = MathF.Sin(rad); Vector2 ToLocal(Vector2 w) { var r = w - _entityStartPos; return new Vector2(r.X * cos - r.Y * sin, r.X * sin + r.Y * cos); } Vector2 ToWorld(Vector2 l) { float r2 = -rad; return _entityStartPos + new Vector2(l.X * MathF.Cos(r2) - l.Y * MathF.Sin(r2), l.X * MathF.Sin(r2) + l.Y * MathF.Cos(r2)); } var localMouse = ToLocal(worldMouse); var half = _entityStartScale * 0.5f; Vector2 fixedLocal = _activeHandle switch { 0 => new Vector2(half.X, -half.Y), 1 => new Vector2(0, -half.Y), 2 => new Vector2(-half.X, -half.Y), 3 => new Vector2(-half.X, 0), 4 => new Vector2(-half.X, half.Y), 5 => new Vector2(0, half.Y), 6 => new Vector2(half.X, half.Y), 7 => new Vector2(half.X, 0), _ => Vector2.Zero }; var movingLocal = localMouse; if (_activeHandle == 1 || _activeHandle == 5) movingLocal.X = 0; if (_activeHandle == 3 || _activeHandle == 7) movingLocal.Y = 0; var fixedWorld = ToWorld(fixedLocal); var movingWorld = ToWorld(movingLocal); selected.Transform.Position = (fixedWorld + movingWorld) * 0.5f; var newScale = _entityStartScale; if (_activeHandle == 0 || _activeHandle == 6 || _activeHandle == 7) newScale.X = fixedLocal.X - movingLocal.X; else if (_activeHandle == 2 || _activeHandle == 3 || _activeHandle == 4) newScale.X = movingLocal.X - fixedLocal.X; if (_activeHandle == 0 || _activeHandle == 1 || _activeHandle == 2) newScale.Y = movingLocal.Y - fixedLocal.Y; else if (_activeHandle == 4 || _activeHandle == 5 || _activeHandle == 6) newScale.Y = fixedLocal.Y - movingLocal.Y; selected.Transform.Scale = newScale; }
    private int HitTestScaleHandles(Entity e, Vector2 m) { var (c, s, r) = GetEntityBounds(e); var h = GetHandlePositions(c, s, r); float d = GetWorldPixelSize() * HandleScreenSize; for (int i = 0; i < h.Length; i++) if (Vector2.Distance(m, h[i]) < d) return i; return -1; }
    private bool HitTestRotateHandle(Entity e, Vector2 m) { var (c, s, r) = GetEntityBounds(e); float rad = r * MathF.PI / 180f; var p = c + new Vector2(-MathF.Sin(rad), MathF.Cos(rad)) * (s.Y * 0.5f + GetWorldPixelSize() * 30f); return Vector2.Distance(m, p) < GetWorldPixelSize() * HandleScreenSize * 1.5f; }
    private static Vector2[] GetHandlePositions(Vector2 c, Vector2 s, float r) { float rad = r * MathF.PI / 180f; float cos = MathF.Cos(rad), sin = MathF.Sin(rad); float hx = s.X * 0.5f, hy = s.Y * 0.5f; Vector2 Rot(float lx, float ly) => c + new Vector2(lx * cos - ly * sin, lx * sin + ly * cos); return [ Rot(-hx, hy), Rot(0, hy), Rot(hx, hy), Rot(hx, 0), Rot(hx, -hy), Rot(0, -hy), Rot(-hx, -hy), Rot(-hx, 0) ]; }
    private Entity? PickEntity(World world, Vector2 mouse) { var renderers = CollectRenderers(world); SortRenderers(renderers); for (int i = renderers.Count - 1; i >= 0; i--) if (IsPointInsideSpriteAabb(renderers[i], mouse)) return renderers[i].Owner; return PickEmptyEntity(world, mouse); }
    private Entity? PickEmptyEntity(World world, Vector2 mouse) { Entity? res = null; foreach (var e in world.RootEntities) PickEmptyEntityRecursive(e, mouse, ref res); return res; }
    private static void PickEmptyEntityRecursive(Entity e, Vector2 m, ref Entity? r) { if (!e.Active) return; if (e.GetComponent<SpriteRenderer>() == null) { var p = e.Transform.WorldPosition; var s = e.Transform.Scale * DefaultEntitySize * 0.5f; if (m.X >= p.X - MathF.Abs(s.X) && m.X <= p.X + MathF.Abs(s.X) && m.Y >= p.Y - MathF.Abs(s.Y) && m.Y <= p.Y + MathF.Abs(s.Y)) r = e; } foreach (var c in e.Transform.Children) PickEmptyEntityRecursive(c.Owner, m, ref r); }
    private static bool IsPointInsideSpriteAabb(SpriteRenderer sr, Vector2 p) { var t = sr.Owner.Transform; var wp = t.WorldPosition; var s = t.Scale; var min = wp - sr.Pivot * s; var max = wp + (Vector2.One - sr.Pivot) * s; return p.X >= MathF.Min(min.X, max.X) && p.X <= MathF.Max(min.X, max.X) && p.Y >= MathF.Min(min.Y, max.Y) && p.Y <= MathF.Max(min.Y, max.Y); }
    private static (Vector2 center, Vector2 size, float rotation) GetEntityBounds(Entity e) { var sr = e.GetComponent<SpriteRenderer>(); var t = e.Transform; var wp = t.WorldPosition; var s = t.Scale; var r = t.WorldRotation; var absS = new Vector2(MathF.Abs(s.X), MathF.Abs(s.Y)); if (sr != null) { var off = (Vector2.One * 0.5f - sr.Pivot) * s; float rad = r * MathF.PI / 180f; var roff = new Vector2(off.X * MathF.Cos(rad) - off.Y * MathF.Sin(rad), off.X * MathF.Sin(rad) + off.Y * MathF.Cos(rad)); return (wp + roff, absS, r); } return (wp, absS * DefaultEntitySize, r); }
    private float GetWorldPixelSize() { float h = _app.WorldCamera.VisibleHalfHeight * 2f; return _app.WorldCamera.ViewportHeight > 0 ? h / _app.WorldCamera.ViewportHeight : 0.01f; }
    private Vector2 ToWorldMousePosition(Vector2 min, Vector2 sz, Vector2 abs) { var l = abs - min; l.X = Math.Clamp(l.X, 0f, sz.X); l.Y = Math.Clamp(l.Y, 0f, sz.Y); return _app.WorldCamera.ScreenToWorld(l); }
    private static List<SpriteRenderer> CollectRenderers(World w) { var r = new List<SpriteRenderer>(); foreach (var e in w.RootEntities) CollectRenderersRecursive(e, r); return r; }
    private static void CollectRenderersRecursive(Entity e, List<SpriteRenderer> r) { if (!e.Active) return; var sr = e.GetComponent<SpriteRenderer>(); if (sr != null) r.Add(sr); foreach (var c in e.Transform.Children) CollectRenderersRecursive(c.Owner, r); }
    private void SortRenderers(List<SpriteRenderer> r) { r.Sort((a, b) => { int lc = SortingLayer.GetLayerIndex(a.SortingLayerName).CompareTo(SortingLayer.GetLayerIndex(b.SortingLayerName)); if (lc != 0) return lc; return a.OrderInLayer.CompareTo(b.OrderInLayer); }); }
    private Vector2 SnapToGrid(Vector2 p) => new(MathF.Round(p.X / _snapSize) * _snapSize, MathF.Round(p.Y / _snapSize) * _snapSize);
    private void HandleCameraControls() { if (!ImGui.IsWindowHovered()) return; var io = ImGui.GetIO(); var cam = _app.WorldCamera; if (io.MouseWheel != 0) { var imgMin = ImGui.GetItemRectMin(); var imgSize = ImGui.GetItemRectSize(); Vector2 mouseWorldBefore = ToWorldMousePosition(imgMin, imgSize, io.MousePos); float zoomFactor = 1.0f - io.MouseWheel * 0.1f; cam.Zoom = MathF.Max(0.01f, cam.Zoom * zoomFactor); Vector2 mouseWorldAfter = ToWorldMousePosition(imgMin, imgSize, io.MousePos); cam.Position += (mouseWorldBefore - mouseWorldAfter); } if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)) { var d = io.MouseDelta; float pixel = GetWorldPixelSize(); cam.Position -= new Vector2(d.X * pixel, -d.Y * pixel); } }
    private void HandleShortcuts() { if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)) return; var io = ImGui.GetIO(); if (io.WantCaptureKeyboard) return; if (ImGui.IsKeyPressed(ImGuiKey.F) && EditorSelection.SelectedEntity != null) _app.WorldCamera.Position = EditorSelection.SelectedEntity.Transform.WorldPosition; if (ImGui.IsKeyPressed(ImGuiKey.Delete) && EditorSelection.SelectedEntity != null) { var world = WorldManager.ActiveWorld; if (world != null) DeleteEntity(EditorSelection.SelectedEntity, world); } if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.D) && EditorSelection.SelectedEntity != null) { var world = WorldManager.ActiveWorld; if (world != null) DuplicateEntity(EditorSelection.SelectedEntity, world); } if (ImGui.IsKeyPressed(ImGuiKey.W)) _activeTool = GizmoTool.Move; if (ImGui.IsKeyPressed(ImGuiKey.E)) _activeTool = GizmoTool.Scale; if (ImGui.IsKeyPressed(ImGuiKey.R)) _activeTool = GizmoTool.Rotate; }
    private void DeleteEntity(Entity entity, World world) { if (entity.GetComponent<Camera>() != null) { Verity.Core.Debug.LogWarning($"[World] Cannot delete entity '{entity.Name}' because it has a Camera component."); return; } if (EditorSelection.SelectedEntity == entity) EditorSelection.SelectedEntity = null; if (entity.Transform.Parent != null) entity.Transform.SetParent(null, preserveWorldPosition: false); world.DestroyEntity(entity); world.ProcessPendingDestroys(); }
    private void DuplicateEntity(Entity original, World world) { void CopyRecursive(Entity src, Entity? targetParent) { var clone = world.CreateEntity(src.Name + " (Copy)"); clone.Transform.Position = src.Transform.Position; clone.Transform.Rotation = src.Transform.Rotation; clone.Transform.Scale = src.Transform.Scale; if (targetParent != null) clone.Transform.SetParent(targetParent.Transform, preserveWorldPosition: true); foreach (var comp in src.GetAllComponents()) { if (comp is Transform) continue; var cloneComp = clone.AddComponent(comp.GetType()); foreach (var prop in comp.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) { if (prop.CanRead && prop.CanWrite && prop.DeclaringType != typeof(Component)) { try { prop.SetValue(cloneComp, prop.GetValue(comp)); } catch { } } } foreach (var field in comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) { try { field.SetValue(cloneComp, field.GetValue(comp)); } catch { } } } foreach (var child in src.Transform.Children.ToArray()) CopyRecursive(child.Owner, clone); } CopyRecursive(original, original.Transform.Parent?.Owner); }
    private unsafe void DrawInputModal() { var v = ImGui.GetMainViewport(); var c = new Vector2(v.Pos.X + v.Size.X * 0.5f, v.Pos.Y + v.Size.Y * 0.5f); ImGui.SetNextWindowPos(c, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f)); if (ImGui.BeginPopupModal("WorldActionModal", null, ImGuiWindowFlags.AlwaysAutoResize)) { ImGui.Text(_activeMode == ModalMode.Create ? $"Create {_creationType}" : "Rename Asset"); ImGui.Separator(); if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere(); ImGui.InputText("Name", ref _inputBuffer, 64); var size = new Vector2(120, 0); if (ImGui.Button("OK", size) || ImGui.IsKeyPressed(ImGuiKey.Enter)) { FinalizeAction(); ImGui.CloseCurrentPopup(); } ImGui.SameLine(); if (ImGui.Button("Cancel", size)) ImGui.CloseCurrentPopup(); ImGui.EndPopup(); } }
    private void FinalizeAction() { if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer)) return; if (_activeMode == ModalMode.Create) { if (_creationType == CreationType.Script) { var p = System.IO.Path.Combine(_targetPath, _inputBuffer + ".cs"); System.IO.File.WriteAllText(p, $"using Verity.Core.ECS;\n\npublic class {_inputBuffer} : Script\n{{\n    void Start()\n    {{\n    }}\n\n    void Update()\n    {{\n    }}\n}}"); } else if (_creationType == CreationType.World) { var p = System.IO.Path.Combine(_targetPath, _inputBuffer + ".verity"); var w = new World(_inputBuffer); var camEnt = w.CreateEntity("Main Camera"); camEnt.AddComponent<Camera>(); System.IO.File.WriteAllText(p, Verity.Core.Serialization.SceneSerializer.Serialize(w)); LoadWorldByPath(p); } } }
    public void CreateWorldInProject() => OpenCreatePopup(_app.AssetsPath!, CreationType.World);
    public void LoadWorldByPath(string path) {
        if (!System.IO.File.Exists(path)) return;
        
        // Ensure we exit play mode before switching worlds
        if (_app.IsPlaying) _app.ExitPlayMode();

        var w = WorldManager.CreateOrReplaceWorld(System.IO.Path.GetFileNameWithoutExtension(path)); Verity.Core.Serialization.SceneSerializer.Deserialize(w, System.IO.File.ReadAllText(path), _app.ScriptCompiler?.CompiledAssembly); foreach (var entity in w.GetAllEntities()) { var sr = entity.GetComponent<SpriteRenderer>(); if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) { var fullPath = System.IO.Path.Combine(_app.ProjectPath!, sr.Sprite.Path); if (System.IO.File.Exists(fullPath)) sr.Texture = _app.TextureManager.Load(fullPath); } } WorldManager.SetActiveWorld(w); }
    public void SaveActiveWorldAsAsset() { if (WorldManager.ActiveWorld == null || _app.AssetsPath == null) return; var path = System.IO.Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity"); System.IO.File.WriteAllText(path, Verity.Core.Serialization.SceneSerializer.Serialize(WorldManager.ActiveWorld)); }
    public void CompileScriptsForActiveWorld() => _app.ScriptCompiler?.Compile();
    private void OpenCreatePopup(string dir, CreationType type) { _activeMode = ModalMode.Create; _creationType = type; _targetPath = dir; _inputBuffer = type == CreationType.Script ? "NewScript" : "NewWorld"; _shouldOpenPopup = true; }
}
