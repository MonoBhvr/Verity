using System.Numerics;
using Irodori.Framebuffer;
using Irodori.Texture;
using Verity.Core;

namespace Verity.Graphics;

public class DebugDraw
{
    private readonly Shader2D _shader;
    private TextureObjectUploaded? _whitePixel;

    public DebugDraw(Shader2D shader)
    {
        _shader = shader;
    }

    public void SetWhitePixel(TextureObjectUploaded whitePixel)
    {
        _whitePixel = whitePixel;
    }

    public void Render(Camera camera, FramebufferObject.Uploaded? targetFbo = null)
    {
        if (_whitePixel == null) return;

        var lines = Debug.Lines;
        if (lines.Count == 0) return;

        _shader.SetProjection(camera.GetProjectionMatrix());
        _shader.SetView(camera.GetViewMatrix());

        foreach (var line in lines)
        {
            var model = BuildLineMatrix(line.Start, line.End, line.Thickness);
            _shader.SetModel(model);
            _shader.SetTexture(_whitePixel);
            _shader.SetColor(line.Color);
            _shader.QuadBuffer.Draw(_shader.Program, targetFbo);
        }
    }

    private static Matrix4x4 BuildLineMatrix(Vector2 start, Vector2 end, float thickness)
    {
        var dir = end - start;
        var length = dir.Length();
        if (length < 0.0001f) return Matrix4x4.Identity;

        var angle = MathF.Atan2(dir.Y, dir.X);

        var pivotOffset = Matrix4x4.CreateTranslation(0, -0.5f, 0);
        var scale = Matrix4x4.CreateScale(length, thickness, 1f);
        var rotation = Matrix4x4.CreateRotationZ(angle);
        var translation = Matrix4x4.CreateTranslation(start.X, start.Y, 0);

        return pivotOffset * scale * rotation * translation;
    }
}
