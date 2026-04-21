using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Tests;

public sealed class NineSliceRendererTests
{
    [Fact]
    public void CalculateRegions_SplitsSpriteIntoNineSlices()
    {
        var settings = new SpriteImportSettings
        {
            NineSliceLeft = 10,
            NineSliceRight = 10,
            NineSliceTop = 10,
            NineSliceBottom = 10
        };

        var regions = NineSliceRenderer.CalculateRegions(100, 100, 100, 100, settings);

        Assert.Collection(
            regions,
            region => AssertRegion(region, NineSliceRegionPosition.TopLeft, false, 0, 0, 10, 10, 0, 0, 10, 10),
            region => AssertRegion(region, NineSliceRegionPosition.Top, false, 10, 0, 80, 10, 10, 0, 80, 10),
            region => AssertRegion(region, NineSliceRegionPosition.TopRight, false, 90, 0, 10, 10, 90, 0, 10, 10),
            region => AssertRegion(region, NineSliceRegionPosition.Left, false, 0, 10, 10, 80, 0, 10, 10, 80),
            region => AssertRegion(region, NineSliceRegionPosition.Center, true, 10, 10, 80, 80, 10, 10, 80, 80),
            region => AssertRegion(region, NineSliceRegionPosition.Right, false, 90, 10, 10, 80, 90, 10, 10, 80),
            region => AssertRegion(region, NineSliceRegionPosition.BottomLeft, false, 0, 90, 10, 10, 0, 90, 10, 10),
            region => AssertRegion(region, NineSliceRegionPosition.Bottom, false, 10, 90, 80, 10, 10, 90, 80, 10),
            region => AssertRegion(region, NineSliceRegionPosition.BottomRight, false, 90, 90, 10, 10, 90, 90, 10, 10));
    }

    private static void AssertRegion(
        NineSliceRegion actual,
        NineSliceRegionPosition expectedPosition,
        bool expectedTile,
        int expectedSourceX,
        int expectedSourceY,
        int expectedSourceWidth,
        int expectedSourceHeight,
        int expectedDestinationX,
        int expectedDestinationY,
        int expectedDestinationWidth,
        int expectedDestinationHeight)
    {
        Assert.Equal(expectedPosition, actual.Position);
        Assert.Equal(expectedTile, actual.Tile);
        Assert.Equal(new NineSliceRect(expectedSourceX, expectedSourceY, expectedSourceWidth, expectedSourceHeight), actual.Source);
        Assert.Equal(new NineSliceRect(expectedDestinationX, expectedDestinationY, expectedDestinationWidth, expectedDestinationHeight), actual.Destination);
    }
}
