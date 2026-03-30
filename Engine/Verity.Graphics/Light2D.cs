using System.Numerics;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Input;

namespace Verity.Graphics;

public enum Light2DType
{
    Direction = 0,
    Spot = 1,
    World = 2,
    Sprite = 3
}

public enum Light2DFalloff
{
    Soft = 0,
    Hard = 1
}

public enum Light2DMaskSource
{
    PhysicsGroup = 0,
    SortingLayer = 1
}

public enum Light2DSelectionMode
{
    Direct = 0,
    Filter = 1
}

public class Light2D : Component
{
    [SerializeField]
    public Light2DType Type { get; set; } = Light2DType.Spot;

    [SerializeField]
    public Light2DFalloff Falloff { get; set; } = Light2DFalloff.Soft;

    [SerializeField]
    public Color Color { get; set; } = Color.White;

    [SerializeField]
    public float Intensity { get; set; } = 1.0f;

    [SerializeField]
    public float Distance { get; set; } = 5.0f;

    [SerializeField]
    public float Smoothness { get; set; } = 0.25f;

    [SerializeField]
    public float Spread { get; set; } = 45.0f;

    [SerializeField]
    public bool AffectsCameraBackground { get; set; } = false;

    [SerializeField]
    public Light2DSelectionMode AffectedSortingLayerSelectionMode { get; set; } = Light2DSelectionMode.Direct;

    [SerializeField, SortingLayerMaskSelector]
    public ulong AffectedSortingLayerMask { get; set; } = ulong.MaxValue;

    [SerializeField]
    public Filter? AffectedSortingLayerFilter { get; set; }

    [SerializeField]
    public bool CastShadows { get; set; } = true;

    [SerializeField]
    public float ShadowStrength { get; set; } = 0.75f;

    [SerializeField]
    public Light2DMaskSource ShadowLayerSource { get; set; } = Light2DMaskSource.SortingLayer;

    [SerializeField]
    public Light2DSelectionMode ShadowReceiverSelectionMode { get; set; } = Light2DSelectionMode.Direct;

    [SerializeField]
    public ulong ShadowReceiverMask { get; set; } = ulong.MaxValue;

    [SerializeField]
    public Filter? ShadowReceiverFilter { get; set; }

    internal ulong ResolvedAffectedSortingLayerMask => AffectedSortingLayerMask == 0 ? ulong.MaxValue : AffectedSortingLayerMask;
    internal ulong ResolvedShadowReceiverMask => ShadowReceiverMask == 0 ? ulong.MaxValue : ShadowReceiverMask;

    internal Vector2 WorldPosition => Owner?.Transform.WorldPosition ?? Vector2.Zero;

    internal Vector2 WorldDirection
    {
        get
        {
            float rotation = (Owner?.Transform.WorldRotation ?? 0.0f) * MathF.PI / 180.0f;
            return Vector2.Normalize(new Vector2(MathF.Cos(rotation), MathF.Sin(rotation)));
        }
    }

    internal bool AffectsSortingLayer(string sortingLayerName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName;
        ulong mask = FilterRegistry.GetMask("SortingLayer", resolvedName);
        if (AffectedSortingLayerSelectionMode == Light2DSelectionMode.Filter)
            return MatchesFilter(AffectedSortingLayerFilter, typeof(Verity.Core.SortingLayer), mask);

        return (ResolvedAffectedSortingLayerMask & mask) != 0;
    }

