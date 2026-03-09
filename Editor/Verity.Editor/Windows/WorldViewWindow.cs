using System.Numerics;
using Hexa.NET.ImGui;
using Irodori.Backend.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Graphics;
using System.Diagnostics;

namespace Verity.Editor.Windows;

public class WorldViewWindow : EditorWindow
{
    public enum GizmoTool { Move, Scale, Rotate }
    private enum ModalMode { None, Create, Rename }
    private enum CreationType { Script, World, Folder }

    private readonly EditorApp _app;
    private bool _isDragging;
    private bool _gridSnap;
    private float _gridSize = 1.0f;
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

    private static readonly Vector4 SelectionColor = new(0.2f, 0.8f, 1.0f, 1.0f);
    private static readonly Vector4 HandleColor = new(1.0f, 1.0f, 1.0f, 1.0f);
    private static readonly Vector4 HandleFillColor = new(0.2f, 0.8f, 1.0f, 0.8f);
    private static readonly Vector4 RotateHandleColor = new(0.4f, 1.0f, 0.4f, 1.0f);

    public WorldViewWindow(EditorApp app) : base("World") { _app = app; }

    public override void OnGui()
    {
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
            _app.RenderPipeline.RenderWorld(world, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
            RenderEditorGizmos(world);
        }

        var colorTex = _app.RenderPipeline.WorldColorTexture;
        if (colorTex is OpenGlTexture glTex) {
            unsafe {
                var texRef = new ImTextureRef(null, new ImTextureID((nint)glTex.Id));
                ImGui.Image(texRef, contentSize, new Vector2(0, 1), new Vector2(1, 0));
            }
            HandleWorldInteraction(world, ImGui.GetItemRectMin(), ImGui.GetItemRectSize(), ImGui.IsItemHovered());
        }
        HandleCameraControls();
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
        ImGui.SetNextItemWidth(60f);
        ImGui.InputFloat("##GridSize", ref _gridSize, 0f, 0f, "%.2f");
        if (_gridSize <= 0f) _gridSize = 0.01f;
        ImGui.Separator();
    }

