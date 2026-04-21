using System.Numerics;
using Verity.Core.ECS;
using Verity.Core;

namespace Verity.Core.Physics;

public abstract class PhysicalShape : Component
{
    [SerializeField]
    public bool IsSensor { get; set; } = false;

    [SerializeField]
    public Vector2 Offset { get; set; } = Vector2.Zero;

    [SerializeField, PhysicsGroupSelector]
    public string GroupName { get; set; } = "Default";

    [SerializeField]
    public bool CastShadows { get; set; } = true;

    [SerializeField]
    public ShadowSelfMode ShadowSelfMode { get; set; } = ShadowSelfMode.ExcludeSelf;

    public ulong GroupMask => Verity.Filter.Filter.Get(GroupName)?.Mask ?? Verity.Filter.FilterRegistry.GetGroupMask(GroupName);

    public Vector2 GetBaseScale()
    {
        var transform = Owner.GetComponent<Transform>();
        if (transform == null) return Vector2.One;

        var sizeComp = Owner.GetComponent<IHasSize>();
        return transform.WorldScale * (sizeComp?.Size ?? Vector2.One);
    }

    public Vector2 GetWorldCenter()
    {
        var transform = Owner.Transform;
        Vector2 baseScale = GetBaseScale();
        float rotationRad = transform.WorldRotation * MathF.PI / 180.0f;
        
        float cos = MathF.Cos(rotationRad);
        float sin = MathF.Sin(rotationRad);
        Vector2 rotatedOffset = new Vector2(Offset.X * baseScale.X * cos - Offset.Y * baseScale.Y * sin, 
                                            Offset.X * baseScale.X * sin + Offset.Y * baseScale.Y * cos);
        return transform.WorldPosition + rotatedOffset;
    }

    // AABB (Broad Phase용)
    public abstract AABB GetAABB();

    // Narrow Phase를 위한 기하학적 정보
    public abstract Vector2[] GetVertices();

    // 관성 모멘트 계산을 위한 계수 (Mass=1일 때의 값)
    public abstract float CalculateInertiaCoefficient();
}

public struct AABB
{
    public Vector2 Min;
    public Vector2 Max;

    public AABB(Vector2 min, Vector2 max)
    {
        Min = min;
        Max = max;
    }

    public bool Overlaps(AABB other)
    {
        return (Max.X >= other.Min.X && Min.X <= other.Max.X) &&
               (Max.Y >= other.Min.Y && Min.Y <= other.Max.Y);
    }

    public bool IsDefault() => Min == Vector2.Zero && Max == Vector2.Zero;
}
