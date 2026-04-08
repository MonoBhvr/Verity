using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core;
using Verity.Graphics;
using System.Diagnostics;
using Verity.Core.Physics;
using Verity.Core.Engine;

namespace Verity.Editor.Windows;

public sealed class WorldViewUndoState
{
    public bool GridSnap { get; set; }
    public bool ShowGrid { get; set; } = true;
    public bool ShowGizmos { get; set; } = true;
    public float SnapSize { get; set; } = 1.0f;
    public WorldViewWindow.GizmoTool ActiveTool { get; set; } = WorldViewWindow.GizmoTool.Move;
    public CameraRenderDetail RenderDetail { get; set; } = CameraRenderDetail.Basic;
}

public unsafe class WorldViewWindow : EditorWindow
{
    private sealed class TilemapSelectionCacheEntry
    {
        public int ContentVersion;
        public Vector2 Position;
        public Vector2 Scale;
        public float Rotation;
        public List<(Vector2 start, Vector2 end)> Edges = [];
    }

    public enum GizmoTool { Move, Scale, Rotate, Rect }
    private enum ModalMode { None, Create, Rename }
    private enum CreationType { Script, World, Folder }

    private readonly EditorApp _app;
    private readonly Dictionary<Tilemap, TilemapSelectionCacheEntry> _tilemapSelectionCache = new();
    private readonly List<Entity> _renderablePickOrderCache = [];
    private bool _isDragging;
    private bool _isBoxSelecting;
    private Vector2 _boxSelectionStart;
    
    // Tilemap Editing State
    private bool _isTileBoxFilling;
    private (int x, int y) _tileBoxStart;
    private bool _isTileStrokeActive;
    private readonly HashSet<(int x, int y)> _tileStrokeTouched = new();

    private bool _gridSnap;
    private bool _showGrid = true;
    private float _snapSize = 1.0f;
    private GizmoTool _activeTool = GizmoTool.Move;
    private bool _showGizmos = true;
    private CameraRenderDetail _renderDetail = CameraRenderDetail.Basic;

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
    private int _renderablePickOrderFrame = -1;
    private World? _renderablePickOrderWorld;

    public WorldViewWindow(EditorApp app) : base(L10n.Tr("window_worldview")) { _app = app; }

    public WorldViewUndoState CaptureUndoState()
    {
        return new WorldViewUndoState
        {
            GridSnap = _gridSnap,
            ShowGrid = _showGrid,
            ShowGizmos = _showGizmos,
            SnapSize = _snapSize,
            ActiveTool = _activeTool,
            RenderDetail = _renderDetail
        };
    }

    public void RestoreUndoState(WorldViewUndoState? state)
    {
        if (state == null)
            return;

        _gridSnap = state.GridSnap;
        _showGrid = state.ShowGrid;
        _showGizmos = state.ShowGizmos;
        _snapSize = Math.Max(0.01f, state.SnapSize);
        _activeTool = state.ActiveTool;
        _renderDetail = state.RenderDetail;
    }

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
            var originalRenderDetail = _app.WorldCamera.RenderDetail;
            bool originalShowGizmos = _app.WorldCamera.ShowGizmos;
            _app.WorldCamera.BackgroundColor = _app.ProjectSettings.EditorWorldBackgroundColor;
            _app.WorldCamera.RenderDetail = _renderDetail;
            _app.WorldCamera.ShowGizmos = _showGizmos;

            var imgMin = ImGui.GetCursorScreenPos();
            bool isHovered = ImGui.IsMouseHoveringRect(imgMin, imgMin + contentSize);
            UpdatePreviewEntity(world, isHovered, imgMin);