    internal bool ReceivesShadow(string sortingLayerName, ulong physicsMask)
    {
        if (ShadowReceiverSelectionMode == Light2DSelectionMode.Filter)
        {
            Type expectedType = ShadowLayerSource == Light2DMaskSource.PhysicsGroup
                ? typeof(Verity.Core.PhysicsGroup)
                : typeof(Verity.Core.SortingLayer);
            ulong valueMask = ShadowLayerSource == Light2DMaskSource.PhysicsGroup
                ? physicsMask
                : FilterRegistry.GetMask("SortingLayer", string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName);
            return MatchesFilter(ShadowReceiverFilter, expectedType, valueMask);
        }

        return ShadowLayerSource == Light2DMaskSource.PhysicsGroup
            ? (ResolvedShadowReceiverMask & physicsMask) != 0
            : (ResolvedShadowReceiverMask & FilterRegistry.GetMask("SortingLayer", string.IsNullOrWhiteSpace(sortingLayerName) ? "Default" : sortingLayerName)) != 0;
    }

    private static bool MatchesFilter(Filter? filter, Type expectedType, ulong valueMask)
    {
        if (!TryGetCompatibleFilter(filter, expectedType, out ulong filterMask, out FilterMode mode))
            return false;

        bool hasBit = (filterMask & valueMask) != 0;
        return mode == FilterMode.Whitelist ? hasBit : !hasBit;
    }

    private static bool TryGetCompatibleFilter(Filter? filter, Type expectedType, out ulong mask, out FilterMode mode)
    {
        mask = 0;
        mode = FilterMode.Whitelist;

        if (filter == null)
            return false;

        filter.UpdateCache();
        if (!IsCompatibleSingleTypeFilter(filter, expectedType))
            return false;

        mask = filter.Mask;
        mode = filter.Mode;
        return true;
    }

    private static bool IsCompatibleSingleTypeFilter(Filter filter, Type expectedType)
    {
        if (!string.IsNullOrWhiteSpace(filter.EnumTypeName))
        {
            Type? resolvedType = FilterManager.ResolveTypeInternal(filter.EnumTypeName);
            return resolvedType == expectedType;
        }

        if (filter.MixedValues.Count == 0)
            return false;

        bool anyValue = false;
        foreach (var mixedValue in filter.MixedValues)
        {
            Type? resolvedType = FilterManager.ResolveTypeInternal(mixedValue.TypeName);
            if (resolvedType != expectedType)
                return false;
            anyValue = true;
        }

        return anyValue;
    }

    internal bool TryGetSpriteBounds(out Vector2 center, out Vector2 right, out Vector2 up, out Vector2 halfSize)
    {
        center = WorldPosition;
        right = Vector2.UnitX;
        up = Vector2.UnitY;
        halfSize = Vector2.One * 0.5f;

        var transform = Owner?.Transform;
        if (transform == null)
            return false;

        Vector2 baseSize = Vector2.One;
        Vector2 pivot = new(0.5f, 0.5f);

        if (Owner?.GetComponent<SpriteRenderer>() is SpriteRenderer spriteRenderer)
        {
            baseSize = new Vector2(MathF.Abs(spriteRenderer.Size.X), MathF.Abs(spriteRenderer.Size.Y));
            if (!spriteRenderer.UseSpritePivot)
                pivot = spriteRenderer.Pivot;
        }
        else if (Owner?.GetComponent<IHasSize>() is IHasSize hasSize)
        {
            baseSize = new Vector2(MathF.Abs(hasSize.Size.X), MathF.Abs(hasSize.Size.Y));
        }

        Vector2 worldScale = transform.WorldScale;
        Vector2 worldSize = new(MathF.Abs(baseSize.X * worldScale.X), MathF.Abs(baseSize.Y * worldScale.Y));
        halfSize = new Vector2(MathF.Max(0.0001f, worldSize.X * 0.5f), MathF.Max(0.0001f, worldSize.Y * 0.5f));

        float rotation = transform.WorldRotation * MathF.PI / 180.0f;
        right = new Vector2(MathF.Cos(rotation), MathF.Sin(rotation));
        up = new Vector2(-MathF.Sin(rotation), MathF.Cos(rotation));

        Vector2 localCenter = new((0.5f - pivot.X) * worldSize.X, (0.5f - pivot.Y) * worldSize.Y);
        center = transform.WorldPosition + right * localCenter.X + up * localCenter.Y;
        return true;
    }
}