    private void RenderEditorGizmos(World world)
    {
        var selected = EditorSelection.SelectedEntity;
        if (selected == null || !selected.Active) return;

        var (center, size, rotation) = GetEntityBounds(selected);
        float pixel = GetWorldPixelSize();
        _app.RenderPipeline.RenderGizmoRect(center, size, rotation, pixel * 1.5f, SelectionColor, _app.WorldCamera, _app.RenderPipeline.WorldFbo);

        if (_activeTool == GizmoTool.Scale) RenderScaleHandles(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
        if (_activeTool == GizmoTool.Rotate) RenderRotateHandle(center, size, rotation, _app.WorldCamera, _app.RenderPipeline.WorldFbo);
    }

    private void RenderScaleHandles(Vector2 c, Vector2 s, float r, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        float hSize = GetWorldPixelSize() * HandleScreenSize;
        var handles = GetHandlePositions(c, s, r);
        for (int i = 0; i < handles.Length; i++) {
            var color = i == _activeHandle ? new Vector4(1f, 0.9f, 0.2f, 0.9f) : HandleFillColor;
            _app.RenderPipeline.RenderGizmoQuad(handles[i], new Vector2(hSize), color, cam, fbo);
            _app.RenderPipeline.RenderGizmoRect(handles[i], new Vector2(hSize), 0f, GetWorldPixelSize() * 1.2f, HandleColor, cam, fbo);
        }
    }

    private void RenderRotateHandle(Vector2 c, Vector2 s, float r, Camera cam, Irodori.Framebuffer.FramebufferObject.Uploaded? fbo)
    {
        float rad = r * MathF.PI / 180f;
        var pos = c + new Vector2(-MathF.Sin(rad), MathF.Cos(rad)) * (s.Y * 0.5f + GetWorldPixelSize() * 30f);
        _app.RenderPipeline.RenderGizmoQuad(pos, new Vector2(GetWorldPixelSize() * HandleScreenSize * 1.2f), RotateHandleColor, cam, fbo);
    }

    private void HandleWorldInteraction(World? world, Vector2 imgMin, Vector2 imgSize, bool hovered)
    {
        if (ImGui.IsMouseReleased(0)) { _isDragging = false; _activeHandle = -1; }
        if (world == null || !hovered) return;

        var io = ImGui.GetIO();
        var worldMouse = ToWorldMousePosition(imgMin, imgSize, io.MousePos);

        if (ImGui.IsMouseClicked(0)) {
            var selected = EditorSelection.SelectedEntity;
            if (selected != null) {
                if (_activeTool == GizmoTool.Scale) _activeHandle = HitTestScaleHandles(selected, worldMouse);
                else if (_activeTool == GizmoTool.Rotate) _activeHandle = HitTestRotateHandle(selected, worldMouse) ? 88 : -1;
                
                if (_activeHandle >= 0) {
                    _dragStartWorld = worldMouse;
                    _entityStartPos = selected.Transform.Position;
                    _entityStartScale = selected.Transform.Scale;
                    _entityStartRotation = selected.Transform.Rotation;
                    return;
                }
            }
            EditorSelection.SelectedEntity = PickEntity(world, worldMouse);
            _isDragging = EditorSelection.SelectedEntity != null;
            if (_isDragging) { 
                _dragStartWorld = worldMouse; 
                _entityStartPos = EditorSelection.SelectedEntity!.Transform.Position; 
            }
        }

        if (ImGui.IsMouseDown(0) && EditorSelection.SelectedEntity != null) {
            if (_activeHandle == 88) HandleRotateDrag(worldMouse);
            else if (_activeHandle >= 0) HandleScaleDragPainterStyle(worldMouse);
            else if (_isDragging) HandleMoveDrag(worldMouse);
        }
    }

    private void HandleMoveDrag(Vector2 worldMouse)
    {
        var selected = EditorSelection.SelectedEntity; if (selected == null) return;
        var delta = worldMouse - _dragStartWorld;
        var next = _entityStartPos + delta;
        selected.Transform.Position = _gridSnap ? SnapToGrid(next) : next;
    }

    private void HandleRotateDrag(Vector2 worldMouse)
    {
        var selected = EditorSelection.SelectedEntity; if (selected == null) return;
        var center = selected.Transform.WorldPosition;
        float a1 = MathF.Atan2(worldMouse.Y - center.Y, worldMouse.X - center.X);
        float a2 = MathF.Atan2(_dragStartWorld.Y - center.Y, _dragStartWorld.X - center.X);
        selected.Transform.Rotation = _entityStartRotation + (a1 - a2) * 180f / MathF.PI;
    }

    private void HandleScaleDragPainterStyle(Vector2 worldMouse)
    {
        var selected = EditorSelection.SelectedEntity; if (selected == null) return;
        
        // Initial state
        float rad = -_entityStartRotation * MathF.PI / 180f;
        float cos = MathF.Cos(rad), sin = MathF.Sin(rad);

        Vector2 ToLocal(Vector2 w) { var r = w - _entityStartPos; return new Vector2(r.X * cos - r.Y * sin, r.X * sin + r.Y * cos); }
        Vector2 ToWorld(Vector2 l) { float r2 = -rad; return _entityStartPos + new Vector2(l.X * MathF.Cos(r2) - l.Y * MathF.Sin(r2), l.X * MathF.Sin(r2) + l.Y * MathF.Cos(r2)); }

        var localMouse = ToLocal(worldMouse);
        var half = _entityStartScale * 0.5f;
        
        // Fixed point in local space (opposite of handle)
        Vector2 fixedLocal = _activeHandle switch {
            0 => new Vector2(half.X, -half.Y), 1 => new Vector2(0, -half.Y), 2 => new Vector2(-half.X, -half.Y),
            3 => new Vector2(-half.X, 0), 4 => new Vector2(-half.X, half.Y), 5 => new Vector2(0, half.Y),
            6 => new Vector2(half.X, half.Y), 7 => new Vector2(half.X, 0), _ => Vector2.Zero
        };

        var movingLocal = localMouse;
        if (_activeHandle == 1 || _activeHandle == 5) movingLocal.X = 0; // Center X
        if (_activeHandle == 3 || _activeHandle == 7) movingLocal.Y = 0; // Center Y

        // Fixed point in world space
        var fixedWorld = ToWorld(fixedLocal);
        var movingWorld = ToWorld(movingLocal);

        selected.Transform.Position = (fixedWorld + movingWorld) * 0.5f;
        
        // New scale depends on handle orientation
        var newScale = _entityStartScale;
        if (_activeHandle == 0 || _activeHandle == 6 || _activeHandle == 7) newScale.X = fixedLocal.X - movingLocal.X;
        else if (_activeHandle == 2 || _activeHandle == 3 || _activeHandle == 4) newScale.X = movingLocal.X - fixedLocal.X;
        
        if (_activeHandle == 0 || _activeHandle == 1 || _activeHandle == 2) newScale.Y = movingLocal.Y - fixedLocal.Y;
        else if (_activeHandle == 4 || _activeHandle == 5 || _activeHandle == 6) newScale.Y = fixedLocal.Y - movingLocal.Y;

        selected.Transform.Scale = newScale;
    }

    private int HitTestScaleHandles(Entity e, Vector2 m) {
        var (c, s, r) = GetEntityBounds(e); var h = GetHandlePositions(c, s, r);
        float d = GetWorldPixelSize() * HandleScreenSize;
        for (int i = 0; i < h.Length; i++) if (Vector2.Distance(m, h[i]) < d) return i;
        return -1;
    }

    private bool HitTestRotateHandle(Entity e, Vector2 m) {
        var (c, s, r) = GetEntityBounds(e); float rad = r * MathF.PI / 180f;
        var p = c + new Vector2(-MathF.Sin(rad), MathF.Cos(rad)) * (s.Y * 0.5f + GetWorldPixelSize() * 30f);
        return Vector2.Distance(m, p) < GetWorldPixelSize() * HandleScreenSize * 1.5f;
    }

    private static Vector2[] GetHandlePositions(Vector2 c, Vector2 s, float r) {
        float rad = r * MathF.PI / 180f; float cos = MathF.Cos(rad), sin = MathF.Sin(rad);
        float hx = s.X * 0.5f, hy = s.Y * 0.5f;
        Vector2 Rot(float lx, float ly) => c + new Vector2(lx * cos - ly * sin, lx * sin + ly * cos);
        return [ Rot(-hx, hy), Rot(0, hy), Rot(hx, hy), Rot(hx, 0), Rot(hx, -hy), Rot(0, -hy), Rot(-hx, -hy), Rot(-hx, 0) ];
    }

    private Entity? PickEntity(World world, Vector2 mouse) {
        var renderers = CollectRenderers(world); SortRenderers(renderers);
        for (int i = renderers.Count - 1; i >= 0; i--) if (IsPointInsideSpriteAabb(renderers[i], mouse)) return renderers[i].Owner;
        return PickEmptyEntity(world, mouse);
    }

    private Entity? PickEmptyEntity(World world, Vector2 mouse) {
        Entity? res = null; foreach (var e in world.RootEntities) PickEmptyEntityRecursive(e, mouse, ref res); return res;
    }

    private static void PickEmptyEntityRecursive(Entity e, Vector2 m, ref Entity? r) {
        if (!e.Active) return;
        if (e.GetComponent<SpriteRenderer>() == null) {
            var p = e.Transform.WorldPosition; var s = e.Transform.Scale * DefaultEntitySize * 0.5f;
            if (m.X >= p.X - MathF.Abs(s.X) && m.X <= p.X + MathF.Abs(s.X) && m.Y >= p.Y - MathF.Abs(s.Y) && m.Y <= p.Y + MathF.Abs(s.Y)) r = e;
        }
        foreach (var c in e.Transform.Children) PickEmptyEntityRecursive(c.Owner, m, ref r);
    }

    private static bool IsPointInsideSpriteAabb(SpriteRenderer sr, Vector2 p) {
        var t = sr.Owner.Transform; var wp = t.WorldPosition; var s = t.Scale;
        var min = wp - sr.Pivot * s; var max = wp + (Vector2.One - sr.Pivot) * s;
        return p.X >= MathF.Min(min.X, max.X) && p.X <= MathF.Max(min.X, max.X) && p.Y >= MathF.Min(min.Y, max.Y) && p.Y <= MathF.Max(min.Y, max.Y);
    }

    private static (Vector2 center, Vector2 size, float rotation) GetEntityBounds(Entity e) {
        var sr = e.GetComponent<SpriteRenderer>(); var t = e.Transform;
        var wp = t.WorldPosition; var s = t.Scale; var r = t.WorldRotation;
        var absS = new Vector2(MathF.Abs(s.X), MathF.Abs(s.Y));
        if (sr != null) {
            var off = (Vector2.One * 0.5f - sr.Pivot) * s; float rad = r * MathF.PI / 180f;
            var roff = new Vector2(off.X * MathF.Cos(rad) - off.Y * MathF.Sin(rad), off.X * MathF.Sin(rad) + off.Y * MathF.Cos(rad));
            return (wp + roff, absS, r);
        }
        return (wp, absS * DefaultEntitySize, r);
    }

    private float GetWorldPixelSize() {
        float h = _app.WorldCamera.OrthographicSize * _app.WorldCamera.Zoom * 2f;
        return _app.WorldCamera.ViewportHeight > 0 ? h / _app.WorldCamera.ViewportHeight : 0.01f;
    }

    private Vector2 ToWorldMousePosition(Vector2 min, Vector2 sz, Vector2 abs) {
        var l = abs - min; l.X = Math.Clamp(l.X, 0f, sz.X); l.Y = Math.Clamp(l.Y, 0f, sz.Y);
        return _app.WorldCamera.ScreenToWorld(l);
    }

    private static List<SpriteRenderer> CollectRenderers(World w) {
        var r = new List<SpriteRenderer>(); foreach (var e in w.RootEntities) CollectRenderersRecursive(e, r); return r;
    }

    private static void CollectRenderersRecursive(Entity e, List<SpriteRenderer> r) {
        if (!e.Active) return; var sr = e.GetComponent<SpriteRenderer>(); if (sr != null) r.Add(sr);
        foreach (var c in e.Transform.Children) CollectRenderersRecursive(c.Owner, r);
    }

    private void SortRenderers(List<SpriteRenderer> r) {
        r.Sort((a, b) => {
            int lc = SortingLayer.GetLayerIndex(a.SortingLayerName).CompareTo(SortingLayer.GetLayerIndex(b.SortingLayerName));
            if (lc != 0) return lc;
            return a.OrderInLayer.CompareTo(b.OrderInLayer);
        });
    }

    private Vector2 SnapToGrid(Vector2 p) => new(MathF.Round(p.X / _gridSize) * _gridSize, MathF.Round(p.Y / _gridSize) * _gridSize);

    private void HandleCameraControls() {
        if (!ImGui.IsWindowHovered()) return;
        var io = ImGui.GetIO();
        if (io.MouseWheel != 0) _app.WorldCamera.Zoom = MathF.Max(0.01f, _app.WorldCamera.Zoom * (1.0f - io.MouseWheel * 0.1f));
        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle)) {
            var d = io.MouseDelta; float h = _app.WorldCamera.OrthographicSize * _app.WorldCamera.Zoom * 2f;
            float s = h / _app.WorldCamera.ViewportHeight;
            _app.WorldCamera.Position -= new Vector2(d.X * s, -d.Y * s);
        }
    }

    public void CreateWorldInProject() => OpenCreatePopup(_app.AssetsPath!, CreationType.World);
    public void LoadWorldByPath(string path) {
        if (!System.IO.File.Exists(path)) return;
        var w = WorldManager.CreateOrReplaceWorld(System.IO.Path.GetFileNameWithoutExtension(path));
        Verity.Core.Serialization.SceneSerializer.Deserialize(w, System.IO.File.ReadAllText(path), _app.ScriptCompiler?.CompiledAssembly);
        
        // Re-bind textures for all sprite renderers
        foreach (var entity in w.GetAllEntities()) {
            var sr = entity.GetComponent<SpriteRenderer>();
            if (sr != null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) {
                var fullPath = System.IO.Path.Combine(_app.ProjectPath!, sr.Sprite.Path);
                if (System.IO.File.Exists(fullPath)) {
                    sr.Texture = _app.TextureManager.Load(fullPath);
                }
            }
        }

        WorldManager.SetActiveWorld(w);
    }
    public void SaveActiveWorldAsAsset() {
        if (WorldManager.ActiveWorld == null || _app.AssetsPath == null) return;
        var path = System.IO.Path.Combine(_app.AssetsPath, $"{WorldManager.ActiveWorld.Name}.verity");
        System.IO.File.WriteAllText(path, Verity.Core.Serialization.SceneSerializer.Serialize(WorldManager.ActiveWorld));
    }
    public void CompileScriptsForActiveWorld() => _app.ScriptCompiler?.Compile();
    public void BuildAndRun() { }
    private string ResolveContextDirectory() => _app.AssetsPath ?? System.AppContext.BaseDirectory;
    private void OpenCreatePopup(string dir, CreationType type) { _activeMode = ModalMode.Create; _creationType = type; _targetPath = dir; _inputBuffer = type == CreationType.Script ? "NewScript" : "NewWorld"; _shouldOpenPopup = true; }
    private void OpenRenamePopup(string path) { _activeMode = ModalMode.Rename; _targetPath = path; _inputBuffer = Path.GetFileNameWithoutExtension(path); _shouldOpenPopup = true; }
    private unsafe void DrawInputModal() {
        var v = ImGui.GetMainViewport(); var c = new Vector2(v.Pos.X + v.Size.X * 0.5f, v.Pos.Y + v.Size.Y * 0.5f);
        ImGui.SetNextWindowPos(c, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (ImGui.BeginPopupModal("WorldActionModal", null, ImGuiWindowFlags.AlwaysAutoResize)) {
            ImGui.Text(_activeMode == ModalMode.Create ? $"Create {_creationType}" : "Rename Asset");
            ImGui.Separator();
            if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();
            ImGui.InputText("Name", ref _inputBuffer, 64);
            var size = new Vector2(120, 0);
            if (ImGui.Button("OK", size) || ImGui.IsKeyPressed(ImGuiKey.Enter)) { FinalizeAction(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel", size)) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
    private void FinalizeAction() {
        if (_targetPath == null || string.IsNullOrWhiteSpace(_inputBuffer)) return;
        if (_activeMode == ModalMode.Create) {
            if (_creationType == CreationType.Script) {
                var p = System.IO.Path.Combine(_targetPath, _inputBuffer + ".cs");
                System.IO.File.WriteAllText(p, $"using Verity.Core.ECS;\n\npublic class {_inputBuffer} : Script\n{{\n    public override void Start() {{ }}\n    public override void Update() {{ }}\n}}");
            } else if (_creationType == CreationType.World) {
                var p = System.IO.Path.Combine(_targetPath, _inputBuffer + ".verity");
                var w = new World(_inputBuffer); var camEnt = w.CreateEntity("Main Camera"); camEnt.AddComponent<Camera>();
                System.IO.File.WriteAllText(p, Verity.Core.Serialization.SceneSerializer.Serialize(w));
                LoadWorldByPath(p);
            }
        }
    }
}
