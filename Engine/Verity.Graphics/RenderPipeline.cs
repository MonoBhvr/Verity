using System.Drawing;
using System.Numerics;
using Irodori.Backend.OpenGL;
using Irodori.Framebuffer;
using Irodori.Texture;
using Silk.NET.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Graphics;

public enum SortAxis
{
    Y,
    X,
    Z
}

public class RenderPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;

    private FramebufferObject.Uploaded? _worldFbo;
    private TextureObjectUploaded? _worldColorTex;
    private int _worldFboWidth;
    private int _worldFboHeight;

    private FramebufferObject.Uploaded? _screenFbo;
    private TextureObjectUploaded? _screenColorTex;
    private int _screenFboWidth;
    private int _screenFboHeight;

    private TextureObjectUploaded? _whitePixel;
    private readonly DebugDraw _debugDraw;

    public SortAxis CustomSortAxis { get; set; } = SortAxis.Y;
    public bool SortAxisAscending { get; set; } = true;

    // The root directory where "Assets/..." relative paths are resolved from.
    // In Editor, this is the Project folder. In Runtime, it's AppContext.BaseDirectory.
    public string? BaseAssetsPath { get; set; }

    public FramebufferObject.Uploaded? WorldFbo => _worldFbo;
    public TextureObjectUploaded? WorldColorTexture => _worldColorTex;
    public FramebufferObject.Uploaded? ScreenFbo => _screenFbo;
    public TextureObjectUploaded? ScreenColorTexture => _screenColorTex;

    public RenderPipeline(GraphicsDevice device, Shader2D shader, TextureManager textureManager)
    {
        _device = device;
        _shader = shader;
        _textureManager = textureManager;
        _debugDraw = new DebugDraw(shader);
    }

    public void SetWhitePixel(TextureObjectUploaded whitePixel)
    {
        _whitePixel = whitePixel;
        _debugDraw.SetWhitePixel(whitePixel);
    }

    public unsafe void EnsureFbo(int width, int height)
    {
        if (_worldFbo != null && _worldFboWidth == width && _worldFboHeight == height)
            return;

        _worldFbo?.Dispose();
        _worldColorTex?.Dispose();

        _worldColorTex = _device.CreateTexture()
            .WithSize(width, height)
            .WithTextureType(ETextureInternalType.Rgba8)
            .WithFilter(ETextureFilter.Nearest, ETextureFilter.Nearest)
            .Upload(TextureData.Create((void*)null))
            .Unwrap();

        _worldFbo = _device.CreateFramebuffer()
            .WithColorAttachment(_worldColorTex)
            .Upload()
            .Unwrap();

        _worldFboWidth = width;
        _worldFboHeight = height;
    }

    public unsafe void EnsureScreenFbo(int width, int height)
    {
        if (_screenFbo != null && _screenFboWidth == width && _screenFboHeight == height)
            return;

        _screenFbo?.Dispose();
        _screenColorTex?.Dispose();

        _screenColorTex = _device.CreateTexture()
            .WithSize(width, height)
            .WithTextureType(ETextureInternalType.Rgba8)
            .WithFilter(ETextureFilter.Nearest, ETextureFilter.Nearest)
            .Upload(TextureData.Create((void*)null))
            .Unwrap();

        _screenFbo = _device.CreateFramebuffer()
            .WithColorAttachment(_screenColorTex)
            .Upload()
            .Unwrap();

        _screenFboWidth = width;
        _screenFboHeight = height;
    }

    public void RenderWorld(World world, Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        // 1. 전체 타겟 크기 결정
        int targetW, targetH;
        if (targetFbo == _worldFbo) { targetW = _worldFboWidth; targetH = _worldFboHeight; }
        else if (targetFbo == _screenFbo) { targetW = _screenFboWidth; targetH = _screenFboHeight; }
        else { targetW = (int)_device.Window.GetWidth(); targetH = (int)_device.Window.GetHeight(); }

        if (targetW <= 0 || targetH <= 0) return;

        // 2. 뷰포트 및 레터박스 영역 계산
        int vx = 0, vy = 0, vw = targetW, vh = targetH;

        if (camera.FixedAspectRatio)
        {
            float targetAspect = camera.TargetAspectRatio;
            float windowAspect = (float)targetW / targetH;

            if (windowAspect > targetAspect) // 가로가 더 넓음 (Pillarbox)
            {
                vw = (int)MathF.Round(targetH * targetAspect);
                vx = (targetW - vw) / 2;
            }
            else // 세로가 더 길거나 같음 (Letterbox)
            {
                vh = (int)MathF.Round(targetW / targetAspect);
                vy = (targetH - vh) / 2;
            }
        }

        int finalVw = Math.Max(1, vw);
        int finalVh = Math.Max(1, vh);

        // 3. 카메라 정보 업데이트 (UI 상호작용 및 투영 행렬용)
        // SetViewportRect는 좌상단(0,0) 기준 좌표를 사용함
        camera.SetViewportRect(vx, (targetH - (vy + finalVh)), finalVw, finalVh);

        // 4. 배경 클리어
        // 먼저 전체 영역을 검은색으로 지움 (레터박스 바깥쪽)
        _device.Gl.Disable(EnableCap.ScissorTest);
        _device.Clear(Verity.Core.Color.Black, targetFbo);

        // 카메라 영역만 배경색으로 지움 (가위 테스트 사용)
        _device.Gl.Enable(EnableCap.ScissorTest);
        _device.Gl.Scissor(vx, vy, (uint)finalVw, (uint)finalVh);
        _device.Clear(camera.BackgroundColor, targetFbo);

        // 5. 뷰포트 설정 및 렌더링 시작
        _device.Gl.Viewport(vx, vy, (uint)finalVw, (uint)finalVh);

        var projection = camera.GetProjectionMatrix();
        var view = camera.GetViewMatrix();

        _shader.SetProjection(projection);
        _shader.SetView(view);

        var renderers = CollectRenderers(world);
        SortRenderers(renderers);

        foreach (var sr in renderers)
        {
            if (!sr.Enabled) continue;
            
            if (sr.Texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
            {
                try {
                    string fullPath = ResolveAssetPath(sr.Sprite.Path);
                    if (File.Exists(fullPath)) sr.Texture = _textureManager.Load(fullPath);
                } catch { }
            }

            var texture = sr.Texture ?? DefaultSprites.Square;
            if (texture == null) continue;

            // 라이브러리의 내부 리셋을 방지하기 위해 뷰포트 유지 확인
            _device.Gl.Viewport(vx, vy, (uint)finalVw, (uint)finalVh);
            
            _shader.SetModel(BuildModelMatrix(sr.Owner.Transform, sr));
            _shader.SetTexture(texture);
            _shader.SetColor(sr.Color);
            _shader.QuadBuffer.Draw(_shader.Program, targetFbo);
        }

        _device.Gl.Viewport(vx, vy, (uint)finalVw, (uint)finalVh);
        _debugDraw.Render(camera, targetFbo);

        // 6. 정리
        _device.Gl.Disable(EnableCap.ScissorTest);
        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
    }

    private string ResolveAssetPath(string relPath)
    {
        if (Path.IsPathRooted(relPath)) return relPath;
        if (BaseAssetsPath == null) return relPath;

        // If BaseAssetsPath points to the project folder, and relPath starts with "Assets/"
        // Path.Combine handles this correctly.
        return Path.Combine(BaseAssetsPath, relPath);
    }

    private static List<SpriteRenderer> CollectRenderers(World world)
    {
        var result = new List<SpriteRenderer>();
        foreach (var entity in world.RootEntities)
            CollectRenderersRecursive(entity, result);
        return result;
    }

    private static void CollectRenderersRecursive(Entity entity, List<SpriteRenderer> result)
    {
        if (!entity.Active) return;

        var sr = entity.GetComponent<SpriteRenderer>();
        if (sr != null)
            result.Add(sr);

        foreach (var child in entity.Transform.Children)
            CollectRenderersRecursive(child.Owner, result);
    }

    private void SortRenderers(List<SpriteRenderer> renderers)
    {
        renderers.Sort((a, b) =>
        {
            int layerCmp = a.ResolvedLayerIndex.CompareTo(b.ResolvedLayerIndex);
            if (layerCmp != 0) return layerCmp;

            int orderCmp = a.OrderInLayer.CompareTo(b.OrderInLayer);
            if (orderCmp != 0) return orderCmp;

            float axisA = GetSortAxisValue(a.Owner.Transform);
            float axisB = GetSortAxisValue(b.Owner.Transform);
            return SortAxisAscending
                ? axisA.CompareTo(axisB)
                : axisB.CompareTo(axisA);
        });
    }

    private float GetSortAxisValue(Transform transform)
    {
        var pos = transform.WorldPosition;
        return CustomSortAxis switch
        {
            SortAxis.X => pos.X,
            SortAxis.Y => pos.Y,
            _ => 0f
        };
    }

    private static Matrix4x4 BuildModelMatrix(Transform transform, SpriteRenderer sr)
    {
        // 1. Local adjustments (Pivot & Flip)
        var pivotMatrix = Matrix4x4.CreateTranslation(-sr.Pivot.X, -sr.Pivot.Y, 0);
        var flipMatrix = Matrix4x4.CreateScale(sr.FlipX ? -1f : 1f, sr.FlipY ? -1f : 1f, 1f);

        // 2. Entity transform (Scale -> Rotation -> Translation)
        var localScale = Matrix4x4.CreateScale(transform.Scale.X, transform.Scale.Y, 1f);
        var localRotation = Matrix4x4.CreateRotationZ(transform.Rotation * MathF.PI / 180f);
        var localTranslation = Matrix4x4.CreateTranslation(transform.Position.X, transform.Position.Y, 0f);

        // 3. Parent world transform (if any)
        var parentMatrix = Matrix4x4.Identity;
        if (transform.Parent != null)
        {
            parentMatrix = transform.Parent.GetWorldMatrix();
        }

        // Combine in order: Local Offset -> Local Scale -> Local Rot -> Local Trans -> Parent World
        // For System.Numerics (Row-Major), this is the order of application.
        return pivotMatrix * flipMatrix * localScale * localRotation * localTranslation * parentMatrix;
    }

    public void RenderGizmoLine(Vector2 start, Vector2 end, float thickness, Verity.Core.Color color,
        Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        if (_whitePixel == null) return;

        _shader.SetProjection(camera.GetProjectionMatrix());
        _shader.SetView(camera.GetViewMatrix());

        var dir = end - start;
        var length = dir.Length();
        if (length < 0.0001f) return;

        var angle = MathF.Atan2(dir.Y, dir.X);

        var pivotOffset = Matrix4x4.CreateTranslation(0, -0.5f, 0);
        var scale = Matrix4x4.CreateScale(length, thickness, 1f);
        var rotation = Matrix4x4.CreateRotationZ(angle);
        var translation = Matrix4x4.CreateTranslation(start.X, start.Y, 0);

        _shader.SetModel(pivotOffset * scale * rotation * translation);
        _shader.SetTexture(_whitePixel);
        _shader.SetColor(color);
        _shader.QuadBuffer.Draw(_shader.Program, targetFbo);
    }

    public void RenderGizmoRect(Vector2 center, Vector2 size, float rotationDeg, float lineThickness,
        Verity.Core.Color color, Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        float rad = rotationDeg * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);

        var halfSize = size * 0.5f;
        Vector2 Rotate(Vector2 v) => new(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);

        var tl = center + Rotate(new Vector2(-halfSize.X, halfSize.Y));
        var tr = center + Rotate(new Vector2(halfSize.X, halfSize.Y));
        var br = center + Rotate(new Vector2(halfSize.X, -halfSize.Y));
        var bl = center + Rotate(new Vector2(-halfSize.X, -halfSize.Y));

        RenderGizmoLine(tl, tr, lineThickness, color, camera, targetFbo);
        RenderGizmoLine(tr, br, lineThickness, color, camera, targetFbo);
        RenderGizmoLine(br, bl, lineThickness, color, camera, targetFbo);
        RenderGizmoLine(bl, tl, lineThickness, color, camera, targetFbo);
    }

    public void RenderGizmoQuad(Vector2 center, Vector2 size, Verity.Core.Color color,
        Camera camera, FramebufferObject.Uploaded? targetFbo = null, TextureObjectUploaded? texture = null)
    {
        var tex = texture ?? _whitePixel;
        if (tex == null) return;

        _shader.SetProjection(camera.GetProjectionMatrix());
        _shader.SetView(camera.GetViewMatrix());

        var pivotOffset = Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0);
        var scale = Matrix4x4.CreateScale(size.X, size.Y, 1f);
        var translation = Matrix4x4.CreateTranslation(center.X, center.Y, 0);

        _shader.SetModel(pivotOffset * scale * translation);
        _shader.SetTexture(tex);
        _shader.SetColor(color);
        _shader.QuadBuffer.Draw(_shader.Program, targetFbo);
    }

    public void Dispose()
    {
        _worldFbo?.Dispose();
        _worldColorTex?.Dispose();
        _screenFbo?.Dispose();
        _screenColorTex?.Dispose();
    }
}
