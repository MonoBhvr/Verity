using System.Numerics;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Graphics;

public enum CameraRenderDetail
{
    Outline = 0,
    Basic = 1,
    Lighting = 2,
    PostProcess = 3
}

public class Camera : Component
{
    public Camera()
    {
    }

    public static Camera? Main => CameraSelection.GetDefaultCamera(WorldManager.ActiveWorld);

    [SerializeField] 
    public float OrthographicSize { get; set; } = 5.0f;

    [SerializeField]
    public Verity.Core.Color BackgroundColor { get; set; } = new(0.1f, 0.1f, 0.1f, 1.0f);

    [SerializeField]
    public Verity.Core.Color LetterboxColor { get; set; } = Verity.Core.Color.Black;

    [SerializeField]
    public float Zoom { get; set; } = 1.0f;

    [HideInInspector]
    public Vector2 Position { get; set; } = Vector2.Zero;

    private float _rotation;
    [HideInInspector]
    public float Rotation
    {
        get => _rotation;
        set => _rotation = value % 360f;
    }

    [SerializeField]
    public bool FixedAspectRatio { get; set; } = false;

    [SerializeField]
    public float AspectWidth { get; set; } = 16f;

    [SerializeField]
    public float AspectHeight { get; set; } = 9f;

    [SerializeField]
    public int RenderWidth { get; set; }

    [SerializeField]
    public int RenderHeight { get; set; }

    [SerializeField]
    public bool IntegerScaling { get; set; } = false;

    public bool HasExplicitResolution => RenderWidth > 0 && RenderHeight > 0;

    [SerializeField]
    public PostProcessSettings PostProcess { get; set; } = new();

    [SerializeField]
    public float NormalizedViewportX { get; set; } = 0.0f;

    [SerializeField]
    public float NormalizedViewportY { get; set; } = 0.0f;

    [SerializeField]
    public float NormalizedViewportWidth { get; set; } = 1.0f;

    [SerializeField]
    public float NormalizedViewportHeight { get; set; } = 1.0f;

    [HideInInspector]
    public CameraRenderDetail RenderDetail { get; set; } = CameraRenderDetail.PostProcess;

    [HideInInspector]
    public bool ShowGizmos { get; set; } = true;

    private int _viewportX, _viewportY, _viewportW, _viewportH;

    public int ViewportX => _viewportX;
    public int ViewportY => _viewportY;
    public int ViewportWidth => _viewportW;
    public int ViewportHeight => _viewportH;

    public float TargetAspectRatio => (AspectWidth > 0.0001f && AspectHeight > 0.0001f) ? AspectWidth / AspectHeight : 1.777f;
    public float CurrentAspectRatio => _viewportH > 0 ? (float)_viewportW / _viewportH : TargetAspectRatio;

    // Editor Helpers
    public float VisibleHalfHeight => OrthographicSize * Zoom;
    public float VisibleHalfWidth => VisibleHalfHeight * (FixedAspectRatio ? TargetAspectRatio : CurrentAspectRatio);

    public void SetViewportRect(int x, int y, int w, int h)
    {
        _viewportX = x; _viewportY = y; _viewportW = Math.Max(1, w); _viewportH = Math.Max(1, h);
    }

    public void SetViewportSize(int w, int h) => SetViewportRect(0, 0, w, h);

    public Matrix4x4 GetProjectionMatrix()
    {
        return GetProjectionMatrix(CurrentAspectRatio);
    }

    public Matrix4x4 GetProjectionMatrix(float viewportAspect)
    {
        if (viewportAspect < 0.0001f) viewportAspect = 1.0f;
        float hH = VisibleHalfHeight;
        float hW = hH * viewportAspect;

        // 만약 고정 비율 모드라면, 촬영 범위(ShotAspect)가 왜곡되지 않도록 범위를 계산함
        if (FixedAspectRatio)
        {
            float shotAspect = TargetAspectRatio;
            hH = VisibleHalfHeight;
            hW = VisibleHalfWidth;

            if (viewportAspect > shotAspect) hW = hH * viewportAspect;
            else hH = hW / viewportAspect;
        }

        return Matrix4x4.CreateOrthographicOffCenter(-hW, hW, -hH, hH, -1f, 1f);
    }

    public Matrix4x4 GetViewMatrix()
    {
        Vector2 pos = Owner != null ? Owner.Transform.Position : Position;
        float rot = Owner != null ? Owner.Transform.Rotation : Rotation;
        rot = rot * MathF.PI / 180f;
        return Matrix4x4.CreateTranslation(-pos.X, -pos.Y, 0) * Matrix4x4.CreateRotationZ(-rot);
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        float lx = screenPos.X - _viewportX;
        float ly = screenPos.Y - _viewportY;
        float ndcX = _viewportW > 0 ? (2f * lx / _viewportW) - 1f : 0f;
        float ndcY = _viewportH > 0 ? 1f - (2f * ly / _viewportH) : 0f;
        var inv = Matrix4x4.Identity;
        if (Matrix4x4.Invert(GetViewMatrix() * GetProjectionMatrix(), out inv))
        {
            var w = Vector4.Transform(new Vector4(ndcX, ndcY, 0, 1), inv);
            return new Vector2(w.X, w.Y);
        }
        return Vector2.Zero;
    }

    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        var viewProj = GetViewMatrix() * GetProjectionMatrix();
        var clip = Vector4.Transform(new Vector4(worldPos.X, worldPos.Y, 0, 1), viewProj);
        float screenX = (clip.X + 1f) * 0.5f * _viewportW + _viewportX;
        float screenY = (1f - clip.Y) * 0.5f * _viewportH + _viewportY;
        return new Vector2(screenX, screenY);
    }
}