            _app.RenderPipeline.RenderWorld(world, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            if (_showGrid)
                DrawGrid(_app.RenderPipeline.WorldFbo);
            if (_showGizmos)
                RenderEditorGizmos(world);
            
            _app.WorldCamera.BackgroundColor = originalColor;
            _app.WorldCamera.FixedAspectRatio = originalFixed;
            _app.WorldCamera.RenderDetail = originalRenderDetail;
            _app.WorldCamera.ShowGizmos = originalShowGizmos;
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

    private void UpdatePreviewEntity(World world, bool hovered, System.Numerics.Vector2 imgMin)
    {
        string? draggedPath = EditorSelection.DraggedAssetPath;
        Sprite? draggedSprite = EditorSelection.DraggedSpriteAsset;
        bool isDraggingBlueprint = draggedPath != null && draggedPath.EndsWith(".blueprint");
        bool isDraggingImage = draggedPath != null && (draggedPath.EndsWith(".png") || draggedPath.EndsWith(".jpg") || draggedPath.EndsWith(".jpeg"));
        string? previewKey = isDraggingImage
            ? $"{draggedPath}::{draggedSprite?.SpriteId ?? string.Empty}"
            : draggedPath;

        if ((isDraggingBlueprint || isDraggingImage) && hovered)
        {
            var io = ImGui.GetIO();
            var worldMouse = _app.WorldCamera.ScreenToWorld(io.MousePos - imgMin);
            var pos = _gridSnap ? SnapToGrid(worldMouse) : worldMouse;

            if (_previewEntity == null || _previewPath != previewKey)
            {
                if (_previewEntity != null) world.DestroyEntity(_previewEntity);
                
                if (isDraggingBlueprint)
                {
                    _previewEntity = _app.InstantiateBlueprint(draggedPath!);
                }
                else if (isDraggingImage && draggedPath != null)
                {
                    _previewEntity = CreateSpriteEntityFromDrag(world, draggedPath, draggedSprite);
                }

                _previewPath = previewKey;
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
            if (ImGui.Button(label)) SetActiveTool(tool);
            if (active) ImGui.PopStyleColor();
            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(key)) SetActiveTool(tool);
            ImGui.SameLine();
        }
        ToolButton($"{L10n.Tr("Gizmo_Move")} (W)", GizmoTool.Move, ImGuiKey.W);
        ToolButton($"{L10n.Tr("Gizmo_Scale")} (E)", GizmoTool.Scale, ImGuiKey.E);
        ToolButton($"{L10n.Tr("Gizmo_Rotate")} (R)", GizmoTool.Rotate, ImGuiKey.R);
        ToolButton($"{L10n.Tr("Gizmo_Rect")} (T)", GizmoTool.Rect, ImGuiKey.T);
        ImGui.Dummy(new Vector2(20, 0)); ImGui.SameLine();
        bool showGrid = _showGrid;
        if (ImGui.Checkbox(L10n.Tr("label_grid"), ref showGrid))
            SetShowGrid(showGrid);
        ImGui.SameLine();
        bool showGizmos = _showGizmos;
        if (ImGui.Checkbox(L10n.Tr("label_gizmos"), ref showGizmos))
            SetShowGizmos(showGizmos);
        ImGui.SameLine();
        bool gridSnap = _gridSnap;
        if (ImGui.Checkbox(L10n.Tr("label_snap"), ref gridSnap))
            SetGridSnap(gridSnap);
        ImGui.SameLine();
        float snapSize = _snapSize;
        ImGui.SetNextItemWidth(60f);
        if (ImGui.DragFloat("##SnapSize", ref snapSize, 0.1f, 0.01f, 100f, "S: %.2f"))
            _snapSize = Math.Max(0.01f, snapSize);
        if (ImGui.IsItemActivated())
            _app.BeginUndoAction();
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _snapSize = Math.Max(0.01f, _snapSize);
            _app.EndUndoAction();
        }
        ImGui.SameLine();
        ImGui.TextUnformatted($"{L10n.Tr("label_render_detail")}:"); ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        if (ImGui.BeginCombo("##WorldRenderDetail", GetRenderDetailLabel(_renderDetail)))
        {
            RenderRenderDetailOption(CameraRenderDetail.Outline);
            RenderRenderDetailOption(CameraRenderDetail.Basic);
            RenderRenderDetailOption(CameraRenderDetail.Lighting);
            RenderRenderDetailOption(CameraRenderDetail.PostProcess);
            ImGui.EndCombo();
        }
        ImGui.Separator();
    }

    private void RenderRenderDetailOption(CameraRenderDetail detail)
    {
        bool selected = _renderDetail == detail;
        if (ImGui.Selectable(GetRenderDetailLabel(detail), selected))
            SetRenderDetail(detail);

        if (selected)
            ImGui.SetItemDefaultFocus();
    }

    private void SetActiveTool(GizmoTool tool)
    {
        if (_activeTool == tool)
            return;

        _app.RecordUndo();
        _activeTool = tool;
    }

    private void SetShowGrid(bool value)
    {
        if (_showGrid == value)
            return;

        _app.RecordUndo();
        _showGrid = value;
    }

    private void SetShowGizmos(bool value)
    {
        if (_showGizmos == value)
            return;

        _app.RecordUndo();
        _showGizmos = value;
    }

    private void SetGridSnap(bool value)
    {
        if (_gridSnap == value)
            return;

        _app.RecordUndo();
        _gridSnap = value;
    }

    private void SetRenderDetail(CameraRenderDetail detail)
    {
        if (_renderDetail == detail)
            return;

        _app.RecordUndo();
        _renderDetail = detail;
    }

    private static string GetRenderDetailKey(CameraRenderDetail detail) => detail switch
    {
        CameraRenderDetail.Outline => "render_detail_outline",
        CameraRenderDetail.Basic => "render_detail_basic",
        CameraRenderDetail.Lighting => "render_detail_lighting",
        _ => "render_detail_postfx"
    };

    private static string GetRenderDetailLabel(CameraRenderDetail detail) => L10n.Tr(GetRenderDetailKey(detail));

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

