using Verity.Core.UI;
using Vector2 = Verity.Core.Vector2;
using Vector4 = System.Numerics.Vector4;

namespace Verity.Tests;

public sealed class UiLayoutEngineTests
{
    [Fact]
    public void Layout_HorizontalContainer_RewritesChildPosition_ButPreservesExplicitSize_WhenFitChildrenDisabled()
    {
        var root = new Panel
        {
            Layout = new UiLayoutGroup
            {
                Mode = UiLayoutMode.Horizontal,
                FitChildren = false
            }
        };

        var child = new Panel
        {
            Transform = new UiTransform
            {
                Position = new Vector2(123f, 45f),
                Size = new Vector2(150f, 60f)
            }
        };

        root.AddChild(child);
        var screen = new UIScreenAsset { Root = root, ReferenceResolution = new Vector2(1280f, 720f) };

        UiLayoutEngine.Layout(screen, screen.ReferenceResolution.X, screen.ReferenceResolution.Y);

        Assert.Equal(new Vector2(8f, 8f), child.Transform.Position);
        Assert.Equal(new Vector2(150f, 60f), child.Transform.Size);
    }

    [Fact]
    public void Layout_VerticalContainer_RewritesChildWidth_WhenFitChildrenEnabled()
    {
        var root = new Panel
        {
            Layout = new UiLayoutGroup
            {
                Mode = UiLayoutMode.Vertical,
                FitChildren = true,
                Padding = new Vector4(12f, 10f, 20f, 14f)
            }
        };

        var child = new Panel
        {
            Transform = new UiTransform
            {
                Position = new Vector2(50f, 80f),
                Size = new Vector2(90f, 44f)
            }
        };

        root.AddChild(child);
        var screen = new UIScreenAsset { Root = root, ReferenceResolution = new Vector2(1000f, 600f) };

        UiLayoutEngine.Layout(screen, screen.ReferenceResolution.X, screen.ReferenceResolution.Y);

        Assert.Equal(new Vector2(12f, 10f), child.Transform.Position);
        Assert.Equal(968f, child.Transform.Size.X);
        Assert.Equal(44f, child.Transform.Size.Y);
    }
}
