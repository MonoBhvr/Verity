using System.Numerics;
using Verity.Core;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Graphics;

public enum NineSliceRegionPosition
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight
}

public readonly record struct NineSliceRect(int X, int Y, int Width, int Height);

public readonly record struct NineSliceRegion(
    NineSliceRegionPosition Position,
    NineSliceRect Source,
    NineSliceRect Destination,
    bool Tile);

public class NineSliceRenderer : Component, IHasSize
{
    private Sprite _sprite;

    [SerializeField]
    public Sprite Sprite
    {
        get => _sprite;
        set => _sprite = value;
    }

    [SerializeField]
    public Vector2 Size { get; set; } = Vector2.One;

    [SerializeField]
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    [SerializeField]
    public bool UseSpritePivot { get; set; } = true;

    [SerializeField, SortingLayerSelector]
    public string SortingLayerName { get; set; } = "Default";

    [SerializeField]
    public int OrderInLayer { get; set; }

    internal int ResolvedLayerIndex => SortingLayer.GetLayerIndex(SortingLayerName);

    public static NineSliceRegion[] CalculateRegions(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, SpriteImportSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        int clampedSourceWidth = Math.Max(1, sourceWidth);
        int clampedSourceHeight = Math.Max(1, sourceHeight);
        int clampedTargetWidth = Math.Max(1, targetWidth);
        int clampedTargetHeight = Math.Max(1, targetHeight);

        int sourceLeft = Math.Clamp(settings.NineSliceLeft, 0, clampedSourceWidth);
        int sourceRight = Math.Clamp(settings.NineSliceRight, 0, clampedSourceWidth - sourceLeft);
        int sourceTop = Math.Clamp(settings.NineSliceTop, 0, clampedSourceHeight);
        int sourceBottom = Math.Clamp(settings.NineSliceBottom, 0, clampedSourceHeight - sourceTop);

        int destinationLeft = Math.Min(sourceLeft, clampedTargetWidth);
        int destinationRight = Math.Min(sourceRight, clampedTargetWidth - destinationLeft);
        int destinationTop = Math.Min(sourceTop, clampedTargetHeight);
        int destinationBottom = Math.Min(sourceBottom, clampedTargetHeight - destinationTop);

        int sourceCenterWidth = Math.Max(0, clampedSourceWidth - sourceLeft - sourceRight);
        int sourceCenterHeight = Math.Max(0, clampedSourceHeight - sourceTop - sourceBottom);
        int destinationCenterWidth = Math.Max(0, clampedTargetWidth - destinationLeft - destinationRight);
        int destinationCenterHeight = Math.Max(0, clampedTargetHeight - destinationTop - destinationBottom);

        return
        [
            CreateRegion(NineSliceRegionPosition.TopLeft, false, 0, 0, sourceLeft, sourceTop, 0, 0, destinationLeft, destinationTop),
            CreateRegion(NineSliceRegionPosition.Top, false, sourceLeft, 0, sourceCenterWidth, sourceTop, destinationLeft, 0, destinationCenterWidth, destinationTop),
            CreateRegion(NineSliceRegionPosition.TopRight, false, clampedSourceWidth - sourceRight, 0, sourceRight, sourceTop, clampedTargetWidth - destinationRight, 0, destinationRight, destinationTop),
            CreateRegion(NineSliceRegionPosition.Left, false, 0, sourceTop, sourceLeft, sourceCenterHeight, 0, destinationTop, destinationLeft, destinationCenterHeight),
            CreateRegion(NineSliceRegionPosition.Center, true, sourceLeft, sourceTop, sourceCenterWidth, sourceCenterHeight, destinationLeft, destinationTop, destinationCenterWidth, destinationCenterHeight),
            CreateRegion(NineSliceRegionPosition.Right, false, clampedSourceWidth - sourceRight, sourceTop, sourceRight, sourceCenterHeight, clampedTargetWidth - destinationRight, destinationTop, destinationRight, destinationCenterHeight),
            CreateRegion(NineSliceRegionPosition.BottomLeft, false, 0, clampedSourceHeight - sourceBottom, sourceLeft, sourceBottom, 0, clampedTargetHeight - destinationBottom, destinationLeft, destinationBottom),
            CreateRegion(NineSliceRegionPosition.Bottom, false, sourceLeft, clampedSourceHeight - sourceBottom, sourceCenterWidth, sourceBottom, destinationLeft, clampedTargetHeight - destinationBottom, destinationCenterWidth, destinationBottom),
            CreateRegion(NineSliceRegionPosition.BottomRight, false, clampedSourceWidth - sourceRight, clampedSourceHeight - sourceBottom, sourceRight, sourceBottom, clampedTargetWidth - destinationRight, clampedTargetHeight - destinationBottom, destinationRight, destinationBottom)
        ];
    }

    private static NineSliceRegion CreateRegion(
        NineSliceRegionPosition position,
        bool tile,
        int sourceX,
        int sourceY,
        int sourceWidth,
        int sourceHeight,
        int destinationX,
        int destinationY,
        int destinationWidth,
        int destinationHeight)
    {
        return new NineSliceRegion(
            position,
            new NineSliceRect(sourceX, sourceY, sourceWidth, sourceHeight),
            new NineSliceRect(destinationX, destinationY, destinationWidth, destinationHeight),
            tile);
    }
}
