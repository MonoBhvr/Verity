using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core.Physics;

public abstract class PhysicalShape : Component
{
    [SerializeField]
    public bool IsSensor { get; set; } = false;

    [SerializeField]
    public Vector2 Offset { get; set; } = Vector2.Zero;

    [SerializeField]
    public string GroupName { get; set; } = "Default";

    public ulong GroupMask => Verity.Input.Filter.Get(GroupName)?.Mask ?? 1UL;

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
}
