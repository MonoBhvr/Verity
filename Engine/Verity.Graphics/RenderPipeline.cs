using System.Drawing;
using System.Numerics;
using Irodori.Backend.OpenGL;
using Irodori.Framebuffer;
using Irodori.Texture;
using Silk.NET.OpenGL;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Graphics;

public enum SortAxis { Y, X, Z }

public class RenderPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;
    private readonly DebugDraw _debugDraw;
    private TextureObjectUploaded? _whitePixel;

    private FramebufferObject.Uploaded? _worldFbo, _screenFbo;
    private TextureObjectUploaded? _worldColorTex, _screenColorTex;
    private int _worldFboWidth, _worldFboHeight, _screenFboWidth, _screenFboHeight;

    public SortAxis CustomSortAxis { get; set; } = SortAxis.Y;
    public bool SortAxisAscending { get; set; } = true;
    public string? BaseAssetsPath { get; set; }

    public FramebufferObject.Uploaded? WorldFbo => _worldFbo;
    public TextureObjectUploaded? WorldColorTexture => _worldColorTex;
    public FramebufferObject.Uploaded? ScreenFbo => _screenFbo;
    public TextureObjectUploaded? ScreenColorTexture => _screenColorTex;

    public RenderPipeline(GraphicsDevice device, Shader2D shader, TextureManager textureManager)
    {
        _device = device; _shader = shader; _textureManager = textureManager;
        _debugDraw = new DebugDraw(shader);
    }

    public void SetWhitePixel(TextureObjectUploaded whitePixel) { _whitePixel = whitePixel; _debugDraw.SetWhitePixel(whitePixel); }

    public unsafe void EnsureFbo(int w, int h)
    {
        if (_worldFbo != null && _worldFboWidth == w && _worldFboHeight == h) return;
        _worldFbo?.Dispose(); _worldColorTex?.Dispose();
        _worldColorTex = _device.CreateTexture().WithSize(w, h).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Nearest, ETextureFilter.Nearest).Upload(TextureData.Create((void*)null)).Unwrap();
        _worldFbo = _device.CreateFramebuffer().WithColorAttachment(_worldColorTex).Upload().Unwrap();
        _worldFboWidth = w; _worldFboHeight = h;
    }

    public unsafe void EnsureScreenFbo(int w, int h)
    {
        if (_screenFbo != null && _screenFboWidth == w && _screenFboHeight == h) return;
        _screenFbo?.Dispose(); _screenColorTex?.Dispose();
        _screenColorTex = _device.CreateTexture().WithSize(w, h).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Nearest, ETextureFilter.Nearest).Upload(TextureData.Create((void*)null)).Unwrap();
        _screenFbo = _device.CreateFramebuffer().WithColorAttachment(_screenColorTex).Upload().Unwrap();
        _screenFboWidth = w; _screenFboHeight = h;
    }

    public void RenderWorld(World world, Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        bool isWorldFbo = (_worldFbo != null && targetFbo == _worldFbo);
        bool isScreenFbo = (_screenFbo != null && targetFbo == _screenFbo);

        int targetW = isWorldFbo ? _worldFboWidth : (isScreenFbo ? _screenFboWidth : (int)_device.Window.GetWidth());
        int targetH = isWorldFbo ? _worldFboHeight : (isScreenFbo ? _screenFboHeight : (int)_device.Window.GetHeight());
        if (targetW <= 0 || targetH <= 0) return;

        // 1. 전체 화면을 레터박스 색상으로 클리어
        _device.Gl.Disable(EnableCap.ScissorTest);
        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
        _device.Clear(camera.LetterboxColor, targetFbo);

        // 2. 렌더링 영역 계산
        int vx = 0, vy = 0, vw = targetW, vh = targetH;
        float windowAspect = (float)targetW / targetH;
        float shotAspect = camera.TargetAspectRatio;

        if (!isWorldFbo && camera.FixedAspectRatio)
        {
            if (windowAspect > shotAspect) {
                vw = (int)MathF.Round(targetH * shotAspect);
                vx = (targetW - vw) / 2;
            } else {
                vh = (int)MathF.Round(targetW / shotAspect);
                vy = (targetH - vh) / 2;
            }
        }

        // 3. 뷰포트 설정 및 배경 지우기
        int fVw = Math.Max(1, vw), fVh = Math.Max(1, vh);
        if (isScreenFbo) {
            _device.Gl.Viewport(vx, vy, (uint)fVw, (uint)fVh);
            camera.SetViewportRect(vx, targetH - (vy + fVh), fVw, fVh);
            
            // Screen 뷰는 가위 테스트를 사용하여 뷰포트 내만 배경색으로 지움
            _device.Gl.Enable(EnableCap.ScissorTest);
            _device.Gl.Scissor(vx, vy, (uint)fVw, (uint)fVh);
            _device.Clear(camera.BackgroundColor, targetFbo);
            _device.Gl.Disable(EnableCap.ScissorTest);
        } else {
            _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
            camera.SetViewportRect(0, 0, targetW, targetH);
            
            // Standalone이나 WorldView는 전체를 배경색으로 지움
            _device.Clear(camera.BackgroundColor, targetFbo);
        }

        _shader.SetProjection(camera.GetProjectionMatrix(isScreenFbo ? (fVw / (float)fVh) : windowAspect));
        _shader.SetView(camera.GetViewMatrix());

        // 4. 스프라이트 렌더링
        var renderers = CollectRenderers(world);
        SortRenderers(renderers);
        foreach (var sr in renderers)
        {
            if (!sr.Enabled) continue;
            if (sr.Texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
            {
                try { string fp = ResolveAssetPath(sr.Sprite.Path); if (File.Exists(fp)) sr.Texture = _textureManager.Load(fp); } catch { }
            }
            var tex = sr.Texture ?? DefaultSprites.Square;
            if (tex == null) continue;

            if (isScreenFbo) _device.Gl.Viewport(vx, vy, (uint)fVw, (uint)fVh);
            else _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);

            _shader.SetModel(BuildModelMatrix(sr.Owner.Transform, sr));
            _shader.SetTexture(tex);
            _shader.SetColor(sr.Color);
            _shader.QuadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
        }
        _debugDraw.Render(camera, targetFbo);

        // 5. Standalone용 NDC 마스킹 (Screen 뷰는 이미 뷰포트로 잘렸으므로 불필요)
        if (camera.FixedAspectRatio && _whitePixel != null && !isWorldFbo && !isScreenFbo)
        {
            _shader.SetProjection(Matrix4x4.Identity); 
            _shader.SetView(Matrix4x4.Identity);
            _shader.SetTexture(_whitePixel);
            _shader.SetColor(camera.LetterboxColor); // 사용자 정의 레터박스 색상 사용
            var pivot = Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0);

            if (windowAspect > shotAspect) {
                float vW = shotAspect / windowAspect; float bW = 1.0f - vW; float bC = (1.0f + vW) * 0.5f;
                _shader.SetModel(pivot * Matrix4x4.CreateScale(bW, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(-bC, 0, 0));
                _shader.QuadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
                _shader.SetModel(pivot * Matrix4x4.CreateScale(bW, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(bC, 0, 0));
                _shader.QuadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
            } else if (windowAspect < shotAspect) {
                float vH = windowAspect / shotAspect; float bH = 1.0f - vH; float bC = (1.0f + vH) * 0.5f;
                _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, bH, 1.0f) * Matrix4x4.CreateTranslation(0, bC, 0));
                _shader.QuadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
                _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, bH, 1.0f) * Matrix4x4.CreateTranslation(0, -bC, 0));
                _shader.QuadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
            }
        }

        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
    }

    private string ResolveAssetPath(string p) => Path.IsPathRooted(p) ? p : (BaseAssetsPath == null ? p : Path.Combine(BaseAssetsPath, p));
    private static List<SpriteRenderer> CollectRenderers(World w) { var r = new List<SpriteRenderer>(); foreach (var e in w.RootEntities) CollectRenderersRecursive(e, r); return r; }
    private static void CollectRenderersRecursive(Entity e, List<SpriteRenderer> r) { if (!e.Active) return; var sr = e.GetComponent<SpriteRenderer>(); if (sr != null) r.Add(sr); foreach (var c in e.Transform.Children) CollectRenderersRecursive(c.Owner, r); }
    private void SortRenderers(List<SpriteRenderer> r) => r.Sort((a, b) => { int l = a.ResolvedLayerIndex.CompareTo(b.ResolvedLayerIndex); if (l != 0) return l; int o = a.OrderInLayer.CompareTo(b.OrderInLayer); if (o != 0) return o; float va = GetSortAxisValue(a.Owner.Transform), vb = GetSortAxisValue(b.Owner.Transform); return SortAxisAscending ? va.CompareTo(vb) : vb.CompareTo(va); });
    private float GetSortAxisValue(Transform t) => CustomSortAxis switch { SortAxis.X => t.WorldPosition.X, SortAxis.Y => t.WorldPosition.Y, _ => 0f };
    private static Matrix4x4 BuildModelMatrix(Transform t, SpriteRenderer sr) { var wm = t.GetWorldMatrix(); Matrix4x4.Decompose(wm, out var s, out var r, out var tr); return Matrix4x4.CreateTranslation(-sr.Pivot.X, -sr.Pivot.Y, 0) * Matrix4x4.CreateScale(s.X * (sr.FlipX ? -1f : 1f), s.Y * (sr.FlipY ? -1f : 1f), 1f) * Matrix4x4.CreateFromQuaternion(r) * Matrix4x4.CreateTranslation(tr); }
    public void RenderGizmoLine(Vector2 s, Vector2 e, float t, Verity.Core.Color c, Camera cam, FramebufferObject.Uploaded? fbo = null) { if (_whitePixel == null) return; _shader.SetProjection(cam.GetProjectionMatrix()); _shader.SetView(cam.GetViewMatrix()); var dir = e - s; float len = dir.Length(); if (len < 0.0001f) return; float ang = MathF.Atan2(dir.Y, dir.X); _shader.SetModel(Matrix4x4.CreateTranslation(0, -0.5f, 0) * Matrix4x4.CreateScale(len, t, 1f) * Matrix4x4.CreateRotationZ(ang) * Matrix4x4.CreateTranslation(s.X, s.Y, 0)); _shader.SetTexture(_whitePixel); _shader.SetColor(c); _shader.QuadBuffer.Draw(_shader.Program, fbo).Unwrap(); }
    public void RenderGizmoRect(Vector2 ctr, Vector2 sz, float rot, float lineThickness, Verity.Core.Color c, Camera cam, FramebufferObject.Uploaded? fbo = null) { float rd = rot * MathF.PI / 180f; float cs = MathF.Cos(rd), sn = MathF.Sin(rd); var hs = sz * 0.5f; Vector2 Rot(Vector2 v) => new(v.X * cs - v.Y * sn, v.X * sn + v.Y * cs); var tl = ctr + Rot(new Vector2(-hs.X, hs.Y)); var tr = ctr + Rot(new Vector2(hs.X, hs.Y)); var br = ctr + Rot(new Vector2(hs.X, -hs.Y)); var bl = ctr + Rot(new Vector2(-hs.X, -hs.Y)); RenderGizmoLine(tl, tr, lineThickness, c, cam, fbo); RenderGizmoLine(tr, br, lineThickness, c, cam, fbo); RenderGizmoLine(br, bl, lineThickness, c, cam, fbo); RenderGizmoLine(bl, tl, lineThickness, c, cam, fbo); }
    public void RenderGizmoQuad(Vector2 ctr, Vector2 sz, Verity.Core.Color c, Camera cam, FramebufferObject.Uploaded? fbo = null, TextureObjectUploaded? tex = null) { var t = tex ?? _whitePixel; if (t == null) return; _shader.SetProjection(cam.GetProjectionMatrix()); _shader.SetView(cam.GetViewMatrix()); _shader.SetModel(Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0) * Matrix4x4.CreateScale(sz.X, sz.Y, 1f) * Matrix4x4.CreateTranslation(ctr.X, ctr.Y, 0)); _shader.SetTexture(t); _shader.SetColor(c); _shader.QuadBuffer.Draw(_shader.Program, fbo).Unwrap(); }
    public void Dispose() { _worldFbo?.Dispose(); _worldColorTex?.Dispose(); _screenFbo?.Dispose(); _screenColorTex?.Dispose(); }
}