            foreach (var shape in selected.GetComponents<PhysicalShape>())
            {
                if (!shape.Enabled) continue;
                if (shape is not TilemapShape tilemapShape) continue;
                if (_app.IsPlaying) continue;

                float gizmoPixel = GetWorldPixelSize();
                var color = shape.IsSensor ? Verity.Core.Color.Blue : Verity.Core.Color.Green;
                foreach (var polygon in tilemapShape.GetWorldPolygons())
                {
                    for (int i = 0; i < polygon.Length; i++)
                    {
                        _app.RenderPipeline.RenderGizmoLine(
                            polygon[i],
                            polygon[(i + 1) % polygon.Length],
                            gizmoPixel * 2.0f,
                            color,
                            _app.WorldCamera,
                            _app.RenderPipeline.WorldFbo);
                    }
                }
            }

            var (center, size, rotation) = GetEntityBounds(selected);
            float pixel = GetWorldPixelSize();
            bool drewCustomSelection = false;
            var tilemap = selected.GetComponent<Tilemap>();
            if (tilemap != null)
                drewCustomSelection = RenderTilemapSelection(tilemap, pixel);

            if (!drewCustomSelection)
            {
                _app.RenderPipeline.RenderGizmoRect(center, size + new Vector2(pixel * 6f), rotation, pixel * 2.5f, SelectionColor, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
                _app.RenderPipeline.RenderGizmoRect(center, size, rotation, pixel * 1.0f, Verity.Core.Color.White, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            }
            
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

    private bool RenderTilemapSelection(Tilemap tilemap, float pixel)
    {
        var edges = GetTilemapSelectionEdges(tilemap);
        if (edges.Count == 0)
            return false;

        foreach (var (start, end) in edges)
        {
            _app.RenderPipeline.RenderGizmoLine(start, end, pixel * 2.5f, SelectionColor, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            _app.RenderPipeline.RenderGizmoLine(start, end, pixel, Verity.Core.Color.White, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
        }

        return true;
    }

    private List<(Vector2 start, Vector2 end)> GetTilemapSelectionEdges(Tilemap tilemap)
    {
        if (!_tilemapSelectionCache.TryGetValue(tilemap, out var entry))
        {
            entry = new TilemapSelectionCacheEntry();
            _tilemapSelectionCache[tilemap] = entry;
        }

        var transform = tilemap.Owner?.Transform;
        Vector2 position = transform?.WorldPosition ?? Vector2.Zero;
        Vector2 scale = transform?.WorldScale ?? Vector2.One;
        float rotation = transform?.WorldRotation ?? 0f;

        if (entry.ContentVersion != tilemap.ContentVersion ||
            entry.Position != position ||
            entry.Scale != scale ||
            MathF.Abs(entry.Rotation - rotation) > 0.0001f)
        {
            entry.ContentVersion = tilemap.ContentVersion;
            entry.Position = position;
            entry.Scale = scale;
            entry.Rotation = rotation;
            entry.Edges = BuildTilemapSelectionEdges(tilemap);
        }

        return entry.Edges;
    }

    private List<(Vector2 start, Vector2 end)> BuildTilemapSelectionEdges(Tilemap tilemap)
    {
        var occupied = new HashSet<(int x, int y)>();

        foreach (var pair in tilemap.GetAllTiles())
            occupied.Add(pair.Key);

        if (occupied.Count == 0)
            return [];

        var edges = new List<(Vector2 start, Vector2 end)>();
        foreach (var (x, y) in occupied)
        {
            if (!occupied.Contains((x, y + 1)))
            {
                edges.Add((
                    tilemap.CellToWorld(x, y + 1),
                    tilemap.CellToWorld(x + 1, y + 1)));
            }

            if (!occupied.Contains((x, y - 1)))
            {
                edges.Add((
                    tilemap.CellToWorld(x, y),
                    tilemap.CellToWorld(x + 1, y)));
            }

            if (!occupied.Contains((x - 1, y)))
            {
                edges.Add((
                    tilemap.CellToWorld(x, y),
                    tilemap.CellToWorld(x, y + 1)));
            }

            if (!occupied.Contains((x + 1, y)))
            {
                edges.Add((
                    tilemap.CellToWorld(x + 1, y),
                    tilemap.CellToWorld(x + 1, y + 1)));
            }
        }

        return edges;
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
        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) || ImGui.IsMouseReleased(ImGuiMouseButton.Right)) {
            if (_isDragging || _activeHandle >= 0) _app.EndUndoAction(); 
            if (_isTileStrokeActive) EndTileStroke();
            if (_isBoxSelecting) FinalizeBoxSelection(world);
            _isDragging = false; _isBoxSelecting = false; _activeHandle = -1;
        }
        if (world == null) return;
        var worldMouse = ToWorldMousePosition(imgMin, imgSize, io.MousePos);

        // --- Tilemap Editing ---
        var selectedTileEntity = EditorSelection.SelectedEntity;
        bool tilePaletteOpen = _app.GetWindow<TilePaletteWindow>()?.IsOpen == true;
        bool tileEditActive =
            tilePaletteOpen &&
            selectedTileEntity != null &&
            (EditorSelection.SelectedTile != null ||
             EditorSelection.SelectedTool == TilemapEditor.Tool.Eraser ||
             _isTileBoxFilling ||
             _isTileStrokeActive ||
             io.MouseDown[(int)ImGuiMouseButton.Right]);

        if (tileEditActive)
        {
            var tilemap = selectedTileEntity!.GetComponent<Tilemap>();
            if (tilemap != null)
            {
                // Fix: Check if mouse is actually over this window and not captured by other UI
                bool isHovered = ImGui.IsWindowHovered();
                if (!isHovered && !_isTileBoxFilling) return;

                var (tx, ty) = tilemap.WorldToCell(worldMouse);
                bool isEraser = io.MouseDown[(int)ImGuiMouseButton.Right];
                var tool = isEraser ? TilemapEditor.Tool.Eraser : EditorSelection.SelectedTool;

                if (isHovered && (tool == TilemapEditor.Tool.Brush || tool == TilemapEditor.Tool.Eraser))
                {
                    DrawTileBrushPreview(tilemap, tx, ty, imgMin, tool == TilemapEditor.Tool.Eraser);
                }
                
                // Box Fill Logic
                if (tool == TilemapEditor.Tool.BoxFill && !isEraser)
                {
                    if (ImGui.IsMouseClicked(0) && isHovered)
                    {
                        _isTileBoxFilling = true;
                        _tileBoxStart = (tx, ty);
                        _app.BeginUndoAction();
                    }
                    
                    if (_isTileBoxFilling)
                    {
                        // Draw Preview
                        var cam = _app.WorldCamera;
                        var startWorld = tilemap.CellToWorld(_tileBoxStart.x, _tileBoxStart.y);
                        var endWorld = tilemap.CellToWorld(tx + 1, ty + 1);
                        
                        // Fix bounds
                        var minW = Vector2.Min(startWorld, tilemap.CellToWorld(tx, ty));
                        var maxW = Vector2.Max(tilemap.CellToWorld(_tileBoxStart.x + 1, _tileBoxStart.y + 1), endWorld);
                        
                        var p1 = imgMin + cam.WorldToScreen(minW);
                        var p2 = imgMin + cam.WorldToScreen(maxW);
                        
                        var dl = ImGui.GetWindowDrawList();
                        dl.AddRect(p1, p2, ImGui.GetColorU32(new Vector4(1, 1, 0, 0.8f)), 0, 0, 2f);
                        dl.AddRectFilled(p1, p2, ImGui.GetColorU32(new Vector4(1, 1, 0, 0.2f)));

                        if (ImGui.IsMouseReleased(0))
                        {
                            TilemapEditor.BoxFill(tilemap, _tileBoxStart.x, _tileBoxStart.y, tx, ty, EditorSelection.SelectedTile);
                            _isTileBoxFilling = false;
                            _app.EndUndoAction();
                        }
                        
                        // Prevent other interactions
                        if (!io.KeyCtrl) return;
                    }
                }
                else
                {
                    _isTileBoxFilling = false;
                    bool mouseDown = io.MouseDown[(int)ImGuiMouseButton.Left] || io.MouseDown[(int)ImGuiMouseButton.Right];
                    if (mouseDown && isHovered)
                    {
                        if (!_isTileStrokeActive &&
                            (ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)) &&
                            (tool == TilemapEditor.Tool.Brush || tool == TilemapEditor.Tool.Eraser))
                        {
                            _app.BeginUndoAction();
                            _isTileStrokeActive = true;
                            _tileStrokeTouched.Clear();
                        }

                        switch (tool)
                        {
                            case TilemapEditor.Tool.Brush:
                            case TilemapEditor.Tool.Eraser:
                                if (_isTileStrokeActive)
                                {
                                    ApplyTileStroke(tilemap, tx, ty, tool);
                                }
                                break;
                            case TilemapEditor.Tool.Picker: 
                                if (ImGui.IsMouseClicked(0)) {
                                    var picked = TilemapEditor.Picker(tilemap, tx, ty);
                                    if (picked != null)
                                    {
                                        var tilePalette = _app.GetWindow<TilePaletteWindow>();
                                        if (tilePalette == null || !tilePalette.TrySelectTileAsset(picked))
                                            EditorSelection.SelectedTile = picked;
                                    }
                                }
                                break;
                            case TilemapEditor.Tool.FloodFill: 
                                if (ImGui.IsMouseClicked(0))
                                {
                                    _app.RecordUndo();
                                    TilemapEditor.FloodFill(tilemap, tx, ty, EditorSelection.SelectedTile);
                                }
                                break;
                        }
                    }
                }
                if (!io.KeyCtrl) return; // Prevent entity selection if editing tilemap unless Ctrl is held
            }
        }

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
                    EditorSelection.ClearAssetDrag();
                }
            }
            else if (EditorSelection.DraggedAssetPath != null && IsImageAsset(EditorSelection.DraggedAssetPath))
            {
                if (ImGui.AcceptDragDropPayload("ASSET_PATH").Handle != null)
                {
                    _app.RecordUndo();
                    if (_previewEntity != null)
                    {
                        SetAlphaRecursive(_previewEntity, 1.0f);
                        _previewEntity = null;
                        _previewPath = null;
                    }
                    else
                    {
                        var created = CreateSpriteEntityFromDrag(world, EditorSelection.DraggedAssetPath, EditorSelection.DraggedSpriteAsset);
                        created.Transform.Position = _gridSnap ? SnapToGrid(worldMouse) : worldMouse;
                    }

                    EditorSelection.ClearAssetDrag();
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (!hovered && !_isDragging && !_isBoxSelecting) return;

        if (ImGui.IsMouseDoubleClicked(0))
        {
            var doubleClickCandidates = GetPickCandidates(world, worldMouse);
            var doubleClicked = GetDoubleClickCandidate(doubleClickCandidates, worldMouse);
            ApplySingleSelection(doubleClicked);
            return;
        }

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
            if (!io.KeyCtrl)
            {
                var selectedDragTarget = GetDirectDragTargetFromSelection(worldMouse);
                if (selectedDragTarget != null)
                {
                    _isDragging = true;
                    _app.BeginUndoAction();
                    _dragStartWorld = worldMouse;
                    _draggedEntities.Clear();
                    foreach (var ent in EditorSelection.SelectedEntities)
                        _draggedEntities.Add((ent, ent.Transform.WorldPosition, ent.Transform.WorldScale, ent.Transform.WorldRotation));
                    return;
                }
            }

            var candidates = GetPickCandidates(world, worldMouse);
            var picked = GetDragTargetFromSelection(candidates);
            if (picked != null && !io.KeyCtrl)
            {
                _isDragging = true;
                _app.BeginUndoAction();
                _dragStartWorld = worldMouse;
                _draggedEntities.Clear();
                foreach (var ent in EditorSelection.SelectedEntities)
                    _draggedEntities.Add((ent, ent.Transform.WorldPosition, ent.Transform.WorldScale, ent.Transform.WorldRotation));
                return;
            }

            if (picked == null)
            {
                picked = GetTopmostCandidate(candidates);
            }

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
        Vector2 imgMin = ImGui.GetItemRectMin(); var cam = _app.WorldCamera;
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
        var label = $"{L10n.Tr("btn_add")}: {name}"; 
        Vector2 labelSize = ImGui.CalcTextSize(label); 
        Vector2 labelPos = screenPos + new Vector2(-labelSize.X * 0.5f, -labelSize.Y - 15);
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
    private static Entity? GetTopmostCandidate(List<Entity> candidates)
        => candidates.Count > 0 ? candidates[0] : null;

    private Entity? GetDoubleClickCandidate(List<Entity> candidates, Vector2 mouse)
    {
        if (candidates.Count == 0)
            return null;

        var selected = EditorSelection.SelectedEntity;
        if (selected == null)
            return GetTopmostCandidate(candidates);

        int currentIndex = candidates.IndexOf(selected);
        if (currentIndex < 0)
            return GetTopmostCandidate(candidates);

        return candidates[(currentIndex + 1) % candidates.Count];
    }

    private static Entity? GetDragTargetFromSelection(List<Entity> candidates)
    {
        var primary = EditorSelection.SelectedEntity;
        if (primary != null && candidates.Contains(primary))
            return primary;

        foreach (var candidate in candidates)
        {
            if (EditorSelection.IsSelected(candidate))
                return candidate;
        }

        return null;
    }

    private Entity? GetDirectDragTargetFromSelection(Vector2 mouse)
    {
        var primary = EditorSelection.SelectedEntity;
        if (primary != null && IsPointInsideEntity(primary, mouse))
            return primary;

        foreach (var entity in EditorSelection.SelectedEntities)
        {
            if (entity != primary && IsPointInsideEntity(entity, mouse))
                return entity;
        }

        return null;
    }

    private bool IsPointInsideEntity(Entity entity, Vector2 point)
    {
        if (!entity.Active)
            return false;

        if (TryIsPointInsideTilemap(entity, point, out bool tilemapHit))
            return tilemapHit;

        if (TryGetEntityWorldAabb(entity, out var min, out var max))
            return IsPointInsideAabb(min, max, point);

        var position = entity.Transform.WorldPosition;
        var halfSize = entity.Transform.Scale * DefaultEntitySize * 0.5f;
        return point.X >= position.X - MathF.Abs(halfSize.X) &&
               point.X <= position.X + MathF.Abs(halfSize.X) &&
               point.Y >= position.Y - MathF.Abs(halfSize.Y) &&
               point.Y <= position.Y + MathF.Abs(halfSize.Y);
    }

    private static bool TryIsPointInsideTilemap(Entity entity, Vector2 point, out bool hit)
    {
        var tilemap = entity.GetComponent<Tilemap>();
        if (tilemap == null)
        {
            hit = false;
            return false;
        }

        var cell = tilemap.WorldToCell(point);
        hit = tilemap.HasTile(cell.x, cell.y);
        return true;
    }

    private static void ApplySingleSelection(Entity? entity)
    {
        if (entity == null)
            EditorSelection.ClearSelection();
        else
            EditorSelection.SelectedEntity = entity;
    }

    private List<Entity> GetPickCandidates(World world, Vector2 mouse)
    {
        var candidates = new List<Entity>();
        foreach (var entity in CollectRenderableEntitiesInRenderOrder(world).AsEnumerable().Reverse())
        {
            if (IsPointInsideEntity(entity, mouse) && !candidates.Contains(entity))
                candidates.Add(entity);
        }

        var emptyEntities = new List<Entity>();
        foreach (var entity in world.RootEntities)
            CollectEmptyPickCandidates(entity, mouse, emptyEntities);

        for (int i = emptyEntities.Count - 1; i >= 0; i--)
            candidates.Add(emptyEntities[i]);

        return candidates;
    }

    private void CollectEmptyPickCandidates(Entity e, Vector2 m, List<Entity> results)
    {
        if (!e.Active) return;

        if (TryGetEntityWorldAabb(e, out var min, out var max))
        {
            if (IsPointInsideAabb(min, max, m))
                results.Add(e);
        }
        else
        {
            var p = e.Transform.WorldPosition;
            var s = e.Transform.Scale * DefaultEntitySize * 0.5f;
            if (m.X >= p.X - MathF.Abs(s.X) && m.X <= p.X + MathF.Abs(s.X) && m.Y >= p.Y - MathF.Abs(s.Y) && m.Y <= p.Y + MathF.Abs(s.Y))
                results.Add(e);
        }

        foreach (var c in e.Transform.Children)
            CollectEmptyPickCandidates(c.Owner, m, results);
    }

    private static bool IsPointInsideAabb(Vector2 min, Vector2 max, Vector2 p)
    {
        return p.X >= MathF.Min(min.X, max.X) && p.X <= MathF.Max(min.X, max.X) &&
               p.Y >= MathF.Min(min.Y, max.Y) && p.Y <= MathF.Max(min.Y, max.Y);
    }

    private (Vector2 center, Vector2 size, float rotation) GetEntityBounds(Entity e) 
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

            var pivot = GetResolvedPivot(sr);
            var off = (Vector2.One * 0.5f - pivot) * (s * sr.Size); 
            float rad = r * MathF.PI / 180f; 
            var roff = new Vector2(off.X * MathF.Cos(rad) - off.Y * MathF.Sin(rad), off.X * MathF.Sin(rad) + off.Y * MathF.Cos(rad)); 
            return (wp + roff, effS, r); 
        }

        if (TryGetEntityWorldAabb(e, out var min, out var max))
        {
            var size = max - min;
            if (MathF.Abs(size.X) < 0.0001f) size.X = 0.0001f;
            if (MathF.Abs(size.Y) < 0.0001f) size.Y = 0.0001f;
            return ((min + max) * 0.5f, new Vector2(MathF.Abs(size.X), MathF.Abs(size.Y)), 0f);
        }

        var absS = new Vector2(MathF.Max(0.0001f, MathF.Abs(s.X)), MathF.Max(0.0001f, MathF.Abs(s.Y))); 
        return (wp, absS * DefaultEntitySize, r); 
    }

