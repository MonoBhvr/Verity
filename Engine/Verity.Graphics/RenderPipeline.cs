using System.Drawing;
using System.Numerics;
using Irodori.Backend.OpenGL;
using Irodori.Framebuffer;
using Irodori.Texture;
using Silk.NET.OpenGL;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verity.Core;
using Verity.Core.Engine;
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
    private readonly Irodori.Buffer.VertexBuffer.Uploaded _quadBuffer;
    private readonly Irodori.Buffer.VertexBuffer.Unuploaded _dynamicBuffer;
    private TextureObjectUploaded? _whitePixel;

    private readonly Dictionary<string, Shader2D> _shaderCache = new();
    private readonly Dictionary<string, StyleRuntime> _styleCache = new();

    private FramebufferObject.Uploaded? _worldFbo, _screenFbo;
    private TextureObjectUploaded? _worldColorTex, _screenColorTex;
    private int _worldFboWidth, _worldFboHeight, _screenFboWidth, _screenFboHeight;

    // Post-processing
    private Shader2D? _copyShader, _brightExtractShader, _blurShader, _compositeShader;
    private FramebufferObject.Uploaded? _ppSceneFbo, _ppTempFbo1, _ppTempFbo2, _ppHistoryFbo, _ppBloomFbo1, _ppBloomFbo2;
    private TextureObjectUploaded? _ppSceneTex, _ppTempTex1, _ppTempTex2, _ppHistoryTex, _ppBloomTex1, _ppBloomTex2;
    private int _ppW, _ppH;
    private int _ppBloomDownsample = 2;
    private bool _ppHistoryValid;
    private Guid? _ppHistoryCameraId;

    public SortAxis CustomSortAxis { get; set; } = SortAxis.Y;
    public bool SortAxisAscending { get; set; } = true;
    public static string? BaseAssetsPath { get; set; }

    public FramebufferObject.Uploaded? WorldFbo => _worldFbo;
    public TextureObjectUploaded? WorldColorTexture => _worldColorTex;
    public FramebufferObject.Uploaded? ScreenFbo => _screenFbo;
    public TextureObjectUploaded? ScreenColorTexture => _screenColorTex;

    public RenderPipeline(GraphicsDevice device, Shader2D shader, TextureManager textureManager)
    {
        _device = device; _shader = shader; _textureManager = textureManager;
        _quadBuffer = CreateQuadBuffer(device);
        
        var format = Irodori.Buffer.VertexBufferFormat.Create()
            .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2())  // aPosition
            .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2()); // aTexCoord
        _dynamicBuffer = device.CreateVertexBuffer(format);

        _debugDraw = new DebugDraw(shader, _quadBuffer);

        // Initialize post-processing shaders.
        _copyShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.CopyFragment);
        _brightExtractShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.BrightExtractFragment);
        _blurShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.BlurFragment);
        _compositeShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.CompositeFragment);
    }

    private static Irodori.Buffer.VertexBuffer.Uploaded CreateQuadBuffer(GraphicsDevice device)
    {
        var format = Irodori.Buffer.VertexBufferFormat.Create()
            .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2())  // aPosition
            .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2()); // aTexCoord

        var data = Irodori.Buffer.IVertexData.Create<Vector2, Vector2>();
        data.AddVertex(new Vector2(0, 0), new Vector2(0, 0)); // top-left
        data.AddVertex(new Vector2(1, 0), new Vector2(1, 0)); // top-right
        data.AddVertex(new Vector2(0, 1), new Vector2(0, 1)); // bottom-left
        data.AddVertex(new Vector2(1, 1), new Vector2(1, 1)); // bottom-right

        var indices = new int[] { 0, 2, 1, 1, 2, 3 };

        var buffer = device.CreateVertexBuffer(format);
        return buffer.Upload(data, indices).Unwrap();
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

    private unsafe void EnsurePostProcessFbos(int w, int h, int bloomDownsample)
    {
        bloomDownsample = Math.Max(1, bloomDownsample);
        if (_ppSceneFbo != null && _ppW == w && _ppH == h && _ppBloomDownsample == bloomDownsample) return;
        _ppSceneFbo?.Dispose(); _ppSceneTex?.Dispose();
        _ppTempFbo1?.Dispose(); _ppTempTex1?.Dispose();
        _ppTempFbo2?.Dispose(); _ppTempTex2?.Dispose();
        _ppHistoryFbo?.Dispose(); _ppHistoryTex?.Dispose();
        _ppBloomFbo1?.Dispose(); _ppBloomTex1?.Dispose();
        _ppBloomFbo2?.Dispose(); _ppBloomTex2?.Dispose();

        _ppSceneTex = _device.CreateTexture().WithSize(w, h).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Linear, ETextureFilter.Linear).Upload(TextureData.Create((void*)null)).Unwrap();
        _ppSceneFbo = _device.CreateFramebuffer().WithColorAttachment(_ppSceneTex).Upload().Unwrap();

        _ppTempTex1 = _device.CreateTexture().WithSize(w, h).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Linear, ETextureFilter.Linear).Upload(TextureData.Create((void*)null)).Unwrap();
        _ppTempFbo1 = _device.CreateFramebuffer().WithColorAttachment(_ppTempTex1).Upload().Unwrap();

        _ppTempTex2 = _device.CreateTexture().WithSize(w, h).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Linear, ETextureFilter.Linear).Upload(TextureData.Create((void*)null)).Unwrap();
        _ppTempFbo2 = _device.CreateFramebuffer().WithColorAttachment(_ppTempTex2).Upload().Unwrap();

        _ppHistoryTex = _device.CreateTexture().WithSize(w, h).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Linear, ETextureFilter.Linear).Upload(TextureData.Create((void*)null)).Unwrap();
        _ppHistoryFbo = _device.CreateFramebuffer().WithColorAttachment(_ppHistoryTex).Upload().Unwrap();

        int bw = Math.Max(1, w / bloomDownsample);
        int bh = Math.Max(1, h / bloomDownsample);
        _ppBloomTex1 = _device.CreateTexture().WithSize(bw, bh).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Linear, ETextureFilter.Linear).Upload(TextureData.Create((void*)null)).Unwrap();
        _ppBloomFbo1 = _device.CreateFramebuffer().WithColorAttachment(_ppBloomTex1).Upload().Unwrap();

        _ppBloomTex2 = _device.CreateTexture().WithSize(bw, bh).WithTextureType(ETextureInternalType.Rgba8).WithFilter(ETextureFilter.Linear, ETextureFilter.Linear).Upload(TextureData.Create((void*)null)).Unwrap();
        _ppBloomFbo2 = _device.CreateFramebuffer().WithColorAttachment(_ppBloomTex2).Upload().Unwrap();

        _ppW = w; _ppH = h;
        _ppBloomDownsample = bloomDownsample;
        _ppHistoryValid = false;
    }

    public void RenderWorld(World world, Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        bool isWorldFbo = (_worldFbo != null && targetFbo == _worldFbo);
        bool isScreenFbo = (_screenFbo != null && targetFbo == _screenFbo);

        int targetW = isWorldFbo ? _worldFboWidth : (isScreenFbo ? _screenFboWidth : (int)_device.Window.GetWidth());
        int targetH = isWorldFbo ? _worldFboHeight : (isScreenFbo ? _screenFboHeight : (int)_device.Window.GetHeight());
        if (targetW <= 0 || targetH <= 0) return;

        bool usePostProcess = camera.PostProcess.Enabled;
        Guid currentCameraId = camera.Owner?.Id ?? Guid.Empty;
        if (_ppHistoryCameraId != currentCameraId)
        {
            _ppHistoryCameraId = currentCameraId;
            _ppHistoryValid = false;
        }

        if (!usePostProcess)
            _ppHistoryValid = false;

        var actualTargetFbo = targetFbo;
        if (usePostProcess)
        {
            EnsurePostProcessFbos(targetW, targetH, Math.Max(1, camera.PostProcess.Bloom.Downsample));
            actualTargetFbo = _ppSceneFbo;
        }

        _device.Gl.Disable(EnableCap.ScissorTest);
        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
        _device.Clear(camera.LetterboxColor, actualTargetFbo);

        int vx = 0, vy = 0, vw = targetW, vh = targetH;
        float windowAspect = targetH > 0 ? (float)targetW / targetH : 1f;
        float shotAspect = MathF.Max(0.01f, camera.TargetAspectRatio);

        if (!isWorldFbo && camera.FixedAspectRatio)
        {
            if (windowAspect > shotAspect) { vw = (int)MathF.Round(targetH * shotAspect); vx = (targetW - vw) / 2; }
            else { vh = (int)MathF.Round(targetW / shotAspect); vy = (targetH - vh) / 2; }
        }

        int fVw = Math.Max(1, vw), fVh = Math.Max(1, vh);
        if (isScreenFbo) {
            _device.Gl.Viewport(vx, vy, (uint)fVw, (uint)fVh);
            camera.SetViewportRect(vx, targetH - (vy + fVh), fVw, fVh);
            _device.Gl.Enable(EnableCap.ScissorTest);
            _device.Gl.Scissor(vx, vy, (uint)fVw, (uint)fVh);
            _device.Clear(camera.BackgroundColor, actualTargetFbo);
            _device.Gl.Disable(EnableCap.ScissorTest);
        } else {
            _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
            camera.SetViewportRect(0, 0, targetW, targetH);
            _device.Clear(camera.BackgroundColor, actualTargetFbo);
        }

        var projection = camera.GetProjectionMatrix(isScreenFbo ? (fVw / (float)fVh) : windowAspect);
        var view = camera.GetViewMatrix();

        var allRenderers = CollectAllSortedRenderers(world);

        foreach (var r in allRenderers)
        {
            if (r is SpriteRenderer sr)
            {
                string resolvedSpritePath = string.Empty;
                if (sr.Texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path)) {
                    try { resolvedSpritePath = ResolveAssetPath(sr.Sprite.Path, sr.Sprite.Guid); if (File.Exists(resolvedSpritePath)) sr.Texture = LoadTexture(sr.Sprite); } catch { }
                }
                else if (!string.IsNullOrWhiteSpace(sr.Sprite.Path))
                {
                    try { resolvedSpritePath = ResolveAssetPath(sr.Sprite.Path, sr.Sprite.Guid); } catch { }
                }

                var tex = sr.Texture ?? DefaultSprites.Square;
                if (tex == null) continue;

                SpriteSlice spriteSlice = SpriteImportUtility.CreateDefaultSlice(tex.Width, tex.Height, new Vector2(0.5f, 0.5f));
                if (!string.IsNullOrWhiteSpace(resolvedSpritePath) && File.Exists(resolvedSpritePath))
                    spriteSlice = AssetPathUtility.ResolveSpriteSlice(resolvedSpritePath, sr.Sprite, tex.Width, tex.Height);

                Vector2 resolvedPivot = sr.UseSpritePivot ? spriteSlice.Pivot : sr.Pivot;
                var uvMin = new System.Numerics.Vector2(
                    spriteSlice.X / (float)Math.Max(1, tex.Width),
                    spriteSlice.Y / (float)Math.Max(1, tex.Height));
                var uvMax = new System.Numerics.Vector2(
                    (spriteSlice.X + spriteSlice.Width) / (float)Math.Max(1, tex.Width),
                    (spriteSlice.Y + spriteSlice.Height) / (float)Math.Max(1, tex.Height));

                if (isScreenFbo) _device.Gl.Viewport(vx, vy, (uint)fVw, (uint)fVh);
                else _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);

                var styleRuntime = ResolveStyle(sr.Style);
                var activeShader = styleRuntime?.Shader ?? _shader;

                activeShader.SetProjection(projection);
                activeShader.SetView(view);
                activeShader.SetModel(BuildModelMatrix(sr.Owner.Transform, sr, resolvedPivot));
                activeShader.SetTexture(tex);
                activeShader.SetUvRect(uvMin, uvMax);
                activeShader.SetColor(sr.Color);
                styleRuntime?.Apply(activeShader);
                
                _quadBuffer.Draw(activeShader.Program, actualTargetFbo).Unwrap();
            }
            else if (r is TilemapRenderer tr)
            {
                if (isScreenFbo) _device.Gl.Viewport(vx, vy, (uint)fVw, (uint)fVh);
                else _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
                
                tr.Render(this, camera, projection, view, actualTargetFbo);
            }
            else if (r is PolygonRenderer pr)
            {
                var vertices = pr.GetWorldVertices();
                if (vertices.Length < 3) continue;

                if (isScreenFbo) _device.Gl.Viewport(vx, vy, (uint)fVw, (uint)fVh);
                else _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);

                // 내부 채우기 (Fill) - 테두리는 그리지 않고 채우기만 수행
                if (pr.Fill)
                {
                    RenderPolygonFill(vertices, pr.Color, camera, actualTargetFbo);
                }
            }
        }

        if (isWorldFbo) { Verity.Core.Physics.PhysicsManager.DrawGizmos(world); _debugDraw.Render(camera, actualTargetFbo); }

        if (camera.FixedAspectRatio && _whitePixel != null && !isWorldFbo && !isScreenFbo) {
            _shader.SetProjection(Matrix4x4.Identity); _shader.SetView(Matrix4x4.Identity); _shader.SetTexture(_whitePixel); _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One); _shader.SetColor(camera.LetterboxColor);
            var pivot = Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0);
            if (windowAspect > shotAspect) {
                float vW = shotAspect / windowAspect; float bW = 1.0f - vW; float bC = (1.0f + vW) * 0.5f;
                _shader.SetModel(pivot * Matrix4x4.CreateScale(bW, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(-bC, 0, 0)); _quadBuffer.Draw(_shader.Program, actualTargetFbo).Unwrap();
                _shader.SetModel(pivot * Matrix4x4.CreateScale(bW, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(bC, 0, 0)); _quadBuffer.Draw(_shader.Program, actualTargetFbo).Unwrap();
            } else if (windowAspect < shotAspect) {
                float vH = windowAspect / shotAspect; float bH = 1.0f - vH; float bC = (1.0f + vH) * 0.5f;
                _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, bH, 1.0f) * Matrix4x4.CreateTranslation(0, bC, 0)); _quadBuffer.Draw(_shader.Program, actualTargetFbo).Unwrap();
                _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, bH, 1.0f) * Matrix4x4.CreateTranslation(0, -bC, 0)); _quadBuffer.Draw(_shader.Program, actualTargetFbo).Unwrap();
            }
        }

        if (usePostProcess)
        {
            ApplyPostProcess(camera, targetW, targetH, targetFbo);
        }

        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
    }

    private void ApplyPostProcess(Camera camera, int w, int h, FramebufferObject.Uploaded? targetFbo)
    {
        var settings = camera.PostProcess;
        if (_ppSceneTex == null || _ppTempTex1 == null || _ppTempFbo1 == null || _ppHistoryTex == null || _ppHistoryFbo == null ||
            _ppBloomFbo1 == null || _ppBloomFbo2 == null || _brightExtractShader == null || _blurShader == null ||
            _compositeShader == null || _copyShader == null)
        {
            return;
        }

        TextureObjectUploaded bloomTexture = _ppSceneTex;
        if (settings.Bloom.Enabled)
            bloomTexture = BuildBloomTexture(w, h, settings);

        _device.Gl.Viewport(0, 0, (uint)w, (uint)h);
        _device.Clear(Verity.Core.Color.Clear, _ppTempFbo1);
        _compositeShader.SetTexture("uScene", _ppSceneTex);
        _compositeShader.SetTexture("uBloomBlur", bloomTexture);
        _compositeShader.SetTexture("uHistory", _ppHistoryTex);
        _compositeShader.SetVec2("uResolution", new System.Numerics.Vector2(w, h));
        _compositeShader.SetFloat("uTime", Time.TotalTime);
        _compositeShader.SetFloat("uBloomEnabled", settings.Bloom.Enabled ? 1.0f : 0.0f);
        _compositeShader.SetFloat("uBloomIntensity", settings.Bloom.Enabled ? settings.Bloom.Intensity : 0.0f);
        _compositeShader.SetFloat("uVignetteEnabled", settings.Vignette.Enabled ? 1.0f : 0.0f);
        _compositeShader.SetFloat("uVignetteIntensity", settings.Vignette.Intensity);
        _compositeShader.SetFloat("uVignetteSmoothness", settings.Vignette.Smoothness);
        _compositeShader.SetFloat("uVignetteRoundness", settings.Vignette.Roundness);
        _compositeShader.SetColor("uVignetteColor", settings.Vignette.Color);
        _compositeShader.SetFloat("uColorAdjustEnabled", settings.ColorAdjustments.Enabled ? 1.0f : 0.0f);
        _compositeShader.SetFloat("uExposure", settings.ColorAdjustments.Exposure);
        _compositeShader.SetFloat("uContrast", settings.ColorAdjustments.Contrast);
        _compositeShader.SetFloat("uSaturation", settings.ColorAdjustments.Saturation);
        _compositeShader.SetColor("uTint", settings.ColorAdjustments.Tint);
        _compositeShader.SetFloat("uMotionBlurEnabled", settings.MotionBlur.Enabled ? 1.0f : 0.0f);
        _compositeShader.SetFloat("uMotionBlurIntensity", settings.MotionBlur.Intensity);
        _compositeShader.SetFloat("uHasHistory", _ppHistoryValid ? 1.0f : 0.0f);
        _compositeShader.SetFloat("uDistortionEnabled", settings.Distortion.Enabled ? 1.0f : 0.0f);
        _compositeShader.SetFloat("uDistortionIntensity", settings.Distortion.Intensity);
        _compositeShader.SetFloat("uDistortionSpeed", settings.Distortion.Speed);
        _compositeShader.SetFloat("uDistortionFrequency", settings.Distortion.Frequency);
        _compositeShader.SetVec2("uDistortionCenter", settings.Distortion.Center);
        _quadBuffer.Draw(_compositeShader.Program, _ppTempFbo1).Unwrap();

        TextureObjectUploaded finalTexture = _ppTempTex1;

        if (settings.Custom.Enabled)
        {
            var customResult = ApplyCustomPostProcess(settings, finalTexture, bloomTexture, w, h);
            if (customResult != null)
                finalTexture = customResult;
        }

        BlitTexture(finalTexture, targetFbo, w, h);
        BlitTexture(finalTexture, _ppHistoryFbo, w, h);
        _ppHistoryValid = true;
    }

    private TextureObjectUploaded BuildBloomTexture(int w, int h, PostProcessSettings settings)
    {
        if (_ppSceneTex == null || _ppBloomTex1 == null || _ppBloomTex2 == null || _ppBloomFbo1 == null || _ppBloomFbo2 == null ||
            _brightExtractShader == null || _blurShader == null)
        {
            return _ppSceneTex!;
        }

        int downsample = Math.Max(1, _ppBloomDownsample);
        int bw = Math.Max(1, w / downsample);
        int bh = Math.Max(1, h / downsample);

        _device.Gl.Viewport(0, 0, (uint)bw, (uint)bh);
        _device.Clear(Verity.Core.Color.Black, _ppBloomFbo1);
        _brightExtractShader.SetTexture("uTexture", _ppSceneTex);
        _brightExtractShader.SetFloat("uThreshold", settings.Bloom.Threshold);
        _quadBuffer.Draw(_brightExtractShader.Program, _ppBloomFbo1).Unwrap();

        TextureObjectUploaded source = _ppBloomTex1;
        int iterations = Math.Clamp(settings.Bloom.BlurIterations, 1, 8);
        float radius = Math.Max(0.25f, settings.Bloom.Scatter);

        for (int i = 0; i < iterations; i++)
        {
            _blurShader.SetTexture("uTexture", source);
            _blurShader.SetVec2("uDirection", System.Numerics.Vector2.UnitX);
            _blurShader.SetFloat("uRadius", radius);
            _quadBuffer.Draw(_blurShader.Program, _ppBloomFbo2).Unwrap();

            _blurShader.SetTexture("uTexture", _ppBloomTex2);
            _blurShader.SetVec2("uDirection", System.Numerics.Vector2.UnitY);
            _blurShader.SetFloat("uRadius", radius);
            _quadBuffer.Draw(_blurShader.Program, _ppBloomFbo1).Unwrap();

            source = _ppBloomTex1;
        }

        return _ppBloomTex1;
    }

    private TextureObjectUploaded? ApplyCustomPostProcess(PostProcessSettings settings, TextureObjectUploaded sourceTexture, TextureObjectUploaded bloomTexture, int w, int h)
    {
        if (_ppTempFbo2 == null || _ppTempTex2 == null || _ppHistoryTex == null)
            return null;

        var styleRuntime = ResolveStyle(settings.Custom.Style, PostProcessShaders.ScreenVertex, "postprocess");
        if (styleRuntime?.Shader == null)
            return null;

        var shader = styleRuntime.Shader;
        _device.Gl.Viewport(0, 0, (uint)w, (uint)h);
        _device.Clear(Verity.Core.Color.Clear, _ppTempFbo2);
        styleRuntime.Apply(shader);
        shader.SetTexture("uTexture", sourceTexture);
        shader.SetTexture("uScene", sourceTexture);
        shader.SetTexture("uSource", sourceTexture);
        shader.SetTexture("uBloomTexture", bloomTexture);
        shader.SetTexture("uPreviousTexture", _ppHistoryTex);
        shader.SetFloat("uTime", Time.TotalTime);
        shader.SetFloat("uDeltaTime", Time.DeltaTime);
        shader.SetVec2("uResolution", new System.Numerics.Vector2(w, h));
        shader.SetVec2("uTexelSize", new System.Numerics.Vector2(1f / Math.Max(1, w), 1f / Math.Max(1, h)));
        _quadBuffer.Draw(shader.Program, _ppTempFbo2).Unwrap();

        return _ppTempTex2;
    }

    private void BlitTexture(TextureObjectUploaded source, FramebufferObject.Uploaded? targetFbo, int w, int h)
    {
        if (_copyShader == null)
            return;

        _device.Gl.Viewport(0, 0, (uint)w, (uint)h);
        _copyShader.SetTexture("uTexture", source);
        _copyShader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _quadBuffer.Draw(_copyShader.Program, targetFbo).Unwrap();
    }

    private string GetCacheKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        string normalized = path.Replace("\\", "/");
        if (BaseAssetsPath != null && Path.IsPathRooted(normalized))
        {
            string baseWithSlash = BaseAssetsPath.Replace("\\", "/");
            if (!baseWithSlash.EndsWith("/")) baseWithSlash += "/";
            
            if (normalized.StartsWith(baseWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(baseWithSlash.Length);
            }
        }
        return normalized;
    }

    private StyleRuntime? ResolveStyle(StyleAsset asset, string? defaultVertexSource = null, string cacheScope = "style")
    {
        if (string.IsNullOrWhiteSpace(asset.Path)) return null;
        string key = $"{cacheScope}:{GetCacheKey(asset.Path)}";
        if (_styleCache.TryGetValue(key, out var cached)) return cached;
        
        string fullPath = ResolveAssetPath(asset.Path, asset.Guid);
        if (!File.Exists(fullPath)) return null;
        try {
            string json = File.ReadAllText(fullPath);
            var data = StyleData.FromJson(json);
            if (data == null) return null;
            var runtime = new StyleRuntime();
            if (!string.IsNullOrWhiteSpace(data.ShaderPath)) runtime.Shader = ResolveShader(new ShaderAsset(data.ShaderPath), defaultVertexSource, cacheScope);
            foreach (var (k, v) in data.Floats) runtime.Floats[k] = v;
            foreach (var (k, v) in data.Vector2s) runtime.Vector2s[k] = v;
            foreach (var (k, v) in data.Vector3s) runtime.Vector3s[k] = v;
            foreach (var (k, v) in data.Vector4s) runtime.Vector4s[k] = v;
            foreach (var (k, v) in data.Colors) runtime.Colors[k] = v;
            foreach (var (k, v) in data.Textures) {
                string texPath = ResolveAssetPath(v);
                if (File.Exists(texPath)) runtime.Textures[k] = _textureManager.Load(texPath);
            }
            _styleCache[key] = runtime;
            return runtime;
        } catch { return null; }
    }

    private Shader2D? ResolveShader(ShaderAsset asset, string? defaultVertexSource = null, string cacheScope = "shader")
    {
        if (string.IsNullOrWhiteSpace(asset.Path)) return null;
        string key = $"{cacheScope}:{GetCacheKey(asset.Path)}";
        if (_shaderCache.TryGetValue(key, out var cached)) return cached;
        
        string fullPath = ResolveAssetPath(asset.Path, asset.Guid);
        if (!File.Exists(fullPath)) return null;
        try {
            string content = File.ReadAllText(fullPath);
            string? vert = null, frag = null;
            
            if (content.Contains("// VERTEX")) {
                int vIdx = content.IndexOf("// VERTEX");
                int fIdx = content.IndexOf("// FRAGMENT");
                if (fIdx != -1) {
                    if (vIdx != -1 && vIdx < fIdx) {
                        vert = content.Substring(vIdx + 9, fIdx - vIdx - 9).Trim();
                        frag = content.Substring(fIdx + 11).Trim();
                    } else {
                        frag = content.Substring(fIdx + 11).Trim();
                    }
                } else if (vIdx != -1) {
                    vert = content.Substring(vIdx + 9).Trim();
                }
            } else if (content.Contains("// FRAGMENT")) {
                int fIdx = content.IndexOf("// FRAGMENT");
                frag = content.Substring(fIdx + 11).Trim();
            } else {
                frag = content.Trim();
            }

            var shader = Shader2D.Create(_device, vert ?? defaultVertexSource, frag);
            _shaderCache[key] = shader;
            return shader;
        } catch (Exception e) { 
            Verity.Core.Debug.LogError($"[RenderPipeline] Failed to compile shader {key}: {e.Message}");
            return null; 
        }
    }

    private string ResolveAssetPath(string p, string? guid = null) => AssetPathUtility.ResolvePath(BaseAssetsPath, p, guid);

    private List<Component> CollectAllSortedRenderers(World w) {
        var sprites = new List<SpriteRenderer>(); 
        var tilemaps = new List<TilemapRenderer>();
        foreach (var e in w.RootEntities) CollectRenderersRecursive(e, sprites, tilemaps);
        
        var polys = w.GetAllEntities().Where(e => e.Active).Select(e => e.GetComponent<PolygonRenderer>()).Where(pr => pr != null && pr.Enabled).Select(pr => pr!).ToList();
        
        var all = new List<Component>(); 
        all.AddRange(sprites); 
        all.AddRange(tilemaps);
        all.AddRange(polys);
        
        all.Sort((a, b) => {
            int la = a is SpriteRenderer srA ? srA.ResolvedLayerIndex : (a is TilemapRenderer trA ? trA.ResolvedLayerIndex : (a is PolygonRenderer prA ? prA.ResolvedLayerIndex : 0));
            int lb = b is SpriteRenderer srB ? srB.ResolvedLayerIndex : (b is TilemapRenderer trB ? trB.ResolvedLayerIndex : (b is PolygonRenderer prB ? prB.ResolvedLayerIndex : 0));
            int lc = la.CompareTo(lb); if (lc != 0) return lc;
            int oa = a is SpriteRenderer srA2 ? srA2.OrderInLayer : (a is TilemapRenderer trA2 ? trA2.OrderInLayer : (a is PolygonRenderer prA2 ? prA2.OrderInLayer : 0));
            int ob = b is SpriteRenderer srB2 ? srB2.OrderInLayer : (b is TilemapRenderer trB2 ? trB2.OrderInLayer : (b is PolygonRenderer prB2 ? prB2.OrderInLayer : 0));
            int oc = oa.CompareTo(ob); if (oc != 0) return oc;
            float va = GetSortAxisValue(a.Owner.Transform), vb = GetSortAxisValue(b.Owner.Transform);
            return SortAxisAscending ? va.CompareTo(vb) : vb.CompareTo(va);
        });
        return all;
    }
    private static void CollectRenderersRecursive(Entity e, List<SpriteRenderer> r, List<TilemapRenderer> t) { 
        if (!e.Active) return; 
        var sr = e.GetComponent<SpriteRenderer>(); if (sr != null && sr.Enabled) r.Add(sr); 
        var tr = e.GetComponent<TilemapRenderer>(); if (tr != null && tr.Enabled) t.Add(tr);
        foreach (var c in e.Transform.Children) CollectRenderersRecursive(c.Owner, r, t); 
    }
    private float GetSortAxisValue(Transform t) => CustomSortAxis switch { SortAxis.X => t.WorldPosition.X, SortAxis.Y => t.WorldPosition.Y, _ => 0f };
    private static Matrix4x4 BuildModelMatrix(Transform t, SpriteRenderer sr, Vector2 pivot) { 
        var wm = t.GetWorldMatrix(); 
        var localSprite = Matrix4x4.CreateTranslation(-pivot.X, -pivot.Y, 0) * Matrix4x4.CreateScale(sr.Size.X * (sr.FlipX ? -1f : 1f), sr.Size.Y * (sr.FlipY ? -1f : 1f), 1f);
        return localSprite * wm;
    }
    public void RenderGizmoLine(Vector2 s, Vector2 e, float t, Verity.Core.Color c, Camera cam, FramebufferObject.Uploaded? fbo = null) { if (_whitePixel == null) return; _shader.SetProjection(cam.GetProjectionMatrix()); _shader.SetView(cam.GetViewMatrix()); var dir = e - s; float len = dir.Length(); if (len < 0.0001f) return; float ang = MathF.Atan2(dir.Y, dir.X); _shader.SetModel(Matrix4x4.CreateTranslation(0, -0.5f, 0) * Matrix4x4.CreateScale(len, t, 1f) * Matrix4x4.CreateRotationZ(ang) * Matrix4x4.CreateTranslation(s.X, s.Y, 0)); _shader.SetTexture(_whitePixel); _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One); _shader.SetColor(c); _quadBuffer.Draw(_shader.Program, fbo).Unwrap(); }

    public void RenderGizmoRect(Vector2 center, Vector2 size, float rotationDeg, float thickness, Verity.Core.Color color, Camera cam, FramebufferObject.Uploaded? fbo = null)
    {
        float rad = rotationDeg * MathF.PI / 180f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);

        Vector2 hw = new Vector2(size.X * 0.5f * cos, size.X * 0.5f * sin);
        Vector2 hh = new Vector2(-size.Y * 0.5f * sin, size.Y * 0.5f * cos);

        Vector2 tl = center - hw + hh;
        Vector2 tr = center + hw + hh;
        Vector2 bl = center - hw - hh;
        Vector2 br = center + hw - hh;

        RenderGizmoLine(tl, tr, thickness, color, cam, fbo);
        RenderGizmoLine(tr, br, thickness, color, cam, fbo);
        RenderGizmoLine(br, bl, thickness, color, cam, fbo);
        RenderGizmoLine(bl, tl, thickness, color, cam, fbo);
    }

    public void RenderGizmoQuad(Vector2 center, Vector2 size, Verity.Core.Color color, Camera cam, FramebufferObject.Uploaded? fbo = null)
    {
        if (_whitePixel == null) return;
        _shader.SetProjection(cam.GetProjectionMatrix());
        _shader.SetView(cam.GetViewMatrix());
        _shader.SetModel(Matrix4x4.CreateScale(size.X, size.Y, 1f) * Matrix4x4.CreateTranslation(center.X, center.Y, 0));
        _shader.SetTexture(_whitePixel);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(color);
        _quadBuffer.Draw(_shader.Program, fbo).Unwrap();
    }

    public void DrawTile(TextureObjectUploaded tex, Matrix4x4 model, Verity.Core.Color color, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? fbo, System.Numerics.Vector2? uvMin = null, System.Numerics.Vector2? uvMax = null)
    {
        _shader.SetProjection(projection);
        _shader.SetView(view);
        _shader.SetModel(model);
        _shader.SetTexture(tex);
        _shader.SetUvRect(uvMin ?? System.Numerics.Vector2.Zero, uvMax ?? System.Numerics.Vector2.One);
        _shader.SetColor(color);
        _quadBuffer.Draw(_shader.Program, fbo).Unwrap();
    }

    public TextureObjectUploaded? LoadTexture(string path, string? guid = null)
    {
        try {
            string fp = ResolveAssetPath(path, guid);
            if (File.Exists(fp))
            {
                var settings = AssetPathUtility.TryGetSpriteImportSettings(fp);
                return _textureManager.Load(fp, settings?.Filter ?? SpriteTextureFilter.Point);
            }
        } catch { }
        return null;
    }

    public TextureObjectUploaded? LoadTexture(Sprite sprite) => LoadTexture(sprite.Path, sprite.Guid);

    private void RenderPolygonFill(Vector2[] vertices, int[] indices, Verity.Core.Color color, Camera cam, FramebufferObject.Uploaded? fbo)
    {
        if (_whitePixel == null || vertices.Length < 3 || indices.Length < 3) return;

        _shader.SetProjection(cam.GetProjectionMatrix());
        _shader.SetView(cam.GetViewMatrix());
        _shader.SetModel(Matrix4x4.Identity);
        _shader.SetTexture(_whitePixel);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(color);

        var data = Irodori.Buffer.IVertexData.Create<Vector2, Vector2>();
        for (int i = 0; i < vertices.Length; i++)
        {
            data.AddVertex(vertices[i], new Vector2(0.5f, 0.5f));
        }

        using var uploaded = _dynamicBuffer.Upload(data, indices).Unwrap();
        uploaded.Draw(_shader.Program, fbo).Unwrap();
    }

    private unsafe void RenderPolygonFill(Vector2[] vertices, Verity.Core.Color color, Camera cam, FramebufferObject.Uploaded? fbo)
    {
        if (vertices.Length < 3) return;
        int[] indices = new int[(vertices.Length - 2) * 3];
        for (int i = 0; i < vertices.Length - 2; i++) {
            indices[i * 3 + 0] = 0; indices[i * 3 + 1] = i + 1; indices[i * 3 + 2] = i + 2;
        }
        RenderPolygonFill(vertices, indices, color, cam, fbo);
    }

    public void ClearCache() { _shaderCache.Clear(); _styleCache.Clear(); }
    public void ClearStyleCache(string path)
    {
        string key = GetCacheKey(path);
        foreach (var cacheKey in _styleCache.Keys.Where(k => k.EndsWith(key, StringComparison.OrdinalIgnoreCase)).ToList())
            _styleCache.Remove(cacheKey);
    }
    public void ClearShaderCache(string path) { 
        string key = GetCacheKey(path);
        foreach (var cacheKey in _shaderCache.Keys.Where(k => k.EndsWith(key, StringComparison.OrdinalIgnoreCase)).ToList())
            _shaderCache.Remove(cacheKey);
        _styleCache.Clear(); // Shaders affect styles, so clear both
    }

    public void Dispose() 
    { 
        _worldFbo?.Dispose(); _worldColorTex?.Dispose(); 
        _screenFbo?.Dispose(); _screenColorTex?.Dispose(); 
        
        _ppSceneFbo?.Dispose(); _ppSceneTex?.Dispose();
        _ppTempFbo1?.Dispose(); _ppTempTex1?.Dispose();
        _ppTempFbo2?.Dispose(); _ppTempTex2?.Dispose();
        _ppHistoryFbo?.Dispose(); _ppHistoryTex?.Dispose();
        _ppBloomFbo1?.Dispose(); _ppBloomTex1?.Dispose();
        _ppBloomFbo2?.Dispose(); _ppBloomTex2?.Dispose();
        
        _copyShader?.Dispose();
        _brightExtractShader?.Dispose();
        _blurShader?.Dispose();
        _compositeShader?.Dispose();

        foreach(var s in _shaderCache.Values) s.Dispose(); 
        _quadBuffer.Dispose(); 
    }
}
