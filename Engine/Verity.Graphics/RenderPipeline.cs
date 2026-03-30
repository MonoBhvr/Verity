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
using Verity.Core.Physics;
using Verity.Core.World;
using Verity.Input;

namespace Verity.Graphics;

public enum SortAxis { Y, X, Z }

public class RenderPipeline : IDisposable
{
    private const int MaxShaderLights = 16;
    private const int MaxShaderOccluders = 24;
    private const int MaxShaderOccluderVertices = 384;
    private const int MaxVerticesPerOccluder = 48;

    private readonly struct ShadowOccluder
    {
        public ShadowOccluder(Entity owner, Vector2[] vertices, bool affectsOwner)
        {
            Owner = owner;
            Vertices = vertices;
            AffectsOwner = affectsOwner;
            GetBounds(vertices, out Vector2 min, out Vector2 max);
            Min = min;
            Max = max;
        }

        public Entity Owner { get; }
        public Vector2[] Vertices { get; }
        public bool AffectsOwner { get; }
        public Vector2 Min { get; }
        public Vector2 Max { get; }

        private static void GetBounds(Vector2[] vertices, out Vector2 min, out Vector2 max)
        {
            min = Vector2.Zero;
            max = Vector2.Zero;
            if (vertices.Length == 0)
                return;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (var vertex in vertices)
            {
                minX = MathF.Min(minX, vertex.X);
                minY = MathF.Min(minY, vertex.Y);
                maxX = MathF.Max(maxX, vertex.X);
                maxY = MathF.Max(maxY, vertex.Y);
            }

            min = new Vector2(minX, minY);
            max = new Vector2(maxX, maxY);
        }
    }

    private readonly record struct GridPoint(int X, int Y);

    private readonly record struct GridEdge(GridPoint Start, GridPoint End);

    private readonly GraphicsDevice _device;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;
    private readonly DebugDraw _debugDraw;
    private readonly GlyphAtlasTextRenderer _textRenderer;
    private readonly Irodori.Buffer.VertexBuffer.Uploaded _quadBuffer;
    private readonly Irodori.Buffer.VertexBuffer.Unuploaded _dynamicBuffer;
    private TextureObjectUploaded? _whitePixel;

    private readonly Dictionary<string, Shader2D> _shaderCache = new();
    private readonly Dictionary<string, StyleRuntime> _styleCache = new();
    private readonly Dictionary<string, Vector2[][]> _spriteShadowShapeCache = new(StringComparer.OrdinalIgnoreCase);

    private FramebufferObject.Uploaded? _worldFbo, _screenFbo;
    private TextureObjectUploaded? _worldColorTex, _screenColorTex;
    private int _worldFboWidth, _worldFboHeight, _screenFboWidth, _screenFboHeight;

    // Post-processing
    private Shader2D? _copyShader, _brightExtractShader, _blurShader, _bloomCombineShader, _vignetteShader, _colorAdjustShader, _motionBlurShader, _distortionShader, _pixelateShader, _chromaticAberrationShader;
    private FramebufferObject.Uploaded? _ppSceneFbo, _ppTempFbo1, _ppTempFbo2, _ppHistoryFbo, _ppBloomFbo1, _ppBloomFbo2;
    private TextureObjectUploaded? _ppSceneTex, _ppTempTex1, _ppTempTex2, _ppHistoryTex, _ppBloomTex1, _ppBloomTex2;
    private int _ppW, _ppH;
    private int _ppBloomDownsample = 2;
    private bool _ppHistoryValid;
    private Guid? _ppHistoryCameraId;
    private readonly List<Light2D> _frameLights = new();
    private readonly List<ShadowOccluder> _frameShadowOccluders = new();
    private bool _frameLightingEnabled;

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
        _textRenderer = new GlyphAtlasTextRenderer(_device, _textureManager, _shader, ResolveAssetPath);

