using Verity.Core.UI;

namespace Verity.Tests;

public sealed class DynamicAreaTests
{
    [Fact]
    public void Update_PreservesExistingChildren_WhenItemsAppend()
    {
        var items = new List<Item>
        {
            new("Sword"),
            new("Shield"),
            new("Potion")
        };
        var canvas = CreateCanvas();
        canvas.Set("Items", items);
        canvas.Update(1920, 1080);

        var area = Assert.IsType<DynamicArea>(canvas.Query("Inventory"));
        var first = Assert.IsType<Label>(area.Children[0]);
        var second = Assert.IsType<Label>(area.Children[1]);
        var third = Assert.IsType<Label>(area.Children[2]);

        items.Add(new Item("Bow"));
        canvas.Update(1920, 1080);

        Assert.Same(first, area.Children[0]);
        Assert.Same(second, area.Children[1]);
        Assert.Same(third, area.Children[2]);

        Assert.Collection(
            area.Children,
            child => Assert.Equal("Sword", Assert.IsType<Label>(child).Text),
            child => Assert.Equal("Shield", Assert.IsType<Label>(child).Text),
            child => Assert.Equal("Potion", Assert.IsType<Label>(child).Text),
            child => Assert.Equal("Bow", Assert.IsType<Label>(child).Text));
    }

    [Fact]
    public void Update_PreservesPrefixAndSuffix_WhenMiddleItemChanges()
    {
        var canvas = CreateCanvas();
        var items = new List<Item>
        {
            new("Sword"),
            new("Shield"),
            new("Potion")
        };

        canvas.Set("Items", items);
        canvas.Update(1920, 1080);

        var area = Assert.IsType<DynamicArea>(canvas.Query("Inventory"));
        var first = Assert.IsType<Label>(area.Children[0]);
        var second = Assert.IsType<Label>(area.Children[1]);
        var third = Assert.IsType<Label>(area.Children[2]);

        items[1] = new Item("Armor");
        canvas.Update(1920, 1080);

        Assert.Same(first, area.Children[0]);
        Assert.NotSame(second, area.Children[1]);
        Assert.Same(third, area.Children[2]);

        Assert.Collection(
            area.Children,
            child => Assert.Equal("Sword", Assert.IsType<Label>(child).Text),
            child => Assert.Equal("Armor", Assert.IsType<Label>(child).Text),
            child => Assert.Equal("Potion", Assert.IsType<Label>(child).Text));
    }

    private static Canvas CreateCanvas()
    {
        var root = new Panel
        {
            Name = "Root"
        };

        root.AddChild(new DynamicArea
        {
            Name = "Inventory",
            ItemsSource = "Items",
            ItemTemplate = new Label
            {
                Name = "Entry",
                Bindings =
                [
                    new UiBinding
                    {
                        Path = "Item.Name",
                        TargetProperty = "Text"
                    }
                ]
            }
        });

        return new Canvas(new UIScreenAsset { Root = root });
    }

    private sealed record Item(string Name);
}
