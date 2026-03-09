using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Graphics;

public class Camera : Component
{
    [SerializeField]
    public float OrthographicSize { get; set; } = 5.0f;

    [SerializeField]
    public Verity.Core.Color BackgroundColor { get; set; } = new(0.1f, 0.1f, 0.1f, 1.0f);

    [SerializeField]
    public float Zoom { get; set; } = 1.0f;

    [HideInInspector]
    public Vector2 Position { get; set; } = Vector2.Zero;

    [HideInInspector]
    public float Rotation { get; set; } = 0.0f;

    private int _viewportWidth;
    private int _viewportHeight;

    public int ViewportWidth => _viewportWidth;
    public int ViewportHeight => _viewportHeight;
    public float AspectRatio => _viewportHeight > 0 ? _viewportWidth / (float)_viewportHeight : 1f;

    public Camera()
    {
    }

    public void SetViewportSize(int width, int height)
    {
        _viewportWidth = width;
        _viewportHeight = height;
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        float halfH = OrthographicSize * Zoom;
        float aspectRatio = AspectRatio;
        float halfW = halfH * aspectRatio;
        return Matrix4x4.CreateOrthographicOffCenter(-halfW, halfW, -halfH, halfH, -1f, 1f);
    }

    public Matrix4x4 GetViewMatrix()
    {
        Vector2 pos;
        float rot;

        if (Owner != null)
        {
            pos = Owner.Transform.Position;
            rot = Owner.Transform.Rotation * MathF.PI / 180f;
        }
        else
        {
            pos = Position;
            rot = Rotation * MathF.PI / 180f;
        }

        var translation = Matrix4x4.CreateTranslation(-pos.X, -pos.Y, 0);
        var rotation = Matrix4x4.CreateRotationZ(-rot);
        return translation * rotation;
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        float ndcX = (2f * screenPos.X / _viewportWidth) - 1f;
        float ndcY = 1f - (2f * screenPos.Y / _viewportHeight);

        var viewProj = GetViewMatrix() * GetProjectionMatrix();
        if (!Matrix4x4.Invert(viewProj, out var inverse))
            return Vector2.Zero;

        var world = Vector4.Transform(new Vector4(ndcX, ndcY, 0, 1), inverse);
        return new Vector2(world.X, world.Y);
    }

    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var viewProj = GetViewMatrix() * GetProjectionMatrix();
        var clip = Vector4.Transform(new Vector4(worldPos.X, worldPos.Y, 0, 1), viewProj);

        float screenX = (clip.X + 1f) * 0.5f * _viewportWidth;
        float screenY = (1f - clip.Y) * 0.5f * _viewportHeight;
        return new Vector2(screenX, screenY);
    }
}