    private bool TryGetEntityWorldAabb(Entity entity, out Vector2 min, out Vector2 max)
    {
        if (TryGetSpriteWorldAabb(entity, out min, out max))
            return true;

        if (TryGetTilemapWorldAabb(entity, out min, out max))
            return true;

        if (TryGetPolygonWorldAabb(entity, out min, out max))
            return true;

        if (TryGetPhysicalShapeWorldAabb(entity, out min, out max))
            return true;

        min = max = Vector2.Zero;
        return false;
    }

    private bool TryGetSpriteWorldAabb(Entity entity, out Vector2 min, out Vector2 max)
    {
        var spriteRenderer = entity.GetComponent<SpriteRenderer>();
        if (spriteRenderer == null || !spriteRenderer.Enabled)
        {
            min = max = Vector2.Zero;
            return false;
        }

        var transform = entity.Transform;
        var scale = transform.WorldScale * spriteRenderer.Size;
        var pivot = GetResolvedPivot(spriteRenderer);
        Vector2[] corners =
        [
            new(-pivot.X * scale.X, -pivot.Y * scale.Y),
            new((1f - pivot.X) * scale.X, -pivot.Y * scale.Y),
            new((1f - pivot.X) * scale.X, (1f - pivot.Y) * scale.Y),
            new(-pivot.X * scale.X, (1f - pivot.Y) * scale.Y)
        ];

        float radians = transform.WorldRotation * MathF.PI / 180f;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        for (int i = 0; i < corners.Length; i++)
        {
            var local = corners[i];
            corners[i] = transform.WorldPosition + new Vector2(
                local.X * cos - local.Y * sin,
                local.X * sin + local.Y * cos);
        }

        return TryCalculateAabb(corners, out min, out max);
    }

