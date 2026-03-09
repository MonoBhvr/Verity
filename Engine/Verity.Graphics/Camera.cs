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

    private int _viewportX;
    private int _viewportY;
    private int _viewportWidth;
    private int _viewportHeight;

    [SerializeField]
    public bool FixedAspectRatio { get; set; } = false;

    [SerializeField]
    public float AspectWidth { get; set; } = 16f;

    [SerializeField]
    public float AspectHeight { get; set; } = 9f;

    public float TargetAspectRatio => (AspectWidth > 0 && AspectHeight > 0) ? AspectWidth / AspectHeight : 1f;

    public int ViewportX => _viewportX;
    public int ViewportY => _viewportY;
    public int ViewportWidth => _viewportWidth;
    public int ViewportHeight => _viewportHeight;
    
    public float CurrentAspectRatio
    {
        get
        {
            if (_viewportHeight <= 0 || _viewportWidth <= 0) 
            {
                return (AspectWidth > 0 && AspectHeight > 0) ? AspectWidth / AspectHeight : 1.777f;
            }
            return (float)_viewportWidth / _viewportHeight;
        }
    }

    public enum OrthographicScalingMode
    {
        FixedHeight,    // Standard: Vertical size stays constant (Unity default)
        ConstantArea    // New: Total visible world area stays constant
    }

    [SerializeField]
    public OrthographicScalingMode ScalingMode { get; set; } = OrthographicScalingMode.FixedHeight;

    public float GetCalculatedAspect() => FixedAspectRatio ? TargetAspectRatio : CurrentAspectRatio;

    public float VisibleHalfHeight
    {
        get
        {
            return OrthographicSize * Zoom;
        }
    }

    public float VisibleHalfWidth
    {
        get
        {
            return VisibleHalfHeight * GetCalculatedAspect();
        }
    }

    public Matrix4x4 GetProjectionMatrix()
    {
        float hH = VisibleHalfHeight;
        float hW = VisibleHalfWidth;
        return Matrix4x4.CreateOrthographicOffCenter(-hW, hW, -hH, hH, -1f, 1f);
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

    /// <summary>
    /// 입력된 좌표(픽셀)를 월드 좌표로 변환합니다. 
    /// screenPos는 렌더링 타겟(FBO 또는 Window)의 좌상단(0,0) 기준 픽셀 좌표여야 합니다.
    /// </summary>
    public void SetViewportRect(int x, int y, int width, int height)
    {
        _viewportX = x;
        _viewportY = y;
        _viewportWidth = width;
        _viewportHeight = height;
    }

    public void SetViewportSize(int width, int height)
    {
        SetViewportRect(0, 0, width, height);
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        // 1. 뷰포트 상대 좌표로 변환 (레터박스 제외 영역으로 0~1 매핑)
        float localX = screenPos.X - _viewportX;
        float localY = screenPos.Y - _viewportY;

        // 2. NDC (-1 to 1) 변환 (Y축 반전 고려)
        float ndcX = (2f * localX / _viewportWidth) - 1f;
        float ndcY = 1f - (2f * localY / _viewportHeight);

        // 3. 역행렬을 이용한 월드 좌표 산출
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

        float screenX = (clip.X + 1f) * 0.5f * _viewportWidth + _viewportX;
        float screenY = (1f - clip.Y) * 0.5f * _viewportHeight + _viewportY;
        return new Vector2(screenX, screenY);
    }
}