        // Initialize post-processing shaders.
        _copyShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.CopyFragment);
        _brightExtractShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.BrightExtractFragment);
        _blurShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.BlurFragment);
        _bloomCombineShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.BloomCombineFragment);
        _vignetteShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.VignetteFragment);
        _colorAdjustShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.ColorAdjustFragment);
        _motionBlurShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.MotionBlurFragment);
        _distortionShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.DistortionFragment);
        _pixelateShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.PixelateFragment);
        _chromaticAberrationShader = Shader2D.Create(_device, PostProcessShaders.ScreenVertex, PostProcessShaders.ChromaticAberrationFragment);
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

        bool renderOutlineOnly = camera.RenderDetail == CameraRenderDetail.Outline;
        bool renderLighting = camera.RenderDetail is CameraRenderDetail.Lighting or CameraRenderDetail.PostProcess;
        bool usePostProcess = camera.RenderDetail == CameraRenderDetail.PostProcess &&
                              camera.PostProcess.Enabled &&
                              camera.PostProcess.HasAnyEnabledEffect();
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
            EnsurePostProcessFbos(targetW, targetH, Math.Max(1, camera.PostProcess.Bloom?.Downsample ?? 2));
            actualTargetFbo = _ppSceneFbo;
        }

        PrepareFrameLighting(world, renderLighting);
        Verity.Core.Color resolvedBackgroundColor = renderLighting
            ? ResolveCameraBackgroundColor(camera.BackgroundColor)
            : camera.BackgroundColor;

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
            _device.Clear(resolvedBackgroundColor, actualTargetFbo);
            _device.Gl.Disable(EnableCap.ScissorTest);
        } else {
            _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
            camera.SetViewportRect(0, 0, targetW, targetH);
            _device.Clear(resolvedBackgroundColor, actualTargetFbo);
        }

        var projection = camera.GetProjectionMatrix(isScreenFbo ? (fVw / (float)fVh) : windowAspect);
        var view = camera.GetViewMatrix();

        if (renderOutlineOnly)
        {
            RenderWorldOutline(world, camera, actualTargetFbo, isScreenFbo ? (vx, vy, fVw, fVh) : null, targetW, targetH);
        }
        else
        {
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

                if (activeShader == _shader)
                    ApplyLighting(activeShader, sr.Owner, sr.SortingLayerName);

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
                    RenderPolygonFill(vertices, pr.Color, camera, actualTargetFbo, pr.Owner, pr.SortingLayerName);
                }
            }
        }

        }

        if (isWorldFbo && camera.ShowGizmos)
        {
            Verity.Core.Physics.PhysicsManager.DrawGizmos(world);
            ConfigureUnlitShader(_shader);
            _debugDraw.Render(camera, actualTargetFbo);
        }

        if (usePostProcess)
        {
            ApplyPostProcess(camera, targetW, targetH, targetFbo);
        }
        else
        {
            DrawLetterboxBars(camera, targetW, targetH, windowAspect, shotAspect, actualTargetFbo, isWorldFbo, isScreenFbo);
        }

        if (usePostProcess)
        {
            DrawLetterboxBars(camera, targetW, targetH, windowAspect, shotAspect, targetFbo, isWorldFbo, isScreenFbo);
        }

        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
    }

    private Verity.Core.Color ResolveCameraBackgroundColor(Verity.Core.Color baseColor)
    {
        System.Numerics.Vector3 lighting = System.Numerics.Vector3.Zero;
        bool hasBackgroundLight = false;
        foreach (var light in _frameLights)
        {
            if (!light.AffectsCameraBackground || light.Type != Light2DType.World)
                continue;

            hasBackgroundLight = true;
            lighting += new System.Numerics.Vector3(light.Color.R, light.Color.G, light.Color.B) * MathF.Max(0.0f, light.Intensity);
        }

        if (!hasBackgroundLight)
            return baseColor;

        lighting = System.Numerics.Vector3.Clamp(lighting, System.Numerics.Vector3.Zero, System.Numerics.Vector3.One);
        return new Verity.Core.Color(baseColor.R * lighting.X, baseColor.G * lighting.Y, baseColor.B * lighting.Z, baseColor.A);
    }

    private void DrawLetterboxBars(Camera camera, int targetW, int targetH, float windowAspect, float shotAspect, FramebufferObject.Uploaded? targetFbo, bool isWorldFbo, bool isScreenFbo)
    {
        if (_whitePixel == null || !camera.FixedAspectRatio || isWorldFbo || isScreenFbo)
            return;

        _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
        ConfigureUnlitShader(_shader);
        _shader.SetProjection(Matrix4x4.Identity);
        _shader.SetView(Matrix4x4.Identity);
        _shader.SetTexture(_whitePixel);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(camera.LetterboxColor);

        var pivot = Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0);
        if (windowAspect > shotAspect)
        {
            float visibleWidth = shotAspect / windowAspect;
            float barWidth = 1.0f - visibleWidth;
            float barCenter = (1.0f + visibleWidth) * 0.5f;
            _shader.SetModel(pivot * Matrix4x4.CreateScale(barWidth, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(-barCenter, 0, 0));
            _quadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
            _shader.SetModel(pivot * Matrix4x4.CreateScale(barWidth, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(barCenter, 0, 0));
            _quadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
        }
        else if (windowAspect < shotAspect)
        {
            float visibleHeight = windowAspect / shotAspect;
            float barHeight = 1.0f - visibleHeight;
            float barCenter = (1.0f + visibleHeight) * 0.5f;
            _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, barHeight, 1.0f) * Matrix4x4.CreateTranslation(0, barCenter, 0));
            _quadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
            _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, barHeight, 1.0f) * Matrix4x4.CreateTranslation(0, -barCenter, 0));
            _quadBuffer.Draw(_shader.Program, targetFbo).Unwrap();
        }
    }

    private void ApplyPostProcess(Camera camera, int w, int h, FramebufferObject.Uploaded? targetFbo)
    {
        var settings = camera.PostProcess;
        var bloom = settings.Bloom;
        var vignette = settings.Vignette;
        var colorAdjustments = settings.ColorAdjustments;
        var motionBlur = settings.MotionBlur;
        var distortion = settings.Distortion;
        var pixelate = settings.Pixelate;
        var chromaticAberration = settings.ChromaticAberration;
        List<CustomPostProcessSettings> customs = settings.GetCustomEffects();
        if (_ppSceneTex == null || _ppTempTex1 == null || _ppTempTex2 == null || _ppTempFbo1 == null || _ppTempFbo2 == null ||
            _ppHistoryTex == null || _ppHistoryFbo == null || _ppBloomFbo1 == null || _ppBloomFbo2 == null ||
            _brightExtractShader == null || _blurShader == null || _copyShader == null || _bloomCombineShader == null ||
            _vignetteShader == null || _colorAdjustShader == null || _motionBlurShader == null || _distortionShader == null ||
            _pixelateShader == null || _chromaticAberrationShader == null)
        {
            return;
        }

        var passes = new List<(int Order, string Key)>();
        if (bloom?.Enabled == true) passes.Add((bloom.Order, "Bloom"));
        if (distortion?.Enabled == true) passes.Add((distortion.Order, "Distortion"));
        if (pixelate?.Enabled == true) passes.Add((pixelate.Order, "Pixelate"));
        if (chromaticAberration?.Enabled == true) passes.Add((chromaticAberration.Order, "ChromaticAberration"));
        if (motionBlur?.Enabled == true) passes.Add((motionBlur.Order, "MotionBlur"));
        if (colorAdjustments?.Enabled == true) passes.Add((colorAdjustments.Order, "ColorAdjustments"));
        if (vignette?.Enabled == true) passes.Add((vignette.Order, "Vignette"));
        for (int i = 0; i < customs.Count; i++)
        {
            if (customs[i].Enabled)
                passes.Add((customs[i].Order, $"Custom:{i}"));
        }

        passes.Sort((a, b) =>
        {
            int orderCompare = a.Order.CompareTo(b.Order);
            return orderCompare != 0 ? orderCompare : string.CompareOrdinal(a.Key, b.Key);
        });

        TextureObjectUploaded sourceTexture = _ppSceneTex;
        bool useFirstTarget = true;

        foreach (var pass in passes)
        {
            var destFbo = useFirstTarget ? _ppTempFbo1 : _ppTempFbo2;
            var destTex = useFirstTarget ? _ppTempTex1 : _ppTempTex2;
            if (destFbo == null || destTex == null)
                break;

            switch (pass.Key)
            {
                case "Bloom":
                    if (bloom != null)
                    {
                        var bloomTexture = BuildBloomTexture(sourceTexture, w, h, bloom);
                        ApplyScreenShader(destFbo, w, h, _bloomCombineShader, shader =>
                        {
                            shader.SetTexture("uScene", sourceTexture);
                            shader.SetTexture("uBloomBlur", bloomTexture);
                            shader.SetFloat("uBloomIntensity", bloom.Intensity);
                        });
                    }
                    break;
                case "Distortion":
                    if (distortion != null)
                    {
                        ApplyScreenShader(destFbo, w, h, _distortionShader, shader =>
                        {
                            shader.SetTexture("uTexture", sourceTexture);
                            shader.SetVec2("uResolution", new System.Numerics.Vector2(w, h));
                            shader.SetFloat("uDistortionIntensity", distortion.Intensity);
                            shader.SetVec2("uDistortionCenter", distortion.Center);
                            shader.SetFloat("uDistortionScale", distortion.Scale);
                        });
                    }
                    break;
                case "Pixelate":
                    if (pixelate != null)
                    {
                        ApplyScreenShader(destFbo, w, h, _pixelateShader, shader =>
                        {
                            shader.SetTexture("uTexture", sourceTexture);
                            shader.SetVec2("uPixelateResolution", new System.Numerics.Vector2(Math.Max(1, pixelate.Width), Math.Max(1, pixelate.Height)));
                        });
                    }
                    break;
                case "ChromaticAberration":
                    if (chromaticAberration != null)
                    {
                        ApplyScreenShader(destFbo, w, h, _chromaticAberrationShader, shader =>
                        {
                            shader.SetTexture("uTexture", sourceTexture);
                            shader.SetFloat("uChromaticAberrationIntensity", chromaticAberration.Intensity);
                            shader.SetVec2("uChromaticAberrationCenter", chromaticAberration.Center);
                        });
                    }
                    break;
                case "MotionBlur":
                    if (motionBlur != null)
                    {
                        ApplyScreenShader(destFbo, w, h, _motionBlurShader, shader =>
                        {
                            shader.SetTexture("uTexture", sourceTexture);
                            shader.SetTexture("uHistory", _ppHistoryTex);
                            shader.SetFloat("uMotionBlurIntensity", motionBlur.Intensity);
                            shader.SetFloat("uHasHistory", _ppHistoryValid ? 1.0f : 0.0f);
                        });
                    }
                    break;
                case "ColorAdjustments":
                    if (colorAdjustments != null)
                    {
                        ApplyScreenShader(destFbo, w, h, _colorAdjustShader, shader =>
                        {
                            shader.SetTexture("uTexture", sourceTexture);
                            shader.SetFloat("uExposure", colorAdjustments.Exposure);
                            shader.SetFloat("uContrast", colorAdjustments.Contrast);
                            shader.SetFloat("uSaturation", colorAdjustments.Saturation);
                            shader.SetColor("uTint", colorAdjustments.Tint);
                        });
                    }
                    break;
                case "Vignette":
                    if (vignette != null)
                    {
                        ApplyScreenShader(destFbo, w, h, _vignetteShader, shader =>
                        {
                            shader.SetTexture("uTexture", sourceTexture);
                            shader.SetFloat("uVignetteIntensity", vignette.Intensity);
                            shader.SetFloat("uVignetteSmoothness", vignette.Smoothness);
                            shader.SetFloat("uVignetteRoundness", vignette.Roundness);
                            shader.SetColor("uVignetteColor", vignette.Color);
                        });
                    }
                    break;
                default:
                    if (TryGetCustomPass(pass.Key, customs, out CustomPostProcessSettings? custom) && custom != null)
                    {
                        var customResult = ApplyCustomPostProcess(custom, sourceTexture, w, h, destFbo, destTex);
                        if (customResult == null)
                            continue;
                    }
                    else
                    {
                        continue;
                    }
                    break;
            }

            sourceTexture = destTex;
            useFirstTarget = !useFirstTarget;
        }

        BlitTexture(sourceTexture, targetFbo, w, h);
        BlitTexture(sourceTexture, _ppHistoryFbo, w, h);
        _ppHistoryValid = true;
    }

    private TextureObjectUploaded BuildBloomTexture(TextureObjectUploaded sourceTexture, int w, int h, BloomSettings settings)
    {
        if (_ppSceneTex == null || _ppBloomTex1 == null || _ppBloomTex2 == null || _ppBloomFbo1 == null || _ppBloomFbo2 == null ||
            _brightExtractShader == null || _blurShader == null)
        {
            return sourceTexture;
        }

        int downsample = Math.Max(1, _ppBloomDownsample);
        int bw = Math.Max(1, w / downsample);
        int bh = Math.Max(1, h / downsample);

        _device.Gl.Viewport(0, 0, (uint)bw, (uint)bh);
        _device.Clear(Verity.Core.Color.Black, _ppBloomFbo1);
        _brightExtractShader.SetTexture("uTexture", sourceTexture);
        _brightExtractShader.SetFloat("uThreshold", settings.Threshold);
        _quadBuffer.Draw(_brightExtractShader.Program, _ppBloomFbo1).Unwrap();

        TextureObjectUploaded source = _ppBloomTex1;
        int iterations = Math.Clamp(settings.BlurIterations, 1, 8);
        float radius = Math.Max(0.25f, settings.Scatter);

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

    private TextureObjectUploaded? ApplyCustomPostProcess(CustomPostProcessSettings settings, TextureObjectUploaded sourceTexture, int w, int h, FramebufferObject.Uploaded destFbo, TextureObjectUploaded destTex)
    {
        if (_ppHistoryTex == null)
            return null;

        var styleRuntime = ResolveStyle(settings.Style, PostProcessShaders.ScreenVertex, "postprocess");
        if (styleRuntime?.Shader == null)
            return null;

        var shader = styleRuntime.Shader;
        _device.Gl.Viewport(0, 0, (uint)w, (uint)h);
        _device.Clear(Verity.Core.Color.Clear, destFbo);
        styleRuntime.Apply(shader);
        shader.SetTexture("uTexture", sourceTexture);
        shader.SetTexture("uScene", sourceTexture);
        shader.SetTexture("uSource", sourceTexture);
        shader.SetTexture("uPreviousTexture", _ppHistoryTex);
        shader.SetFloat("uTime", Time.TotalTime);
        shader.SetFloat("uDeltaTime", Time.DeltaTime);
        shader.SetVec2("uResolution", new System.Numerics.Vector2(w, h));
        shader.SetVec2("uTexelSize", new System.Numerics.Vector2(1f / Math.Max(1, w), 1f / Math.Max(1, h)));
        _quadBuffer.Draw(shader.Program, destFbo).Unwrap();

        return destTex;
    }

    private static bool TryGetCustomPass(string key, List<CustomPostProcessSettings> customs, out CustomPostProcessSettings? custom)
    {
        const string prefix = "Custom:";
        if (key.StartsWith(prefix, StringComparison.Ordinal) &&
            int.TryParse(key[prefix.Length..], out int index) &&
            index >= 0 &&
            index < customs.Count)
        {
            custom = customs[index];
            return true;
        }

        custom = null;
        return false;
    }

    private void ApplyScreenShader(FramebufferObject.Uploaded destFbo, int w, int h, Shader2D? shader, Action<Shader2D> configure)
    {
        if (shader == null)
            return;

        _device.Gl.Viewport(0, 0, (uint)w, (uint)h);
        _device.Clear(Verity.Core.Color.Clear, destFbo);
        configure(shader);
        _quadBuffer.Draw(shader.Program, destFbo).Unwrap();
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

    private void PrepareFrameLighting(World world, bool enabled)
    {
        _frameLights.Clear();
        _frameShadowOccluders.Clear();
        _frameLightingEnabled = false;

        if (!enabled)
            return;

        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active)
                continue;

            CollectShadowOccluders(entity);

            var light = entity.GetComponent<Light2D>();
            if (light == null || !light.Enabled)
                continue;

            _frameLights.Add(light);
            _frameLightingEnabled = true;
        }
    }

    private void ConfigureUnlitShader(Shader2D shader)
    {
        shader.SetFloat("uLightingEnabled", 0.0f);
        shader.SetFloat("uLightCount", 0.0f);
        shader.SetFloat("uOccluderCount", 0.0f);
        shader.SetVec3("uAmbientLight", System.Numerics.Vector3.One);
    }

    private void ApplyLighting(Shader2D shader, Entity? owner, string? sortingLayerName = null)
    {
        if (!_frameLightingEnabled || owner == null)
        {
            ConfigureUnlitShader(shader);
            return;
        }

        ulong rendererMask = ResolveEntityPhysicsMask(owner);
        string resolvedSortingLayerName = ResolveRendererSortingLayer(owner, sortingLayerName);
        System.Numerics.Vector3 ambient = System.Numerics.Vector3.Zero;
        int lightCount = 0;

        ApplyShadowOccluders(shader, owner);

        foreach (var light in _frameLights)
        {
            if (!light.AffectsSortingLayer(resolvedSortingLayerName))
                continue;

            System.Numerics.Vector3 lightColor = new(light.Color.R, light.Color.G, light.Color.B);
            if (light.Type == Light2DType.World)
            {
                ambient += lightColor * MathF.Max(0.0f, light.Intensity);
                continue;
            }

            if (lightCount >= MaxShaderLights)
                continue;

            Vector2 direction = light.WorldDirection;
            Vector2 spriteCenter = light.WorldPosition;
            Vector2 spriteRight = Vector2.UnitX;
            Vector2 spriteUp = Vector2.UnitY;
            Vector2 spriteHalfSize = new(0.5f, 0.5f);
            if (light.Type == Light2DType.Sprite)
                light.TryGetSpriteBounds(out spriteCenter, out spriteRight, out spriteUp, out spriteHalfSize);

            float shadowStrength = ResolveShadowStrength(light, resolvedSortingLayerName, rendererMask);

            shader.SetVec4($"uLightMeta1[{lightCount}]", new Vector4(
                light.Type == Light2DType.Sprite ? spriteCenter.X : light.WorldPosition.X,
                light.Type == Light2DType.Sprite ? spriteCenter.Y : light.WorldPosition.Y,
                MathF.Max(0.0001f, light.Distance),
                MathF.Max(0.0f, light.Intensity)));
            shader.SetVec4($"uLightMeta2[{lightCount}]", new Vector4(
                direction.X,
                direction.Y,
                MathF.Max(0.0174533f, light.Spread * MathF.PI / 180.0f),
                Math.Clamp(light.Smoothness, 0.0f, 1.0f)));
            shader.SetVec4($"uLightMeta3[{lightCount}]", new Vector4(
                lightColor.X,
                lightColor.Y,
                lightColor.Z,
                (float)light.Type));
            shader.SetVec4($"uLightMeta4[{lightCount}]", new Vector4(
                spriteRight.X,
                spriteRight.Y,
                spriteUp.X,
                spriteUp.Y));
            shader.SetVec4($"uLightMeta5[{lightCount}]", new Vector4(
                spriteHalfSize.X,
                spriteHalfSize.Y,
                (float)light.Falloff,
                shadowStrength));
            lightCount++;
        }

        shader.SetFloat("uLightingEnabled", 1.0f);
        shader.SetVec3("uAmbientLight", ambient);
        shader.SetFloat("uLightCount", lightCount);
    }

    private void ApplyShadowOccluders(Shader2D shader, Entity owner)
    {
        Vector2 ownerPosition = owner.Transform.WorldPosition;
        int occluderCount = 0;
        int vertexCount = 0;
        foreach (var occluder in _frameShadowOccluders
            .Where(occluder => occluder.Owner != owner || occluder.AffectsOwner)
            .OrderBy(occluder => Vector2.DistanceSquared(ownerPosition, ClosestPointOnBounds(ownerPosition, occluder.Min, occluder.Max))))
        {
            if (occluderCount >= MaxShaderOccluders)
                break;

            int clampedVertexCount = Math.Min(occluder.Vertices.Length, MaxVerticesPerOccluder);
            if (clampedVertexCount < 3 || vertexCount + clampedVertexCount > MaxShaderOccluderVertices)
                continue;

            shader.SetVec4($"uOccluderMeta[{occluderCount}]", new Vector4(vertexCount, clampedVertexCount, 0.0f, 0.0f));
            for (int i = 0; i < clampedVertexCount; i++)
            {
                Vector2 vertex = occluder.Vertices[i];
                shader.SetVec4($"uOccluderVertices[{vertexCount + i}]", new Vector4(vertex.X, vertex.Y, 0.0f, 0.0f));
            }

            vertexCount += clampedVertexCount;
            occluderCount++;
        }

        shader.SetFloat("uOccluderCount", occluderCount);
        shader.SetFloat("uOccluderVertexCount", vertexCount);
    }

    private static float ResolveShadowStrength(Light2D light, string sortingLayerName, ulong physicsMask)
    {
        if (!light.CastShadows)
            return 0.0f;

        if (light.Type is not (Light2DType.Direction or Light2DType.Spot))
            return 0.0f;

        return light.ReceivesShadow(sortingLayerName, physicsMask)
            ? Math.Clamp(light.ShadowStrength, 0.0f, 1.0f)
            : 0.0f;
    }

    private static ulong ResolveEntityPhysicsMask(Entity entity)
    {
        ulong mask = 0;

        if (entity.GetComponent<Physical>() is Physical physical)
            mask |= physical.GroupMask;

        foreach (var shape in entity.GetComponents<PhysicalShape>())
            mask |= shape.GroupMask;

        return mask != 0 ? mask : FilterRegistry.GetGroupMask("Default");
    }

    private static ulong ResolveSortingLayerMask(string sortingLayerName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName;
        return FilterRegistry.GetMask("SortingLayer", resolvedName);
    }

    private static string ResolveRendererSortingLayer(Entity owner, string? explicitSortingLayerName)
    {
        if (!string.IsNullOrWhiteSpace(explicitSortingLayerName))
            return explicitSortingLayerName;

        if (owner.GetComponent<SpriteRenderer>() is SpriteRenderer spriteRenderer)
            return spriteRenderer.SortingLayerName;
        if (owner.GetComponent<TilemapRenderer>() is TilemapRenderer tilemapRenderer)
            return tilemapRenderer.SortingLayerName;
        if (owner.GetComponent<PolygonRenderer>() is PolygonRenderer polygonRenderer)
            return polygonRenderer.SortingLayerName;

        return "Default";
    }

    private static Vector2 ClosestPointOnBounds(Vector2 point, Vector2 min, Vector2 max)
        => new(Math.Clamp(point.X, min.X, max.X), Math.Clamp(point.Y, min.Y, max.Y));

    private void CollectShadowOccluders(Entity entity)
    {
        List<Vector2[]> colliderPolygons = CollectColliderShadowPolygons(entity);
        bool colliderAdded = false;
        bool hasRendererCaster = false;

        if (entity.GetComponent<SpriteRenderer>() is SpriteRenderer spriteRenderer && spriteRenderer.Enabled && spriteRenderer.CastShadows)
        {
            hasRendererCaster = true;
            AppendRendererShadowOccluders(entity, spriteRenderer.ShadowSourceMode, colliderPolygons, ref colliderAdded, BuildSpriteOccluders(spriteRenderer), spriteRenderer.ShadowSelfMode == ShadowSelfMode.AffectSelf);
        }

        if (entity.GetComponent<PolygonRenderer>() is PolygonRenderer polygonRenderer && polygonRenderer.Enabled && polygonRenderer.CastShadows)
        {
            hasRendererCaster = true;
            AppendRendererShadowOccluders(entity, polygonRenderer.ShadowSourceMode, colliderPolygons, ref colliderAdded, BuildPolygonOccluders(polygonRenderer), polygonRenderer.ShadowSelfMode == ShadowSelfMode.AffectSelf);
        }

        if (entity.GetComponent<TilemapRenderer>() is TilemapRenderer tilemapRenderer && tilemapRenderer.Enabled && tilemapRenderer.CastShadows)
        {
            hasRendererCaster = true;
            AppendRendererShadowOccluders(entity, tilemapRenderer.ShadowSourceMode, colliderPolygons, ref colliderAdded, BuildTilemapOccluders(tilemapRenderer), tilemapRenderer.ShadowSelfMode == ShadowSelfMode.AffectSelf);
        }

        if (!hasRendererCaster)
            AddShadowOccluders(entity, colliderPolygons, colliderPolygons.Any() && AnyColliderAffectsSelf(entity));
    }

    private void AppendRendererShadowOccluders(Entity entity, ShadowCasterSourceMode sourceMode, List<Vector2[]> colliderPolygons, ref bool colliderAdded, Vector2[][] rendererPolygons, bool rendererAffectsSelf)
    {
        bool hasRendererPolygons = rendererPolygons.Length > 0;
        bool hasColliderPolygons = colliderPolygons.Count > 0;
        bool colliderAffectsSelf = AnyColliderAffectsSelf(entity);

        switch (sourceMode)
        {
            case ShadowCasterSourceMode.Renderer:
                if (hasRendererPolygons)
                    AddShadowOccluders(entity, rendererPolygons, rendererAffectsSelf);
                break;

            case ShadowCasterSourceMode.Collider:
                if (!colliderAdded && hasColliderPolygons)
                {
                    AddShadowOccluders(entity, colliderPolygons, colliderAffectsSelf);
                    colliderAdded = true;
                }
                break;

            case ShadowCasterSourceMode.Both:
                if (hasRendererPolygons)
                    AddShadowOccluders(entity, rendererPolygons, rendererAffectsSelf);
                if (!colliderAdded && hasColliderPolygons)
                {
                    AddShadowOccluders(entity, colliderPolygons, colliderAffectsSelf);
                    colliderAdded = true;
                }
                break;

            case ShadowCasterSourceMode.PreferCollider:
                if (!colliderAdded && hasColliderPolygons)
                {
                    AddShadowOccluders(entity, colliderPolygons, colliderAffectsSelf);
                    colliderAdded = true;
                }
                else if (hasRendererPolygons)
                {
                    AddShadowOccluders(entity, rendererPolygons, rendererAffectsSelf);
                }
                break;

            default:
                if (hasRendererPolygons)
                {
                    AddShadowOccluders(entity, rendererPolygons, rendererAffectsSelf);
                }
                else if (!colliderAdded && hasColliderPolygons)
                {
                    AddShadowOccluders(entity, colliderPolygons, colliderAffectsSelf);
                    colliderAdded = true;
                }
                break;
        }
    }

    private void AddShadowOccluders(Entity entity, IEnumerable<Vector2[]> polygons, bool affectsOwner)
    {
        foreach (var polygon in polygons)
        {
            if (polygon.Length >= 3)
                _frameShadowOccluders.Add(new ShadowOccluder(entity, polygon, affectsOwner));
        }
    }

    private static bool AnyColliderAffectsSelf(Entity entity)
        => entity.GetComponents<PhysicalShape>().Any(shape => shape.Enabled && shape.CastShadows && shape.ShadowSelfMode == ShadowSelfMode.AffectSelf);

    private static List<Vector2[]> CollectColliderShadowPolygons(Entity entity)
    {
        List<Vector2[]> polygons = new();
        foreach (var shape in entity.GetComponents<PhysicalShape>())
        {
            if (!shape.Enabled || !shape.CastShadows)
                continue;

            AppendShapeShadowPolygons(shape, polygons);
        }

        return polygons;
    }

    private static void AppendShapeShadowPolygons(PhysicalShape shape, List<Vector2[]> polygons)
    {
        if (shape is TilemapShape tilemapShape)
        {
            foreach (var polygon in tilemapShape.GetWorldPolygons())
            {
                if (polygon.Length >= 3)
                    polygons.Add(polygon);
            }
            return;
        }

        if (shape is CircleShape circleShape)
        {
            if (TryBuildCircleOccluder(circleShape, out Vector2[] circleVertices))
                polygons.Add(circleVertices);
            return;
        }

        Vector2[] vertices = shape.GetVertices();
        if (vertices.Length >= 3)
            polygons.Add(vertices);
    }

    private Vector2[][] BuildSpriteOccluders(SpriteRenderer renderer)
    {
        var transform = renderer.Owner?.Transform;
        if (transform == null)
            return Array.Empty<Vector2[]>();

        if (string.IsNullOrWhiteSpace(renderer.Sprite.Path))
            return BuildSpriteQuadOccluders(renderer, renderer.UseSpritePivot ? new Vector2(0.5f, 0.5f) : renderer.Pivot);

        try
        {
            string resolvedPath = ResolveAssetPath(renderer.Sprite.Path, renderer.Sprite.Guid);
            if (!File.Exists(resolvedPath))
                return BuildSpriteQuadOccluders(renderer, renderer.UseSpritePivot ? new Vector2(0.5f, 0.5f) : renderer.Pivot);

            var raw = _textureManager.GetRawPixels(resolvedPath);
            SpriteSlice slice = AssetPathUtility.ResolveSpriteSlice(resolvedPath, renderer.Sprite, raw.Width, raw.Height);
            Vector2 resolvedPivot = renderer.UseSpritePivot ? slice.Pivot : renderer.Pivot;
            int alphaThresholdByte = Math.Clamp((int)MathF.Round(Math.Clamp(renderer.ShadowAlphaThreshold, 0.0f, 1.0f) * 255.0f), 0, 255);
            string cacheKey = $"{resolvedPath}|{renderer.Sprite.SpriteId}|{slice.X}|{slice.Y}|{slice.Width}|{slice.Height}|{alphaThresholdByte}";

            if (!_spriteShadowShapeCache.TryGetValue(cacheKey, out Vector2[][]? localPolygons))
            {
                localPolygons = BuildSpriteShadowShapes(raw.Pixels, raw.Width, raw.Height, slice, (byte)alphaThresholdByte);
                _spriteShadowShapeCache[cacheKey] = localPolygons;
            }

            return TransformShadowPolygons(localPolygons, BuildModelMatrix(transform, renderer, resolvedPivot));
        }
        catch
        {
            return BuildSpriteQuadOccluders(renderer, renderer.UseSpritePivot ? new Vector2(0.5f, 0.5f) : renderer.Pivot);
        }
    }

    private static Vector2[][] BuildSpriteQuadOccluders(SpriteRenderer renderer, Vector2 pivot)
    {
        var transform = renderer.Owner?.Transform;
        if (transform == null)
            return Array.Empty<Vector2[]>();

        Matrix4x4 model = BuildModelMatrix(transform, renderer, pivot);
        return
        [
            [
                TransformPoint(0.0f, 0.0f, model),
                TransformPoint(1.0f, 0.0f, model),
                TransformPoint(1.0f, 1.0f, model),
                TransformPoint(0.0f, 1.0f, model)
            ]
        ];
    }

    private Vector2[][] BuildTilemapOccluders(TilemapRenderer renderer)
    {
        if (!TryBuildTilemapOccluder(renderer, out Vector2[] vertices))
            return Array.Empty<Vector2[]>();

        return [vertices];
    }

    private static bool TryBuildTilemapOccluder(TilemapRenderer renderer, out Vector2[] vertices)
    {
        vertices = Array.Empty<Vector2>();

        var tilemap = renderer.Owner?.GetComponent<Tilemap>();
        var transform = renderer.Owner?.Transform;
        if (tilemap == null || transform == null)
            return false;

        if (!tilemap.TryGetTileBounds(out int tileMinX, out int tileMinY, out int tileMaxX, out int tileMaxY))
            return false;

        Vector2 localMin = new(tileMinX * tilemap.TileSize.X, tileMinY * tilemap.TileSize.Y);
        Vector2 localMax = new((tileMaxX + 1) * tilemap.TileSize.X, (tileMaxY + 1) * tilemap.TileSize.Y);
        Matrix4x4 worldMatrix = transform.GetWorldMatrix();
        vertices =
        [
            TransformPoint(localMin.X, localMin.Y, worldMatrix),
            TransformPoint(localMax.X, localMin.Y, worldMatrix),
            TransformPoint(localMax.X, localMax.Y, worldMatrix),
            TransformPoint(localMin.X, localMax.Y, worldMatrix)
        ];
        return true;
    }

    private static Vector2[][] BuildPolygonOccluders(PolygonRenderer renderer)
    {
        Vector2[] vertices = renderer.GetWorldVertices();
        return vertices.Length >= 3 ? [vertices] : Array.Empty<Vector2[]>();
    }

    private static bool TryBuildCircleOccluder(CircleShape shape, out Vector2[] vertices)
    {
        vertices = Array.Empty<Vector2>();
        var transform = shape.Owner?.Transform;
        if (transform == null)
            return false;

        Vector2 baseScale = shape.GetBaseScale();
        float radius = shape.Radius * Math.Max(MathF.Abs(baseScale.X), MathF.Abs(baseScale.Y));
        if (radius <= 0.0001f)
            return false;

        Vector2 center = shape.GetWorldCenter();
        const int segments = 8;
        vertices = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = (MathF.PI * 2.0f * i) / segments;
            vertices[i] = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
        }

        return true;
    }

    private static bool TryCalculateBounds(Vector2[] points, out Vector2 min, out Vector2 max)
    {
        min = Vector2.Zero;
        max = Vector2.Zero;

        if (points.Length == 0)
            return false;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var point in points)
        {
            minX = MathF.Min(minX, point.X);
            minY = MathF.Min(minY, point.Y);
            maxX = MathF.Max(maxX, point.X);
            maxY = MathF.Max(maxY, point.Y);
        }

        min = new Vector2(minX, minY);
        max = new Vector2(maxX, maxY);
        return true;
    }

    private static Vector2 TransformPoint(float x, float y, Matrix4x4 matrix)
    {
        var point = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(x, y, 0.0f), matrix);
        return new Vector2(point.X, point.Y);
    }

    private static Vector2[][] TransformShadowPolygons(Vector2[][] localPolygons, Matrix4x4 model)
    {
        List<Vector2[]> transformed = new(localPolygons.Length);
        foreach (var polygon in localPolygons)
        {
            if (polygon.Length < 3)
                continue;

            Vector2[] worldVertices = new Vector2[polygon.Length];
            for (int i = 0; i < polygon.Length; i++)
                worldVertices[i] = TransformPoint(polygon[i].X, polygon[i].Y, model);

            transformed.Add(worldVertices);
        }

        return transformed.ToArray();
    }

    private static Vector2[][] BuildSpriteShadowShapes(byte[] pixels, int textureWidth, int textureHeight, SpriteSlice slice, byte alphaThreshold)
    {
        int width = Math.Max(1, slice.Width);
        int height = Math.Max(1, slice.Height);
        bool[] mask = new bool[width * height];
        bool hasOpaquePixel = false;

        for (int y = 0; y < height; y++)
        {
            int sourceY = slice.Y + y;
            if (sourceY < 0 || sourceY >= textureHeight)
                continue;

            for (int x = 0; x < width; x++)
            {
                int sourceX = slice.X + x;
                if (sourceX < 0 || sourceX >= textureWidth)
                    continue;

                int pixelIndex = ((sourceY * textureWidth) + sourceX) * 4;
                if (pixelIndex + 3 >= pixels.Length)
                    continue;

                bool opaque = pixels[pixelIndex + 3] > alphaThreshold;
                mask[(y * width) + x] = opaque;
                hasOpaquePixel |= opaque;
            }
        }

        if (!hasOpaquePixel)
            return Array.Empty<Vector2[]>();

        List<GridEdge> edges = BuildSpriteBoundaryEdges(mask, width, height);
        List<List<GridPoint>> loops = TraceSpriteBoundaryLoops(edges);
        List<Vector2[]> polygons = new();

        foreach (var loop in loops)
        {
            List<GridPoint> simplified = SimplifyGridLoop(loop);
            if (simplified.Count < 3 || CalculateSignedArea(simplified) <= 0.0f)
                continue;

            Vector2[] polygon = new Vector2[simplified.Count];
            for (int i = 0; i < simplified.Count; i++)
                polygon[i] = new Vector2(simplified[i].X / (float)width, simplified[i].Y / (float)height);

            polygons.Add(polygon);
        }

        return polygons.ToArray();
    }

    private static List<GridEdge> BuildSpriteBoundaryEdges(bool[] mask, int width, int height)
    {
        List<GridEdge> edges = new();

        bool IsOpaque(int x, int y)
            => x >= 0 && x < width && y >= 0 && y < height && mask[(y * width) + x];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!IsOpaque(x, y))
                    continue;

                if (!IsOpaque(x, y - 1))
                    edges.Add(new GridEdge(new GridPoint(x, y), new GridPoint(x + 1, y)));
                if (!IsOpaque(x + 1, y))
                    edges.Add(new GridEdge(new GridPoint(x + 1, y), new GridPoint(x + 1, y + 1)));
                if (!IsOpaque(x, y + 1))
                    edges.Add(new GridEdge(new GridPoint(x + 1, y + 1), new GridPoint(x, y + 1)));
                if (!IsOpaque(x - 1, y))
                    edges.Add(new GridEdge(new GridPoint(x, y + 1), new GridPoint(x, y)));
            }
        }

        return edges;
    }

    private static List<List<GridPoint>> TraceSpriteBoundaryLoops(List<GridEdge> edges)
    {
        List<List<GridPoint>> loops = new();
        if (edges.Count == 0)
            return loops;

        Dictionary<GridPoint, List<int>> outgoing = new();
        for (int i = 0; i < edges.Count; i++)
        {
            if (!outgoing.TryGetValue(edges[i].Start, out List<int>? next))
            {
                next = new List<int>();
                outgoing[edges[i].Start] = next;
            }

            next.Add(i);
        }

        bool[] used = new bool[edges.Count];
        for (int i = 0; i < edges.Count; i++)
        {
            if (used[i])
                continue;

            List<GridPoint> loop = new() { edges[i].Start, edges[i].End };
            used[i] = true;

            GridPoint start = edges[i].Start;
            GridPoint previous = edges[i].Start;
            GridPoint current = edges[i].End;
            int guard = 0;

            while (current != start && guard++ < edges.Count)
            {
                if (!outgoing.TryGetValue(current, out List<int>? candidates))
                {
                    loop.Clear();
                    break;
                }

                int nextIndex = ChooseNextBoundaryEdge(edges, used, candidates, previous, current);
                if (nextIndex < 0)
                {
                    loop.Clear();
                    break;
                }

                used[nextIndex] = true;
                previous = current;
                current = edges[nextIndex].End;
                loop.Add(current);
            }

            if (loop.Count >= 4 && loop[^1] == loop[0])
            {
                loop.RemoveAt(loop.Count - 1);
                loops.Add(loop);
            }
        }

        return loops;
    }

    private static int ChooseNextBoundaryEdge(List<GridEdge> edges, bool[] used, List<int> candidates, GridPoint previous, GridPoint current)
    {
        int bestIndex = -1;
        int bestPriority = int.MaxValue;
        int previousDirection = GetCardinalDirection(previous, current);

        foreach (int candidate in candidates)
        {
            if (used[candidate])
                continue;

            int nextDirection = GetCardinalDirection(current, edges[candidate].End);
            int delta = (nextDirection - previousDirection + 4) % 4;
            int priority = delta switch
            {
                1 => 0,
                0 => 1,
                3 => 2,
                _ => 3
            };

            if (priority < bestPriority)
            {
                bestPriority = priority;
                bestIndex = candidate;
            }
        }

        return bestIndex;
    }

    private static int GetCardinalDirection(GridPoint start, GridPoint end)
    {
        int dx = end.X - start.X;
        int dy = end.Y - start.Y;

        if (dx > 0) return 0;
        if (dy > 0) return 1;
        if (dx < 0) return 2;
        return 3;
    }

    private static List<GridPoint> SimplifyGridLoop(List<GridPoint> loop)
    {
        List<GridPoint> simplified = new(loop);
        if (simplified.Count < 3)
            return simplified;

        bool removed;
        do
        {
            removed = false;
            for (int i = 0; i < simplified.Count; i++)
            {
                GridPoint previous = simplified[(i + simplified.Count - 1) % simplified.Count];
                GridPoint current = simplified[i];
                GridPoint next = simplified[(i + 1) % simplified.Count];
                if ((previous.X == current.X && current.X == next.X) || (previous.Y == current.Y && current.Y == next.Y))
                {
                    simplified.RemoveAt(i);
                    removed = true;
                    break;
                }
            }
        }
        while (removed && simplified.Count >= 3);

        return simplified;
    }

    private static float CalculateSignedArea(List<GridPoint> loop)
    {
        float area = 0.0f;
        for (int i = 0; i < loop.Count; i++)
        {
            GridPoint current = loop[i];
            GridPoint next = loop[(i + 1) % loop.Count];
            area += (current.X * next.Y) - (next.X * current.Y);
        }

        return area * 0.5f;
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
        var hierarchyOrder = w.GetAllEntities()
            .Select((entity, index) => (entity, index))
            .ToDictionary(pair => pair.entity, pair => pair.index);

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

            int ha = hierarchyOrder.GetValueOrDefault(a.Owner, int.MaxValue);
            int hb = hierarchyOrder.GetValueOrDefault(b.Owner, int.MaxValue);
            int hc = ha.CompareTo(hb); if (hc != 0) return hc;

            float va = GetSortAxisValue(a.Owner.Transform), vb = GetSortAxisValue(b.Owner.Transform);
            int vc = SortAxisAscending ? va.CompareTo(vb) : vb.CompareTo(va);
            return vc != 0 ? vc : a.Owner.Id.CompareTo(b.Owner.Id);
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

    private void RenderWorldOutline(World world, Camera camera, FramebufferObject.Uploaded? targetFbo, (int x, int y, int w, int h)? viewport, int targetW, int targetH)
    {
        if (viewport.HasValue)
        {
            var v = viewport.Value;
            _device.Gl.Viewport(v.x, v.y, (uint)v.w, (uint)v.h);
        }
        else
        {
            _device.Gl.Viewport(0, 0, (uint)targetW, (uint)targetH);
        }

        float thickness = MathF.Max(0.01f, camera.VisibleHalfHeight / Math.Max(1, camera.ViewportHeight) * 2.0f);
        var outlineColor = new Verity.Core.Color(0.9f, 0.95f, 1.0f, 0.92f);
        foreach (var renderer in CollectAllSortedRenderers(world))
        {
            if (renderer is SpriteRenderer sr)
            {
                var (center, size, rotation) = GetSpriteOutlineRect(sr);
                RenderGizmoRect(center, size, rotation, thickness, outlineColor, camera, targetFbo);
            }
            else if (renderer is TilemapRenderer tr)
            {
                RenderTilemapOutline(tr, thickness, outlineColor, camera, targetFbo);
            }
            else if (renderer is PolygonRenderer pr)
            {
                RenderPolygonOutline(pr, thickness, outlineColor, camera, targetFbo);
            }
        }
    }

    private (Vector2 center, Vector2 size, float rotation) GetSpriteOutlineRect(SpriteRenderer sr)
    {
        Transform transform = sr.Owner.Transform;
        Vector2 worldPosition = transform.WorldPosition;
        Vector2 worldScale = transform.WorldScale;
        float worldRotation = transform.WorldRotation;
        Vector2 effectiveSize = new(MathF.Abs(worldScale.X * sr.Size.X), MathF.Abs(worldScale.Y * sr.Size.Y));
        if (effectiveSize.X < 0.0001f) effectiveSize.X = 0.0001f;
        if (effectiveSize.Y < 0.0001f) effectiveSize.Y = 0.0001f;

        Vector2 pivot = ResolveSpritePivot(sr);
        Vector2 offset = (Vector2.One * 0.5f - pivot) * (worldScale * sr.Size);
        float radians = worldRotation * MathF.PI / 180f;
        Vector2 rotatedOffset = new(
            offset.X * MathF.Cos(radians) - offset.Y * MathF.Sin(radians),
            offset.X * MathF.Sin(radians) + offset.Y * MathF.Cos(radians));

        return (worldPosition + rotatedOffset, effectiveSize, worldRotation);
    }

    private Vector2 ResolveSpritePivot(SpriteRenderer sr)
    {
        if (!sr.UseSpritePivot)
            return sr.Pivot;

        TextureObjectUploaded? texture = sr.Texture;
        if (texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
            texture = LoadTexture(sr.Sprite);

        if (texture == null || string.IsNullOrWhiteSpace(sr.Sprite.Path))
            return sr.Pivot;

        try
        {
            string resolvedSpritePath = ResolveAssetPath(sr.Sprite.Path, sr.Sprite.Guid);
            if (File.Exists(resolvedSpritePath))
            {
                SpriteSlice spriteSlice = AssetPathUtility.ResolveSpriteSlice(resolvedSpritePath, sr.Sprite, texture.Width, texture.Height);
                return spriteSlice.Pivot;
            }
        }
        catch
        {
        }

        return sr.Pivot;
    }

    private void RenderTilemapOutline(TilemapRenderer tr, float thickness, Verity.Core.Color color, Camera camera, FramebufferObject.Uploaded? fbo)
    {
        var tilemap = tr.Owner.GetComponent<Tilemap>();
        if (tilemap == null || !tilemap.TryGetTileBounds(out int minX, out int minY, out int maxX, out int maxY))
            return;

        Vector2 bl = tilemap.CellToWorld(minX, minY);
        Vector2 br = tilemap.CellToWorld(maxX + 1, minY);
        Vector2 trp = tilemap.CellToWorld(maxX + 1, maxY + 1);
        Vector2 tl = tilemap.CellToWorld(minX, maxY + 1);

        RenderGizmoLine(bl, br, thickness, color, camera, fbo);
        RenderGizmoLine(br, trp, thickness, color, camera, fbo);
        RenderGizmoLine(trp, tl, thickness, color, camera, fbo);
        RenderGizmoLine(tl, bl, thickness, color, camera, fbo);
    }

    private void RenderPolygonOutline(PolygonRenderer renderer, float thickness, Verity.Core.Color color, Camera camera, FramebufferObject.Uploaded? fbo)
    {
        Vector2[] vertices = renderer.GetWorldVertices();
        if (vertices.Length < 2)
            return;

        int edgeCount = renderer.IsClosed ? vertices.Length : vertices.Length - 1;
        for (int i = 0; i < edgeCount; i++)
        {
            int next = (i + 1) % vertices.Length;
            RenderGizmoLine(vertices[i], vertices[next], thickness, color, camera, fbo);
        }
    }

    public void RenderGizmoLine(Vector2 s, Vector2 e, float t, Verity.Core.Color c, Camera cam, FramebufferObject.Uploaded? fbo = null) { if (_whitePixel == null) return; ConfigureUnlitShader(_shader); _shader.SetProjection(cam.GetProjectionMatrix()); _shader.SetView(cam.GetViewMatrix()); var dir = e - s; float len = dir.Length(); if (len < 0.0001f) return; float ang = MathF.Atan2(dir.Y, dir.X); _shader.SetModel(Matrix4x4.CreateTranslation(0, -0.5f, 0) * Matrix4x4.CreateScale(len, t, 1f) * Matrix4x4.CreateRotationZ(ang) * Matrix4x4.CreateTranslation(s.X, s.Y, 0)); _shader.SetTexture(_whitePixel); _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One); _shader.SetColor(c); _quadBuffer.Draw(_shader.Program, fbo).Unwrap(); }

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
        ConfigureUnlitShader(_shader);
        _shader.SetProjection(cam.GetProjectionMatrix());
        _shader.SetView(cam.GetViewMatrix());
        _shader.SetModel(Matrix4x4.CreateScale(size.X, size.Y, 1f) * Matrix4x4.CreateTranslation(center.X, center.Y, 0));
        _shader.SetTexture(_whitePixel);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(color);
        _quadBuffer.Draw(_shader.Program, fbo).Unwrap();
    }

    public void DrawTile(TextureObjectUploaded tex, Matrix4x4 model, Verity.Core.Color color, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? fbo, Entity? owner = null, string? sortingLayerName = null, System.Numerics.Vector2? uvMin = null, System.Numerics.Vector2? uvMax = null)
    {
        ApplyLighting(_shader, owner, sortingLayerName);
        _shader.SetProjection(projection);
        _shader.SetView(view);
        _shader.SetModel(model);
        _shader.SetTexture(tex);
        _shader.SetUvRect(uvMin ?? System.Numerics.Vector2.Zero, uvMax ?? System.Numerics.Vector2.One);
        _shader.SetColor(color);
        _quadBuffer.Draw(_shader.Program, fbo).Unwrap();
    }

    public void DrawText(TextRenderOptions options, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? fbo = null)
    {
        ConfigureUnlitShader(_shader);
        _textRenderer.DrawText(options, projection, view, fbo);
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

    private void RenderPolygonFill(Vector2[] vertices, int[] indices, Verity.Core.Color color, Camera cam, FramebufferObject.Uploaded? fbo, Entity? owner, string? sortingLayerName = null)
    {
        if (_whitePixel == null || vertices.Length < 3 || indices.Length < 3) return;

        ApplyLighting(_shader, owner, sortingLayerName);
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

    private unsafe void RenderPolygonFill(Vector2[] vertices, Verity.Core.Color color, Camera cam, FramebufferObject.Uploaded? fbo, Entity? owner, string? sortingLayerName = null)
    {
        if (vertices.Length < 3) return;
        int[] indices = new int[(vertices.Length - 2) * 3];
        for (int i = 0; i < vertices.Length - 2; i++) {
            indices[i * 3 + 0] = 0; indices[i * 3 + 1] = i + 1; indices[i * 3 + 2] = i + 2;
        }
        RenderPolygonFill(vertices, indices, color, cam, fbo, owner, sortingLayerName);
    }

    public void ClearCache() { _shaderCache.Clear(); _styleCache.Clear(); _spriteShadowShapeCache.Clear(); }
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
        _bloomCombineShader?.Dispose();
        _vignetteShader?.Dispose();
        _colorAdjustShader?.Dispose();
        _motionBlurShader?.Dispose();
        _distortionShader?.Dispose();
        _pixelateShader?.Dispose();
        _chromaticAberrationShader?.Dispose();
        _textRenderer.Dispose();

        foreach(var s in _shaderCache.Values) s.Dispose(); 
        _quadBuffer.Dispose(); 
    }
}