    private bool TryGetTilemapWorldAabb(Entity entity, out Vector2 min, out Vector2 max)
    {
        var tilemap = entity.GetComponent<Tilemap>();
        if (tilemap == null || !tilemap.TryGetTileBounds(out int minX, out int minY, out int maxX, out int maxY))
        {
            min = max = Vector2.Zero;
            return false;
        }

        Vector2[] corners =
        [
            tilemap.CellToWorld(minX, minY),
            tilemap.CellToWorld(maxX + 1, minY),
            tilemap.CellToWorld(maxX + 1, maxY + 1),
            tilemap.CellToWorld(minX, maxY + 1)
        ];

        return TryCalculateAabb(corners, out min, out max);
    }

    private bool TryGetPolygonWorldAabb(Entity entity, out Vector2 min, out Vector2 max)
    {
        var polygonRenderer = entity.GetComponent<PolygonRenderer>();
        if (polygonRenderer != null && TryCalculateAabb(polygonRenderer.GetWorldVertices(), out min, out max))
            return true;

        var polygonShape = entity.GetComponent<PolygonShape>();
        if (polygonShape != null && TryCalculateAabb(polygonShape.GetVertices(), out min, out max))
            return true;

        min = max = Vector2.Zero;
        return false;
    }

    private bool TryGetPhysicalShapeWorldAabb(Entity entity, out Vector2 min, out Vector2 max)
    {
        foreach (var shape in entity.GetComponents<PhysicalShape>())
        {
            if (!shape.Enabled) continue;

            var aabb = shape.GetAABB();
            if (aabb.IsDefault()) continue;

            min = aabb.Min;
            max = aabb.Max;
            return true;
        }

        min = max = Vector2.Zero;
        return false;
    }

