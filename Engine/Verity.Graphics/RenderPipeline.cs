using System.Drawing;
using System.Numerics;
using Irodori.Backend.OpenGL;
using Irodori.Framebuffer;
using Irodori.Texture;
using Silk.NET.OpenGL;
using System.Text.Json;
using System.Text.Json.Serialization;
using Verity.Core;
using Verity.Core.Collections;
using Verity.Core.Engine;
using Verity.Core.ECS;
using Verity.Core.Physics;
using Verity.Core.World;
using Verity.Filter;

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
    private readonly record struct RendererSortItem(Component Renderer, int HierarchyOrder, int LayerIndex, int OrderInLayer, float SortAxisValue);
    private readonly record struct OccluderCandidate(ShadowOccluder Occluder, float DistanceSquared);
    private readonly record struct ResolvedAssetInfo(string Path, bool Exists);
    private sealed class CameraOutputTargetHandle : IDisposable
    {
        public CameraOutputTargetHandle(RenderTexture texture, RenderTarget target, int width, int height, RenderTextureFilter filter)
        {
            Texture = texture;
            Target = target;
            Width = width;
            Height = height;
            Filter = filter;
        }

        public RenderTexture Texture { get; }
        public RenderTarget Target { get; }
        public int Width { get; }
        public int Height { get; }
        public RenderTextureFilter Filter { get; }

        public void Dispose()
        {
            Target.Dispose();
            Texture.Dispose();
        }
    }

    private readonly IRenderDevice _device;
    private readonly Shader2D _shader;
    private readonly TextureManager _textureManager;
    private readonly DebugDraw _debugDraw;
    private readonly GlyphAtlasTextRenderer? _textRenderer;
    private readonly RenderMesh _quadBuffer;
    private readonly RenderMeshBuilder _dynamicBuffer;
    private RenderTexture? _whitePixel;

    private readonly ConcurrentLruCache<string, Shader2D> _shaderCache = new(64);
    private readonly ConcurrentLruCache<string, StyleRuntime> _styleCache = new(128);
    private readonly ConcurrentLruCache<string, Vector2[][]> _spriteShadowShapeCache = new(256);
    private readonly ConcurrentLruCache<string, ResolvedAssetInfo> _resolvedAssetCache = new(512);
    private readonly ConcurrentLruCache<string, SpriteSlice> _spriteSliceCache = new(256);
    private readonly List<Component> _sortedRenderers = new();
    private readonly List<RendererSortItem> _rendererSortItems = new();
    private readonly List<OccluderCandidate> _occluderCandidates = new();
    private readonly List<Vector2[]> _shadowPolygonScratch = new();
    private readonly List<(int Order, string Key)> _postProcessPasses = new();
    private readonly List<float> _browserQuadVertices = new();
    private readonly List<int> _browserQuadIndices = new();

    private RenderTarget? _worldFbo, _screenFbo;
    private RenderTexture? _worldColorTex, _screenColorTex;
    private int _worldFboWidth, _worldFboHeight, _screenFboWidth, _screenFboHeight;
    private readonly Dictionary<string, CameraOutputTargetHandle> _cameraOutputTargets = new(StringComparer.OrdinalIgnoreCase);

    // Post-processing
    private Shader2D? _copyShader, _brightExtractShader, _blurShader, _bloomCombineShader, _vignetteShader, _colorAdjustShader, _motionBlurShader, _distortionShader, _pixelateShader, _chromaticAberrationShader;
    private RenderTarget? _ppSceneFbo, _ppTempFbo1, _ppTempFbo2, _ppHistoryFbo, _ppBloomFbo1, _ppBloomFbo2;
    private RenderTexture? _ppSceneTex, _ppTempTex1, _ppTempTex2, _ppHistoryTex, _ppBloomTex1, _ppBloomTex2;
    private int _ppW, _ppH;
    private int _ppBloomDownsample = 2;
    private bool _ppHistoryValid;
    private Guid? _ppHistoryCameraId;
    private readonly List<Light2D> _frameLights = new();
    private readonly List<ShadowOccluder> _frameShadowOccluders = new();
    private bool _frameLightingEnabled;
    private RenderTexture? _browserBatchTexture;
    private RenderTarget? _browserBatchTarget;
    private Matrix4x4 _browserBatchProjection;
    private Matrix4x4 _browserBatchView;
    private Verity.Core.Color _browserBatchColor;
    private bool _browserBatchActive;

    public SortAxis CustomSortAxis { get; set; } = SortAxis.Y;
    public bool SortAxisAscending { get; set; } = true;
    public static string? BaseAssetsPath { get; set; }

    public RenderTarget? WorldFbo => _worldFbo;
    public RenderTexture? WorldColorTexture => _worldColorTex;
    public RenderTarget? ScreenFbo => _screenFbo;
    public RenderTexture? ScreenColorTexture => _screenColorTex;

    public RenderPipeline(IRenderDevice device, Shader2D shader, TextureManager textureManager)
    {
        _device = device; _shader = shader; _textureManager = textureManager;
        _quadBuffer = CreateQuadBuffer(device);
        
        _dynamicBuffer = device.CreateMeshBuilder(RenderMeshLayout.PositionTexture2D);

        _debugDraw = new DebugDraw(shader, _quadBuffer);
        if (OperatingSystem.IsWindows())
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

    private static RenderMesh CreateQuadBuffer(IRenderDevice device)
    {
        var data = RenderMeshData.CreatePositionTexture2D();
        data.AddVertex(new Vector2(0, 0), new Vector2(0, 0)); // top-left
        data.AddVertex(new Vector2(1, 0), new Vector2(1, 0)); // top-right
        data.AddVertex(new Vector2(0, 1), new Vector2(0, 1)); // bottom-left
        data.AddVertex(new Vector2(1, 1), new Vector2(1, 1)); // bottom-right

        var indices = new int[] { 0, 2, 1, 1, 2, 3 };

        var buffer = device.CreateMeshBuilder(RenderMeshLayout.PositionTexture2D);
        return buffer.Upload(data, indices);
    }

    public void SetWhitePixel(RenderTexture whitePixel) { _whitePixel = whitePixel; _debugDraw.SetWhitePixel(whitePixel); }

    public unsafe void EnsureFbo(int w, int h)
    {
        if (_worldFbo != null && _worldFboWidth == w && _worldFboHeight == h) return;
        _worldFbo?.Dispose(); _worldColorTex?.Dispose();
        _worldColorTex = _device.CreateTexture().WithSize(w, h).WithRgba8().WithFilter(RenderTextureFilter.Nearest).UploadEmpty();
        _worldFbo = _device.CreateFramebuffer().WithColorAttachment(_worldColorTex).Upload();
        _worldFboWidth = w; _worldFboHeight = h;
    }

    public unsafe void EnsureScreenFbo(int w, int h)
    {
        if (_screenFbo != null && _screenFboWidth == w && _screenFboHeight == h) return;
        _screenFbo?.Dispose(); _screenColorTex?.Dispose();
        _screenColorTex = _device.CreateTexture().WithSize(w, h).WithRgba8().WithFilter(RenderTextureFilter.Nearest).UploadEmpty();
        _screenFbo = _device.CreateFramebuffer().WithColorAttachment(_screenColorTex).Upload();
        _screenFboWidth = w; _screenFboHeight = h;
    }

    public void RenderCameraOutputs(World world, bool includeWindowOutputs = false)
    {
        foreach (var output in CameraSelection.EnumerateActiveOutputs(world)
                     .Where(output => output.Target == CameraOutputTarget.RenderTexture ||
                                      (includeWindowOutputs && output.Target == CameraOutputTarget.Window))
                     .OrderBy(static output => output.Order))
        {
            var camera = output.Camera;
            if (camera == null || !camera.Enabled)
                continue;

            string outputName = output.ResolveOutputName();
            if (string.IsNullOrWhiteSpace(outputName))
                continue;

            var target = EnsureCameraOutputTarget(outputName, output);
            RenderWorld(world, camera, target.Target);
        }
    }

    public bool TryGetCameraOutputTexture(string outputName, out RenderTexture texture)
    {
        texture = null!;
        if (string.IsNullOrWhiteSpace(outputName))
            return false;

        string key = AssetPathUtility.Normalize(outputName.Trim());
        if (!_cameraOutputTargets.TryGetValue(key, out var target) &&
            !_cameraOutputTargets.TryGetValue(Path.GetFileNameWithoutExtension(key), out target))
        {
            return false;
        }

        texture = target.Texture;
        return true;
    }

    public bool TryGetTextureAsset(TextureAsset asset, out RenderTexture texture)
    {
        texture = null!;
        if (asset == null || string.IsNullOrWhiteSpace(asset.Path))
            return false;

        string path = AssetPathUtility.Normalize(asset.Path);
        if (Path.GetExtension(path).Equals(".rendertexture", StringComparison.OrdinalIgnoreCase))
            return TryGetCameraOutputTexture(path, out texture);

        var loaded = LoadTexture(asset.Path, asset.Guid);
        if (loaded == null)
            return false;

        texture = loaded;
        return true;
    }

    private CameraOutputTargetHandle EnsureCameraOutputTarget(string outputName, CameraOutput output)
    {
        var settings = output.GetRenderTextureSettings();
        int width = Math.Max(1, settings.Width);
        int height = Math.Max(1, settings.Height);
        var filter = output.SamplingMode == CameraOutputSamplingMode.Linear
            ? RenderTextureFilter.Linear
            : RenderTextureFilter.Nearest;

        if (_cameraOutputTargets.TryGetValue(outputName, out var existing) &&
            existing.Width == width &&
            existing.Height == height &&
            existing.Filter == filter)
        {
            return existing;
        }

        if (existing != null)
            existing.Dispose();

        var texture = _device.CreateTexture()
            .WithSize(width, height)
            .WithRgba8()
            .WithFilter(filter)
            .UploadEmpty();
        var target = _device.CreateFramebuffer().WithColorAttachment(texture).Upload();
        var handle = new CameraOutputTargetHandle(texture, target, width, height, filter);
        _cameraOutputTargets[outputName] = handle;
        return handle;
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

        _ppSceneTex = _device.CreateTexture().WithSize(w, h).WithRgba8().WithFilter(RenderTextureFilter.Linear).UploadEmpty();
        _ppSceneFbo = _device.CreateFramebuffer().WithColorAttachment(_ppSceneTex).Upload();

        _ppTempTex1 = _device.CreateTexture().WithSize(w, h).WithRgba8().WithFilter(RenderTextureFilter.Linear).UploadEmpty();
        _ppTempFbo1 = _device.CreateFramebuffer().WithColorAttachment(_ppTempTex1).Upload();

        _ppTempTex2 = _device.CreateTexture().WithSize(w, h).WithRgba8().WithFilter(RenderTextureFilter.Linear).UploadEmpty();
        _ppTempFbo2 = _device.CreateFramebuffer().WithColorAttachment(_ppTempTex2).Upload();

        _ppHistoryTex = _device.CreateTexture().WithSize(w, h).WithRgba8().WithFilter(RenderTextureFilter.Linear).UploadEmpty();
        _ppHistoryFbo = _device.CreateFramebuffer().WithColorAttachment(_ppHistoryTex).Upload();

        int bw = Math.Max(1, w / bloomDownsample);
        int bh = Math.Max(1, h / bloomDownsample);
        _ppBloomTex1 = _device.CreateTexture().WithSize(bw, bh).WithRgba8().WithFilter(RenderTextureFilter.Linear).UploadEmpty();
        _ppBloomFbo1 = _device.CreateFramebuffer().WithColorAttachment(_ppBloomTex1).Upload();

        _ppBloomTex2 = _device.CreateTexture().WithSize(bw, bh).WithRgba8().WithFilter(RenderTextureFilter.Linear).UploadEmpty();
        _ppBloomFbo2 = _device.CreateFramebuffer().WithColorAttachment(_ppBloomTex2).Upload();

        _ppW = w; _ppH = h;
        _ppBloomDownsample = bloomDownsample;
        _ppHistoryValid = false;
    }

    public void RenderWorld(World world, Camera camera, RenderTarget? targetFbo = null, bool clearTarget = true)
    {
        bool browserFastPath = OperatingSystem.IsBrowser();
        bool isWorldFbo = (_worldFbo != null && targetFbo == _worldFbo);
        bool isScreenFbo = (_screenFbo != null && targetFbo == _screenFbo);

        bool isCameraOutputFbo = TryGetCameraOutputTargetSize(targetFbo, out int cameraOutputW, out int cameraOutputH);
        int targetW = isWorldFbo ? _worldFboWidth : (isScreenFbo ? _screenFboWidth : (isCameraOutputFbo ? cameraOutputW : (int)_device.Width));
        int targetH = isWorldFbo ? _worldFboHeight : (isScreenFbo ? _screenFboHeight : (isCameraOutputFbo ? cameraOutputH : (int)_device.Height));
        if (targetW <= 0 || targetH <= 0) return;

        bool renderOutlineOnly = camera.RenderDetail == CameraRenderDetail.Outline;
        bool renderLighting = !browserFastPath && camera.RenderDetail is CameraRenderDetail.Lighting or CameraRenderDetail.PostProcess;
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

        var mainWindowOutput = camera.Owner?.GetComponent<CameraOutput>();
        bool usesMainWindowViewport = !isWorldFbo && !isCameraOutputFbo &&
                                      mainWindowOutput is { Enabled: true, Target: CameraOutputTarget.MainWindow };

        int targetViewportX = 0;
        int targetViewportY = 0;
        int targetViewportW = targetW;
        int targetViewportH = targetH;
        if (usesMainWindowViewport)
        {
            float normalizedX = Math.Clamp(camera.NormalizedViewportX, 0.0f, 1.0f);
            float normalizedY = Math.Clamp(camera.NormalizedViewportY, 0.0f, 1.0f);
            float normalizedW = Math.Clamp(camera.NormalizedViewportWidth, 0.0f, 1.0f - normalizedX);
            float normalizedH = Math.Clamp(camera.NormalizedViewportHeight, 0.0f, 1.0f - normalizedY);

            targetViewportX = (int)MathF.Round(targetW * normalizedX);
            targetViewportY = (int)MathF.Round(targetH * normalizedY);
            targetViewportW = Math.Max(1, (int)MathF.Round(targetW * normalizedW));
            targetViewportH = Math.Max(1, (int)MathF.Round(targetH * normalizedH));
        }

        _device.DisableScissorTest();
        if (clearTarget)
        {
            _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);
            _device.Clear(camera.LetterboxColor, actualTargetFbo);
        }

        int vx = targetViewportX, vy = targetViewportY, vw = targetViewportW, vh = targetViewportH;
        float windowAspect = targetViewportH > 0 ? (float)targetViewportW / targetViewportH : 1f;
        float shotAspect = MathF.Max(0.01f, camera.TargetAspectRatio);

        if (camera.FixedAspectRatio)
        {
            if (windowAspect > shotAspect) { vw = (int)MathF.Round(targetViewportH * shotAspect); vx = targetViewportX + (targetViewportW - vw) / 2; }
            else { vh = (int)MathF.Round(targetViewportW / shotAspect); vy = targetViewportY + (targetViewportH - vh) / 2; }
        }

        int fVw = Math.Max(1, vw), fVh = Math.Max(1, vh);
        bool usesSubViewport = isScreenFbo || usesMainWindowViewport;
        if (usesSubViewport) {
            _device.SetViewport(vx, vy, (uint)fVw, (uint)fVh);
            camera.SetViewportRect(vx, targetH - (vy + fVh), fVw, fVh);
            _device.EnableScissorTest();
            _device.SetScissor(vx, vy, (uint)fVw, (uint)fVh);
            _device.Clear(resolvedBackgroundColor, actualTargetFbo);
            _device.DisableScissorTest();
        } else {
            _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);
            camera.SetViewportRect(0, 0, targetW, targetH);
            _device.Clear(resolvedBackgroundColor, actualTargetFbo);
        }

        var projection = camera.GetProjectionMatrix(usesSubViewport ? (fVw / (float)fVh) : windowAspect);
        var view = camera.GetViewMatrix();

        if (renderOutlineOnly)
        {
            RenderWorldOutline(world, camera, actualTargetFbo, usesSubViewport ? (vx, vy, fVw, fVh) : null, targetW, targetH);
        }
        else
        {
        var allRenderers = CollectAllSortedRenderers(world);

        foreach (var r in allRenderers)
        {
            if (r is SpriteRenderer sr)
            {
                bool usesTextureAsset = TryGetTextureAsset(sr.TextureAsset, out var assetTexture);
                RenderTexture outputTexture = null!;
                bool usesCameraOutput = !usesTextureAsset && TryGetCameraOutputTexture(sr.CameraOutputName, out outputTexture);
                if (!usesTextureAsset && !usesCameraOutput && sr.Texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
                    sr.Texture = LoadTexture(sr.Sprite);

                var tex = usesTextureAsset ? assetTexture : (usesCameraOutput ? outputTexture : (sr.Texture ?? DefaultSprites.Square));
                if (tex == null) continue;

                SpriteSlice spriteSlice = SpriteImportUtility.CreateDefaultSlice(tex.Width, tex.Height, new Vector2(0.5f, 0.5f));
                if (!usesTextureAsset && !usesCameraOutput && TryResolveSpriteSlice(sr.Sprite, tex.Width, tex.Height, out _, out var resolvedSlice))
                    spriteSlice = resolvedSlice;

                Vector2 resolvedPivot = sr.UseSpritePivot ? spriteSlice.Pivot : sr.Pivot;
                var uvMin = new System.Numerics.Vector2(
                    spriteSlice.X / (float)Math.Max(1, tex.Width),
                    spriteSlice.Y / (float)Math.Max(1, tex.Height));
                var uvMax = new System.Numerics.Vector2(
                    (spriteSlice.X + spriteSlice.Width) / (float)Math.Max(1, tex.Width),
                    (spriteSlice.Y + spriteSlice.Height) / (float)Math.Max(1, tex.Height));

                if (usesSubViewport) _device.SetViewport(vx, vy, (uint)fVw, (uint)fVh);
                else _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);

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
                
                activeShader.Draw(_quadBuffer, actualTargetFbo);
            }
            else if (r is TilemapRenderer tr)
            {
                if (usesSubViewport) _device.SetViewport(vx, vy, (uint)fVw, (uint)fVh);
                else _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);
                
                tr.Render(this, camera, projection, view, actualTargetFbo);
            }
            else if (r is PolygonRenderer pr)
            {
                FlushBrowserQuadBatch();
                var vertices = pr.GetWorldVertices();
                if (vertices.Length < 3) continue;

                if (usesSubViewport) _device.SetViewport(vx, vy, (uint)fVw, (uint)fVh);
                else _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);

                // 내부 채우기 (Fill) - 테두리는 그리지 않고 채우기만 수행
                if (pr.Fill)
                {
                    RenderPolygonFill(vertices, pr.Color, camera, actualTargetFbo, pr.Owner, pr.SortingLayerName);
                }
            }
        }

        }

        FlushBrowserQuadBatch();

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
            DrawLetterboxBars(camera, targetW, targetH, windowAspect, shotAspect, actualTargetFbo, isWorldFbo, usesSubViewport);
        }

        if (usePostProcess)
        {
            DrawLetterboxBars(camera, targetW, targetH, windowAspect, shotAspect, targetFbo, isWorldFbo, usesSubViewport);
        }

        _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);
    }

    private bool TryGetCameraOutputTargetSize(RenderTarget? targetFbo, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (targetFbo == null)
            return false;

        foreach (var target in _cameraOutputTargets.Values)
        {
            if (target.Target != targetFbo)
                continue;

            width = target.Width;
            height = target.Height;
            return true;
        }

        return false;
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

    private void DrawLetterboxBars(Camera camera, int targetW, int targetH, float windowAspect, float shotAspect, RenderTarget? targetFbo, bool isWorldFbo, bool isScreenFbo)
    {
        if (_whitePixel == null || !camera.FixedAspectRatio || isWorldFbo || isScreenFbo)
            return;

        _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);
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
            _shader.Draw(_quadBuffer, targetFbo);
            _shader.SetModel(pivot * Matrix4x4.CreateScale(barWidth, 2.0f, 1.0f) * Matrix4x4.CreateTranslation(barCenter, 0, 0));
            _shader.Draw(_quadBuffer, targetFbo);
        }
        else if (windowAspect < shotAspect)
        {
            float visibleHeight = windowAspect / shotAspect;
            float barHeight = 1.0f - visibleHeight;
            float barCenter = (1.0f + visibleHeight) * 0.5f;
            _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, barHeight, 1.0f) * Matrix4x4.CreateTranslation(0, barCenter, 0));
            _shader.Draw(_quadBuffer, targetFbo);
            _shader.SetModel(pivot * Matrix4x4.CreateScale(2.0f, barHeight, 1.0f) * Matrix4x4.CreateTranslation(0, -barCenter, 0));
            _shader.Draw(_quadBuffer, targetFbo);
        }
    }

    private void ApplyPostProcess(Camera camera, int w, int h, RenderTarget? targetFbo)
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

        _postProcessPasses.Clear();
        if (bloom?.Enabled == true) _postProcessPasses.Add((bloom.Order, "Bloom"));
        if (distortion?.Enabled == true) _postProcessPasses.Add((distortion.Order, "Distortion"));
        if (pixelate?.Enabled == true) _postProcessPasses.Add((pixelate.Order, "Pixelate"));
        if (chromaticAberration?.Enabled == true) _postProcessPasses.Add((chromaticAberration.Order, "ChromaticAberration"));
        if (motionBlur?.Enabled == true) _postProcessPasses.Add((motionBlur.Order, "MotionBlur"));
        if (colorAdjustments?.Enabled == true) _postProcessPasses.Add((colorAdjustments.Order, "ColorAdjustments"));
        if (vignette?.Enabled == true) _postProcessPasses.Add((vignette.Order, "Vignette"));
        for (int i = 0; i < customs.Count; i++)
        {
            if (customs[i].Enabled)
                _postProcessPasses.Add((customs[i].Order, $"Custom:{i}"));
        }

        _postProcessPasses.Sort((a, b) =>
        {
            int orderCompare = a.Order.CompareTo(b.Order);
            return orderCompare != 0 ? orderCompare : string.CompareOrdinal(a.Key, b.Key);
        });

        RenderTexture sourceTexture = _ppSceneTex;
        bool useFirstTarget = true;

        foreach (var pass in _postProcessPasses)
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

    private RenderTexture BuildBloomTexture(RenderTexture sourceTexture, int w, int h, BloomSettings settings)
    {
        if (_ppSceneTex == null || _ppBloomTex1 == null || _ppBloomTex2 == null || _ppBloomFbo1 == null || _ppBloomFbo2 == null ||
            _brightExtractShader == null || _blurShader == null)
        {
            return sourceTexture;
        }

        int downsample = Math.Max(1, _ppBloomDownsample);
        int bw = Math.Max(1, w / downsample);
        int bh = Math.Max(1, h / downsample);

        _device.SetViewport(0, 0, (uint)bw, (uint)bh);
        _device.Clear(Verity.Core.Color.Black, _ppBloomFbo1);
        _brightExtractShader.SetTexture("uTexture", sourceTexture);
        _brightExtractShader.SetFloat("uThreshold", settings.Threshold);
        _brightExtractShader.Draw(_quadBuffer, _ppBloomFbo1);

        RenderTexture source = _ppBloomTex1;
        int iterations = Math.Clamp(settings.BlurIterations, 1, 8);
        float radius = Math.Max(0.25f, settings.Scatter);

        for (int i = 0; i < iterations; i++)
        {
            _blurShader.SetTexture("uTexture", source);
            _blurShader.SetVec2("uDirection", System.Numerics.Vector2.UnitX);
            _blurShader.SetFloat("uRadius", radius);
            _blurShader.Draw(_quadBuffer, _ppBloomFbo2);

            _blurShader.SetTexture("uTexture", _ppBloomTex2);
            _blurShader.SetVec2("uDirection", System.Numerics.Vector2.UnitY);
            _blurShader.SetFloat("uRadius", radius);
            _blurShader.Draw(_quadBuffer, _ppBloomFbo1);

            source = _ppBloomTex1;
        }

        return _ppBloomTex1;
    }

    private RenderTexture? ApplyCustomPostProcess(CustomPostProcessSettings settings, RenderTexture sourceTexture, int w, int h, RenderTarget destFbo, RenderTexture destTex)
    {
        if (_ppHistoryTex == null)
            return null;

        var styleRuntime = ResolveStyle(settings.Style, PostProcessShaders.ScreenVertex, "postprocess");
        if (styleRuntime?.Shader == null)
            return null;

        var shader = styleRuntime.Shader;
        _device.SetViewport(0, 0, (uint)w, (uint)h);
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
        shader.Draw(_quadBuffer, destFbo);

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

    private void ApplyScreenShader(RenderTarget destFbo, int w, int h, Shader2D? shader, Action<Shader2D> configure)
    {
        if (shader == null)
            return;

        _device.SetViewport(0, 0, (uint)w, (uint)h);
        _device.Clear(Verity.Core.Color.Clear, destFbo);
        configure(shader);
        shader.Draw(_quadBuffer, destFbo);
    }

    public void BlitTexture(RenderTexture source, RenderTarget? targetFbo, int w, int h)
    {
        if (_copyShader == null)
            return;

        _device.SetViewport(0, 0, (uint)w, (uint)h);
        _copyShader.SetTexture("uTexture", source);
        _copyShader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _copyShader.Draw(_quadBuffer, targetFbo);
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
        _occluderCandidates.Clear();
        foreach (var occluder in _frameShadowOccluders)
        {
            if (occluder.Owner == owner && !occluder.AffectsOwner)
                continue;

            float distanceSquared = Vector2.DistanceSquared(ownerPosition, ClosestPointOnBounds(ownerPosition, occluder.Min, occluder.Max));
            _occluderCandidates.Add(new OccluderCandidate(occluder, distanceSquared));
        }

        _occluderCandidates.Sort(static (a, b) => a.DistanceSquared.CompareTo(b.DistanceSquared));

        int occluderCount = 0;
        int vertexCount = 0;
        foreach (var candidate in _occluderCandidates)
        {
            if (occluderCount >= MaxShaderOccluders)
                break;

            var occluder = candidate.Occluder;
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
        _shadowPolygonScratch.Clear();
        CollectColliderShadowPolygons(entity, _shadowPolygonScratch);
        bool colliderAdded = false;
        bool hasRendererCaster = false;

        if (entity.GetComponent<SpriteRenderer>() is SpriteRenderer spriteRenderer && spriteRenderer.Enabled && spriteRenderer.CastShadows)
        {
            hasRendererCaster = true;
            AppendRendererShadowOccluders(entity, spriteRenderer.ShadowSourceMode, _shadowPolygonScratch, ref colliderAdded, BuildSpriteOccluders(spriteRenderer), spriteRenderer.ShadowSelfMode == ShadowSelfMode.AffectSelf);
        }

        if (entity.GetComponent<PolygonRenderer>() is PolygonRenderer polygonRenderer && polygonRenderer.Enabled && polygonRenderer.CastShadows)
        {
            hasRendererCaster = true;
            AppendRendererShadowOccluders(entity, polygonRenderer.ShadowSourceMode, _shadowPolygonScratch, ref colliderAdded, BuildPolygonOccluders(polygonRenderer), polygonRenderer.ShadowSelfMode == ShadowSelfMode.AffectSelf);
        }

        if (entity.GetComponent<TilemapRenderer>() is TilemapRenderer tilemapRenderer && tilemapRenderer.Enabled && tilemapRenderer.CastShadows)
        {
            hasRendererCaster = true;
            AppendRendererShadowOccluders(entity, tilemapRenderer.ShadowSourceMode, _shadowPolygonScratch, ref colliderAdded, BuildTilemapOccluders(tilemapRenderer), tilemapRenderer.ShadowSelfMode == ShadowSelfMode.AffectSelf);
        }

        if (!hasRendererCaster)
            AddShadowOccluders(entity, _shadowPolygonScratch, _shadowPolygonScratch.Count > 0 && AnyColliderAffectsSelf(entity));
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

    private static void CollectColliderShadowPolygons(Entity entity, List<Vector2[]> polygons)
    {
        foreach (var shape in entity.GetComponents<PhysicalShape>())
        {
            if (!shape.Enabled || !shape.CastShadows)
                continue;

            AppendShapeShadowPolygons(shape, polygons);
        }
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
            if (!TryResolveAssetPath(renderer.Sprite.Path, renderer.Sprite.Guid, out string resolvedPath))
                return BuildSpriteQuadOccluders(renderer, renderer.UseSpritePivot ? new Vector2(0.5f, 0.5f) : renderer.Pivot);

            var raw = _textureManager.GetRawPixels(resolvedPath);
            SpriteSlice slice = TryResolveSpriteSlice(renderer.Sprite, raw.Width, raw.Height, out _, out var cachedSlice)
                ? cachedSlice
                : SpriteImportUtility.CreateDefaultSlice(raw.Width, raw.Height, new Vector2(0.5f, 0.5f));
            Vector2 resolvedPivot = renderer.UseSpritePivot ? slice.Pivot : renderer.Pivot;
            int alphaThresholdByte = Math.Clamp((int)MathF.Round(Math.Clamp(renderer.ShadowAlphaThreshold, 0.0f, 1.0f) * 255.0f), 0, 255);
            string cacheKey = NormalizeInsensitiveCacheKey($"{resolvedPath}|{renderer.Sprite.SpriteId}|{slice.X}|{slice.Y}|{slice.Width}|{slice.Height}|{alphaThresholdByte}");

            if (!_spriteShadowShapeCache.TryGetValue(cacheKey, out Vector2[][]? localPolygons))
            {
                localPolygons = BuildSpriteShadowShapes(raw.Pixels, raw.Width, raw.Height, slice, (byte)alphaThresholdByte);
                _spriteShadowShapeCache.Set(cacheKey, localPolygons);
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
        
        if (!TryResolveAssetPath(asset.Path, asset.Guid, out string fullPath))
        {
            Verity.Core.Debug.LogError($"[RenderPipeline] Style asset not found: {asset.Path}");
            return null;
        }
        try {
            string json = File.ReadAllText(fullPath);
            var data = StyleData.FromJson(json);
            if (data == null) return null;
            var runtime = new StyleRuntime();
            string? normalizedShaderPath = NormalizeEmbeddedAssetPath(data.ShaderPath);
            if (!string.IsNullOrWhiteSpace(normalizedShaderPath))
                runtime.Shader = ResolveShader(new ShaderAsset(normalizedShaderPath), defaultVertexSource, cacheScope);
            foreach (var (k, v) in data.Floats) runtime.Floats[k] = v;
            foreach (var (k, v) in data.Vector2s) runtime.Vector2s[k] = v;
            foreach (var (k, v) in data.Vector3s) runtime.Vector3s[k] = v;
            foreach (var (k, v) in data.Vector4s) runtime.Vector4s[k] = v;
            foreach (var (k, v) in data.Colors) runtime.Colors[k] = v;
            foreach (var (k, v) in data.Textures) {
                string? normalizedTexturePath = NormalizeEmbeddedAssetPath(v);
                if (!string.IsNullOrWhiteSpace(normalizedTexturePath) && TryResolveAssetPath(normalizedTexturePath, null, out string texPath))
                    runtime.Textures[k] = _textureManager.Load(texPath);
            }
            _styleCache.Set(key, runtime);
            return runtime;
        } catch (Exception e) {
            Verity.Core.Debug.LogError($"[RenderPipeline] Failed to load style {key}: {e.Message}");
            return null;
        }
    }

    private Shader2D? ResolveShader(ShaderAsset asset, string? defaultVertexSource = null, string cacheScope = "shader")
    {
        if (string.IsNullOrWhiteSpace(asset.Path)) return null;
        string key = $"{cacheScope}:{GetCacheKey(asset.Path)}";
        if (_shaderCache.TryGetValue(key, out var cached)) return cached;
        
        if (!TryResolveAssetPath(asset.Path, asset.Guid, out string fullPath)) return null;
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
            _shaderCache.Set(key, shader);
            return shader;
        } catch (Exception e) { 
            Verity.Core.Debug.LogError($"[RenderPipeline] Failed to compile shader {key}: {e.Message}");
            return null; 
        }
    }

    private string ResolveAssetPath(string p, string? guid = null) => AssetPathUtility.ResolvePath(BaseAssetsPath, p, guid);

    private static string? NormalizeEmbeddedAssetPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        string normalized = path.Replace('\\', '/');
        int assetsIndex = normalized.LastIndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
        if (assetsIndex >= 0)
            return normalized[(assetsIndex + 1)..];

        if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            return normalized;

        return path;
    }

    private bool TryResolveAssetPath(string p, string? guid, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(p))
            return false;

        string key = NormalizeInsensitiveCacheKey(string.IsNullOrWhiteSpace(guid) ? GetCacheKey(p) : $"{guid}:{GetCacheKey(p)}");
        if (!_resolvedAssetCache.TryGetValue(key, out var cached))
        {
            string resolved = ResolveAssetPath(p, guid);
            cached = new ResolvedAssetInfo(resolved, File.Exists(resolved));
            _resolvedAssetCache.Set(key, cached);
        }

        fullPath = cached.Path;
        return cached.Exists;
    }

    private bool TryResolveSpriteSlice(Sprite sprite, int textureWidth, int textureHeight, out string resolvedPath, out SpriteSlice spriteSlice)
    {
        spriteSlice = SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, new Vector2(0.5f, 0.5f));
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(sprite.Path))
            return false;

        try
        {
            if (!TryResolveAssetPath(sprite.Path, sprite.Guid, out resolvedPath))
                return false;

            string cacheKey = NormalizeInsensitiveCacheKey($"{resolvedPath}|{sprite.SpriteId}|{textureWidth}|{textureHeight}");
            if (_spriteSliceCache.TryGetValue(cacheKey, out var cachedSlice))
            {
                spriteSlice = cachedSlice;
            }
            else
            {
                spriteSlice = AssetPathUtility.ResolveSpriteSlice(resolvedPath, sprite, textureWidth, textureHeight);
                _spriteSliceCache.Set(cacheKey, spriteSlice);
            }

            return true;
        }
        catch
        {
            resolvedPath = string.Empty;
            spriteSlice = SpriteImportUtility.CreateDefaultSlice(textureWidth, textureHeight, new Vector2(0.5f, 0.5f));
            return false;
        }
    }

    public bool TryGetSpriteUv(Sprite sprite, RenderTexture texture, out System.Numerics.Vector2 uvMin, out System.Numerics.Vector2 uvMax)
    {
        uvMin = System.Numerics.Vector2.Zero;
        uvMax = System.Numerics.Vector2.One;
        if (!TryResolveSpriteSlice(sprite, texture.Width, texture.Height, out _, out var slice))
            return false;

        uvMin = new System.Numerics.Vector2(
            slice.X / (float)Math.Max(1, texture.Width),
            slice.Y / (float)Math.Max(1, texture.Height));
        uvMax = new System.Numerics.Vector2(
            (slice.X + slice.Width) / (float)Math.Max(1, texture.Width),
            (slice.Y + slice.Height) / (float)Math.Max(1, texture.Height));
        return true;
    }

    private List<Component> CollectAllSortedRenderers(World w)
    {
        _rendererSortItems.Clear();
        _sortedRenderers.Clear();

        var entities = w.GetAllEntities();
        for (int i = 0; i < entities.Count; i++)
        {
            var entity = entities[i];
            if (!entity.Active)
                continue;

            if (entity.GetComponent<SpriteRenderer>() is SpriteRenderer spriteRenderer && spriteRenderer.Enabled)
            {
                _rendererSortItems.Add(new RendererSortItem(
                    spriteRenderer,
                    i,
                    spriteRenderer.ResolvedLayerIndex,
                    spriteRenderer.OrderInLayer,
                    GetSortAxisValue(entity.Transform)));
            }

            if (entity.GetComponent<TilemapRenderer>() is TilemapRenderer tilemapRenderer && tilemapRenderer.Enabled)
            {
                _rendererSortItems.Add(new RendererSortItem(
                    tilemapRenderer,
                    i,
                    tilemapRenderer.ResolvedLayerIndex,
                    tilemapRenderer.OrderInLayer,
                    GetSortAxisValue(entity.Transform)));
            }

            if (entity.GetComponent<PolygonRenderer>() is PolygonRenderer polygonRenderer && polygonRenderer.Enabled)
            {
                _rendererSortItems.Add(new RendererSortItem(
                    polygonRenderer,
                    i,
                    polygonRenderer.ResolvedLayerIndex,
                    polygonRenderer.OrderInLayer,
                    GetSortAxisValue(entity.Transform)));
            }
        }

        _rendererSortItems.Sort((a, b) =>
        {
            int layerComparison = a.LayerIndex.CompareTo(b.LayerIndex);
            if (layerComparison != 0) return layerComparison;

            int orderComparison = a.OrderInLayer.CompareTo(b.OrderInLayer);
            if (orderComparison != 0) return orderComparison;

            int hierarchyComparison = a.HierarchyOrder.CompareTo(b.HierarchyOrder);
            if (hierarchyComparison != 0) return hierarchyComparison;

            int axisComparison = SortAxisAscending ? a.SortAxisValue.CompareTo(b.SortAxisValue) : b.SortAxisValue.CompareTo(a.SortAxisValue);
            return axisComparison != 0 ? axisComparison : a.Renderer.Owner.Id.CompareTo(b.Renderer.Owner.Id);
        });

        foreach (var item in _rendererSortItems)
            _sortedRenderers.Add(item.Renderer);

        return _sortedRenderers;
    }
    private float GetSortAxisValue(Transform t) => CustomSortAxis switch { SortAxis.X => t.WorldPosition.X, SortAxis.Y => t.WorldPosition.Y, _ => 0f };
    private static Matrix4x4 BuildModelMatrix(Transform t, SpriteRenderer sr, Vector2 pivot) { 
        var wm = t.GetWorldMatrix(); 
        var localSprite = Matrix4x4.CreateTranslation(-pivot.X, -pivot.Y, 0) * Matrix4x4.CreateScale(sr.Size.X * (sr.FlipX ? -1f : 1f), sr.Size.Y * (sr.FlipY ? -1f : 1f), 1f);
        return localSprite * wm;
    }

    private void RenderWorldOutline(World world, Camera camera, RenderTarget? targetFbo, (int x, int y, int w, int h)? viewport, int targetW, int targetH)
    {
        if (viewport.HasValue)
        {
            var v = viewport.Value;
            _device.SetViewport(v.x, v.y, (uint)v.w, (uint)v.h);
        }
        else
        {
            _device.SetViewport(0, 0, (uint)targetW, (uint)targetH);
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

            RenderTexture? texture = sr.Texture;
        if (texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
            texture = LoadTexture(sr.Sprite);

        if (texture == null || string.IsNullOrWhiteSpace(sr.Sprite.Path))
            return sr.Pivot;

        if (TryResolveSpriteSlice(sr.Sprite, texture.Width, texture.Height, out _, out var spriteSlice))
            return spriteSlice.Pivot;

        return sr.Pivot;
    }

    private void RenderTilemapOutline(TilemapRenderer tr, float thickness, Verity.Core.Color color, Camera camera, RenderTarget? fbo)
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

    private void RenderPolygonOutline(PolygonRenderer renderer, float thickness, Verity.Core.Color color, Camera camera, RenderTarget? fbo)
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

    public void RenderGizmoLine(Vector2 s, Vector2 e, float t, Verity.Core.Color c, Camera cam, RenderTarget? fbo = null) { if (_whitePixel == null) return; ConfigureUnlitShader(_shader); _shader.SetProjection(cam.GetProjectionMatrix()); _shader.SetView(cam.GetViewMatrix()); var dir = e - s; float len = dir.Length(); if (len < 0.0001f) return; float ang = MathF.Atan2(dir.Y, dir.X); _shader.SetModel(Matrix4x4.CreateTranslation(0, -0.5f, 0) * Matrix4x4.CreateScale(len, t, 1f) * Matrix4x4.CreateRotationZ(ang) * Matrix4x4.CreateTranslation(s.X, s.Y, 0)); _shader.SetTexture(_whitePixel); _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One); _shader.SetColor(c); _shader.Draw(_quadBuffer, fbo); }

    public void RenderGizmoRect(Vector2 center, Vector2 size, float rotationDeg, float thickness, Verity.Core.Color color, Camera cam, RenderTarget? fbo = null)
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

    public void RenderGizmoQuad(Vector2 center, Vector2 size, Verity.Core.Color color, Camera cam, RenderTarget? fbo = null)
    {
        if (_whitePixel == null) return;
        ConfigureUnlitShader(_shader);
        _shader.SetProjection(cam.GetProjectionMatrix());
        _shader.SetView(cam.GetViewMatrix());
        _shader.SetModel(Matrix4x4.CreateScale(size.X, size.Y, 1f) * Matrix4x4.CreateTranslation(center.X, center.Y, 0));
        _shader.SetTexture(_whitePixel);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(color);
        _shader.Draw(_quadBuffer, fbo);
    }

    public void DrawTile(RenderTexture tex, Matrix4x4 model, Verity.Core.Color color, Matrix4x4 projection, Matrix4x4 view, RenderTarget? fbo, Entity? owner = null, string? sortingLayerName = null, System.Numerics.Vector2? uvMin = null, System.Numerics.Vector2? uvMax = null)
    {
        System.Numerics.Vector2 resolvedUvMin = uvMin ?? System.Numerics.Vector2.Zero;
        System.Numerics.Vector2 resolvedUvMax = uvMax ?? System.Numerics.Vector2.One;

        if (OperatingSystem.IsBrowser())
        {
            if (!CanUseBrowserQuadBatch(tex, projection, view, fbo, color))
            {
                FlushBrowserQuadBatch();
                BeginBrowserQuadBatch(tex, projection, view, fbo, color);
            }

            EnqueueBrowserQuad(model, resolvedUvMin, resolvedUvMax);
            return;
        }

        ApplyLighting(_shader, owner, sortingLayerName);
        _shader.SetProjection(projection);
        _shader.SetView(view);
        _shader.SetModel(model);
        _shader.SetTexture(tex);
        _shader.SetUvRect(resolvedUvMin, resolvedUvMax);
        _shader.SetColor(color);
        _shader.Draw(_quadBuffer, fbo);
    }

    public void FlushBrowserQuadBatch()
    {
        if (!_browserBatchActive || _browserBatchTexture == null || _browserQuadIndices.Count == 0)
            return;

        var data = RenderMeshData.CreatePositionTexture2D();
        for (int i = 0; i < _browserQuadVertices.Count; i += 4)
            data.AddVertex(new Vector2(_browserQuadVertices[i], _browserQuadVertices[i + 1]), new Vector2(_browserQuadVertices[i + 2], _browserQuadVertices[i + 3]));

        using RenderMesh uploaded = _dynamicBuffer.Upload(data, _browserQuadIndices.ToArray());
        ConfigureUnlitShader(_shader);
        _shader.SetProjection(_browserBatchProjection);
        _shader.SetView(_browserBatchView);
        _shader.SetModel(Matrix4x4.Identity);
        _shader.SetTexture(_browserBatchTexture);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(_browserBatchColor);
        _shader.Draw(uploaded, _browserBatchTarget);

        _browserQuadVertices.Clear();
        _browserQuadIndices.Clear();
        _browserBatchTexture = null;
        _browserBatchTarget = null;
        _browserBatchProjection = Matrix4x4.Identity;
        _browserBatchView = Matrix4x4.Identity;
        _browserBatchColor = Verity.Core.Color.White;
        _browserBatchActive = false;
    }

    private bool CanUseBrowserQuadBatch(RenderTexture texture, Matrix4x4 projection, Matrix4x4 view, RenderTarget? fbo, Verity.Core.Color color)
    {
        return _browserBatchActive &&
               ReferenceEquals(_browserBatchTexture, texture) &&
               ReferenceEquals(_browserBatchTarget, fbo) &&
               _browserBatchProjection == projection &&
               _browserBatchView == view &&
               _browserBatchColor.Equals(color);
    }

    private void BeginBrowserQuadBatch(RenderTexture texture, Matrix4x4 projection, Matrix4x4 view, RenderTarget? fbo, Verity.Core.Color color)
    {
        _browserBatchTexture = texture;
        _browserBatchTarget = fbo;
        _browserBatchProjection = projection;
        _browserBatchView = view;
        _browserBatchColor = color;
        _browserBatchActive = true;
    }

    private void EnqueueBrowserQuad(Matrix4x4 model, System.Numerics.Vector2 uvMin, System.Numerics.Vector2 uvMax)
    {
        int vertexBase = _browserQuadVertices.Count / 4;
        Vector2 topLeft = TransformPoint(0f, 0f, model);
        Vector2 topRight = TransformPoint(1f, 0f, model);
        Vector2 bottomLeft = TransformPoint(0f, 1f, model);
        Vector2 bottomRight = TransformPoint(1f, 1f, model);

        AddBrowserBatchVertex(topLeft, new System.Numerics.Vector2(uvMin.X, uvMin.Y));
        AddBrowserBatchVertex(topRight, new System.Numerics.Vector2(uvMax.X, uvMin.Y));
        AddBrowserBatchVertex(bottomLeft, new System.Numerics.Vector2(uvMin.X, uvMax.Y));
        AddBrowserBatchVertex(bottomRight, new System.Numerics.Vector2(uvMax.X, uvMax.Y));

        _browserQuadIndices.Add(vertexBase + 0);
        _browserQuadIndices.Add(vertexBase + 2);
        _browserQuadIndices.Add(vertexBase + 1);
        _browserQuadIndices.Add(vertexBase + 1);
        _browserQuadIndices.Add(vertexBase + 2);
        _browserQuadIndices.Add(vertexBase + 3);
    }

    private void AddBrowserBatchVertex(Vector2 position, System.Numerics.Vector2 uv)
    {
        _browserQuadVertices.Add(position.X);
        _browserQuadVertices.Add(position.Y);
        _browserQuadVertices.Add(uv.X);
        _browserQuadVertices.Add(uv.Y);
    }

    public void DrawText(TextRenderOptions options, Matrix4x4 projection, Matrix4x4 view, RenderTarget? fbo = null)
    {
        if (_textRenderer == null)
            return;

        ConfigureUnlitShader(_shader);
        _textRenderer.DrawText(options, projection, view, fbo);
    }

    public RenderTexture? LoadTexture(string path, string? guid = null)
    {
        try {
            if (TryResolveAssetPath(path, guid, out string fp))
            {
                var settings = AssetPathUtility.TryGetSpriteImportSettings(fp);
                return _textureManager.Load(fp, settings?.Filter ?? SpriteTextureFilter.Point);
            }
        } catch { }
        return null;
    }

    public RenderTexture? LoadTexture(Sprite sprite) => LoadTexture(sprite.Path, sprite.Guid);

    private void RenderPolygonFill(Vector2[] vertices, int[] indices, Verity.Core.Color color, Camera cam, RenderTarget? fbo, Entity? owner, string? sortingLayerName = null)
    {
        if (_whitePixel == null || vertices.Length < 3 || indices.Length < 3) return;

        ApplyLighting(_shader, owner, sortingLayerName);
        _shader.SetProjection(cam.GetProjectionMatrix());
        _shader.SetView(cam.GetViewMatrix());
        _shader.SetModel(Matrix4x4.Identity);
        _shader.SetTexture(_whitePixel);
        _shader.SetUvRect(System.Numerics.Vector2.Zero, System.Numerics.Vector2.One);
        _shader.SetColor(color);

        var data = RenderMeshData.CreatePositionTexture2D();
        for (int i = 0; i < vertices.Length; i++)
        {
            data.AddVertex(vertices[i], new Vector2(0.5f, 0.5f));
        }

        using var uploaded = _dynamicBuffer.Upload(data, indices);
        _shader.Draw(uploaded, fbo);
    }

    private unsafe void RenderPolygonFill(Vector2[] vertices, Verity.Core.Color color, Camera cam, RenderTarget? fbo, Entity? owner, string? sortingLayerName = null)
    {
        if (vertices.Length < 3) return;
        int[] indices = new int[(vertices.Length - 2) * 3];
        for (int i = 0; i < vertices.Length - 2; i++) {
            indices[i * 3 + 0] = 0; indices[i * 3 + 1] = i + 1; indices[i * 3 + 2] = i + 2;
        }
        RenderPolygonFill(vertices, indices, color, cam, fbo, owner, sortingLayerName);
    }

    public void ClearCache() { _shaderCache.Clear(); _styleCache.Clear(); _spriteShadowShapeCache.Clear(); _resolvedAssetCache.Clear(); _spriteSliceCache.Clear(); }
    public void ClearStyleCache(string path)
    {
        string key = NormalizeInsensitiveCacheKey(GetCacheKey(path));
        foreach (var cacheKey in _styleCache.Keys.Where(k => k.EndsWith(key, StringComparison.OrdinalIgnoreCase)).ToList())
            _styleCache.Remove(cacheKey);
    }
    public void ClearShaderCache(string path) { 
        string key = NormalizeInsensitiveCacheKey(GetCacheKey(path));
        foreach (var cacheKey in _shaderCache.Keys.Where(k => k.EndsWith(key, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_shaderCache.TryGetValue(cacheKey, out var shader) && _shaderCache.Remove(cacheKey))
                shader.Dispose();
        }
        _styleCache.Clear(); // Shaders affect styles, so clear both
    }

    private static string NormalizeInsensitiveCacheKey(string key) => key.ToUpperInvariant();

    public void Dispose() 
    { 
        _worldFbo?.Dispose(); _worldColorTex?.Dispose(); 
        _screenFbo?.Dispose(); _screenColorTex?.Dispose(); 
        foreach (var target in _cameraOutputTargets.Values)
            target.Dispose();
        _cameraOutputTargets.Clear();
        
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
        _textRenderer?.Dispose();

        _shaderCache.Dispose();
        _styleCache.Dispose();
        _spriteShadowShapeCache.Dispose();
        _resolvedAssetCache.Dispose();
        _spriteSliceCache.Dispose();
        _quadBuffer.Dispose(); 
    }
}
