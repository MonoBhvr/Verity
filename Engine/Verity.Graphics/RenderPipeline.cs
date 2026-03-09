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
        _device.Clear(camera.BackgroundColor, targetFbo);

        var renderers = CollectRenderers(world);
        SortRenderers(renderers);

        var projection = camera.GetProjectionMatrix();
        var view = camera.GetViewMatrix();

        _shader.SetProjection(projection);
        _shader.SetView(view);

        foreach (var sr in renderers)
        {
            if (!sr.Enabled)
                continue;

            // Auto-load texture if path is set but texture is missing
            if (sr.Texture == null && !string.IsNullOrWhiteSpace(sr.Sprite.Path))
            {
                try {
                    string fullPath = ResolveAssetPath(sr.Sprite.Path);
                    if (File.Exists(fullPath))
                        sr.Texture = _textureManager.Load(fullPath);
                } catch { /* Handle gracefully */ }
            }

            var texture = sr.Texture ?? DefaultSprites.Square;
            if (texture == null)
                continue;

            var transform = sr.Owner.Transform;
            var model = BuildModelMatrix(transform, sr);

            _shader.SetModel(model);
            _shader.SetTexture(texture);
            _shader.SetColor(sr.Color);

            _shader.QuadBuffer.Draw(_shader.Program, targetFbo);
        }

        _debugDraw.Render(camera, targetFbo);
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
        var worldMatrix = transform.GetWorldMatrix();
        Matrix4x4.Decompose(worldMatrix, out var scale, out var rotation, out var translation);

        var scaleX = scale.X * (sr.FlipX ? -1f : 1f);
        var scaleY = scale.Y * (sr.FlipY ? -1f : 1f);

        var pivotOffset = Matrix4x4.CreateTranslation(-sr.Pivot.X, -sr.Pivot.Y, 0);
        var scaleMatrix = Matrix4x4.CreateScale(scaleX, scaleY, 1f);
        var rotMatrix = Matrix4x4.CreateFromQuaternion(rotation);
        var transMatrix = Matrix4x4.CreateTranslation(translation);

        return pivotOffset * scaleMatrix * rotMatrix * transMatrix;
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
        Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        if (_whitePixel == null) return;

        _shader.SetProjection(camera.GetProjectionMatrix());
        _shader.SetView(camera.GetViewMatrix());

        var pivotOffset = Matrix4x4.CreateTranslation(-0.5f, -0.5f, 0);
        var scale = Matrix4x4.CreateScale(size.X, size.Y, 1f);
        var translation = Matrix4x4.CreateTranslation(center.X, center.Y, 0);

        _shader.SetModel(pivotOffset * scale * translation);
        _shader.SetTexture(_whitePixel);
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