    private static bool TryCalculateAabb(IReadOnlyList<Vector2> points, out Vector2 min, out Vector2 max)
    {
        if (points.Count == 0)
        {
            min = max = Vector2.Zero;
            return false;
        }

        min = points[0];
        max = points[0];
        for (int i = 1; i < points.Count; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }

        return true;
    }

    private Entity CreateSpriteEntityFromDrag(World world, string draggedPath, Sprite? draggedSprite)
    {
        var entity = world.CreateEntity(Path.GetFileNameWithoutExtension(draggedPath) ?? "New Entity");
        var sr = entity.AddComponent<SpriteRenderer>();
        sr.Sprite = draggedSprite ?? _app.CreateSpriteReference(draggedPath);
        sr.Texture = _app.LoadSpriteTexture(sr.Sprite);
        sr.Size = _app.GetDefaultSpriteWorldSize(sr.Sprite);
        sr.Pivot = _app.GetDefaultSpritePivot(sr.Sprite);
        _app.AttachToBlueprintDefaultParent(entity);
        return entity;
    }

    private static bool IsImageAsset(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg";
    }
    private Vector2 GetResolvedPivot(SpriteRenderer sr) => sr.UseSpritePivot ? _app.GetDefaultSpritePivot(sr.Sprite) : sr.Pivot;
    private float GetWorldPixelSize() { float h = _app.WorldCamera.VisibleHalfHeight * 2f; return _app.WorldCamera.ViewportHeight > 0 ? h / _app.WorldCamera.ViewportHeight : 0.01f; }
    private Vector2 ToWorldMousePosition(Vector2 min, Vector2 sz, Vector2 abs) { var l = abs - min; l.X = Math.Clamp(l.X, 0f, sz.X); l.Y = Math.Clamp(l.Y, 0f, sz.Y); return _app.WorldCamera.ScreenToWorld(l); }
    private List<Entity> CollectRenderableEntitiesInRenderOrder(World world)
    {
        if (ReferenceEquals(_renderablePickOrderWorld, world) && _renderablePickOrderFrame == Time.FrameCount)
            return _renderablePickOrderCache;

        _renderablePickOrderCache.Clear();
        var hierarchyOrder = world.GetAllEntities()
            .Select((entity, index) => (entity, index))
            .ToDictionary(pair => pair.entity, pair => pair.index);

        var renderables = new List<Component>();
        foreach (var entity in world.GetAllEntities().Where(static entity => entity.Active))
        {
            if (entity.GetComponent<SpriteRenderer>() is SpriteRenderer sr && sr.Enabled)
                renderables.Add(sr);
            if (entity.GetComponent<TilemapRenderer>() is TilemapRenderer tr && tr.Enabled)
                renderables.Add(tr);
            if (entity.GetComponent<PolygonRenderer>() is PolygonRenderer pr && pr.Enabled)
                renderables.Add(pr);
        }

        renderables.Sort((a, b) =>
        {
            int la = GetLayerIndexForPicking(a);
            int lb = GetLayerIndexForPicking(b);
            int lc = la.CompareTo(lb);
            if (lc != 0) return lc;

            int oa = a is SpriteRenderer srA2 ? srA2.OrderInLayer : (a is TilemapRenderer trA2 ? trA2.OrderInLayer : ((PolygonRenderer)a).OrderInLayer);
            int ob = b is SpriteRenderer srB2 ? srB2.OrderInLayer : (b is TilemapRenderer trB2 ? trB2.OrderInLayer : ((PolygonRenderer)b).OrderInLayer);
            int oc = oa.CompareTo(ob);
            if (oc != 0) return oc;

            int ha = hierarchyOrder.GetValueOrDefault(a.Owner, int.MaxValue);
            int hb = hierarchyOrder.GetValueOrDefault(b.Owner, int.MaxValue);
            int hc = ha.CompareTo(hb);
            if (hc != 0) return hc;

            float va = GetSortAxisValueForPicking(a.Owner.Transform);
            float vb = GetSortAxisValueForPicking(b.Owner.Transform);
            int vc = _app.RenderPipeline.SortAxisAscending ? va.CompareTo(vb) : vb.CompareTo(va);
            return vc != 0 ? vc : a.Owner.Id.CompareTo(b.Owner.Id);
        });

        foreach (Component renderable in renderables)
            _renderablePickOrderCache.Add(renderable.Owner);

        _renderablePickOrderWorld = world;
        _renderablePickOrderFrame = Time.FrameCount;
        return _renderablePickOrderCache;
    }

    private float GetSortAxisValueForPicking(Transform transform) => _app.RenderPipeline.CustomSortAxis switch
    {
        SortAxis.X => transform.WorldPosition.X,
        SortAxis.Y => transform.WorldPosition.Y,
        _ => 0f
    };

    private static int GetLayerIndexForPicking(Component component) => component switch
    {
        SpriteRenderer sr => Verity.Graphics.SortingLayer.GetLayerIndex(sr.SortingLayerName),
        TilemapRenderer tr => Verity.Graphics.SortingLayer.GetLayerIndex(tr.SortingLayerName),
        PolygonRenderer pr => Verity.Graphics.SortingLayer.GetLayerIndex(pr.SortingLayerName),
        _ => 0
    };
    private void ApplyTileStroke(Tilemap tilemap, int tx, int ty, TilemapEditor.Tool tool)
    {
        var cells = TilemapEditor.GetBrushCells(tx, ty, EditorSelection.TileBrushSize, EditorSelection.TileBrushShape);
        foreach (var cell in cells)
        {
            if (!_tileStrokeTouched.Add(cell)) continue;
            tilemap.SetTile(cell.x, cell.y, tool == TilemapEditor.Tool.Eraser ? null : EditorSelection.SelectedTile);
        }
    }

    private void EndTileStroke()
    {
        _app.EndUndoAction();
        _isTileStrokeActive = false;
        _tileStrokeTouched.Clear();
    }

    private void DrawTileBrushPreview(Tilemap tilemap, int tx, int ty, Vector2 imgMin, bool erasing)
    {
        var cam = _app.WorldCamera;
        var dl = ImGui.GetWindowDrawList();
        var lineColor = erasing ? new Vector4(1f, 0.4f, 0.4f, 0.95f) : new Vector4(0.4f, 0.8f, 1f, 0.95f);
        var fillColor = erasing ? new Vector4(1f, 0.35f, 0.35f, 0.18f) : new Vector4(0.35f, 0.75f, 1f, 0.18f);

        foreach (var cell in TilemapEditor.GetBrushCells(tx, ty, EditorSelection.TileBrushSize, EditorSelection.TileBrushShape))
        {
            var startWorld = tilemap.CellToWorld(cell.x, cell.y);
            var endWorld = tilemap.CellToWorld(cell.x + 1, cell.y + 1);
            var minW = Vector2.Min(startWorld, endWorld);
            var maxW = Vector2.Max(startWorld, endWorld);

            var p1 = imgMin + cam.WorldToScreen(minW);
            var p2 = imgMin + cam.WorldToScreen(maxW);

            dl.AddRectFilled(p1, p2, ImGui.GetColorU32(fillColor));
            dl.AddRect(p1, p2, ImGui.GetColorU32(lineColor), 0f, 0, 1.5f);
        }
    }
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
                if (System.IO.File.Exists(fullPath)) sr.Texture = _app.LoadSpriteTexture(sr.Sprite); 
            } 
        } 
        WorldManager.SetActiveWorld(w); 
    }
    public void SaveActiveWorldAsAsset() { if (WorldManager.ActiveWorld == null || _app.AssetsPath == null) return; var path = System.IO.Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity"); System.IO.File.WriteAllText(path, Verity.Core.Serialization.SceneSerializer.Serialize(WorldManager.ActiveWorld)); }
    public void CompileScriptsForActiveWorld() => _app.ScriptCompiler?.Compile();
    private void OpenCreatePopup(string dir, CreationType type) { _activeMode = ModalMode.Create; _creationType = type; _targetPath = dir; _inputBuffer = type == CreationType.Script ? "NewScript" : "NewWorld"; _shouldOpenPopup = true; }
}
