using System.Text.Json;
using Verity.Core.Serialization;

namespace Verity.Core.UI;

public static class UiSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters =
        {
            new Vector2Converter(),
            new Vector3Converter(),
            new Vector4Converter(),
            new SpriteConverter(),
            new ColorConverter()
        }
    };

    public static UIScreenAsset Load(string path)
    {
        var json = File.ReadAllText(path);
        var asset = JsonSerializer.Deserialize<UIScreenAsset>(json, Options) ?? new UIScreenAsset();
        asset.RebindTree();
        return asset;
    }

    public static UiPrefabAsset LoadPrefab(string path)
    {
        var json = File.ReadAllText(path);
        var asset = JsonSerializer.Deserialize<UiPrefabAsset>(json, Options) ?? new UiPrefabAsset();
        asset.Root.RebindTree();
        return asset;
    }

    public static UiStyleAsset LoadStyle(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<UiStyleAsset>(json, Options) ?? new UiStyleAsset();
    }

    public static void Save(string path, UIScreenAsset asset)
    {
        asset.RebindTree();
        File.WriteAllText(path, JsonSerializer.Serialize(asset, Options));
    }

    public static void SavePrefab(string path, UiPrefabAsset asset)
    {
        asset.Root.RebindTree();
        File.WriteAllText(path, JsonSerializer.Serialize(asset, Options));
    }

    public static void SaveStyle(string path, UiStyleAsset asset)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(asset, Options));
    }

    public static UiNode CloneNode(UiNode node)
    {
        string json = JsonSerializer.Serialize<UiNode>(node, Options);
        var clone = JsonSerializer.Deserialize<UiNode>(json, Options) ?? UiNodeFactory.Create(node.Kind);
        clone.RebindTree();
        return clone;
    }

    public static UIScreenAsset CreateDefaultScreen(string name)
    {
        var root = new Panel
        {
            Name = "Root",
            Transform = new UiTransform
            {
                AnchorMin = Vector2.Zero,
                AnchorMax = Vector2.One,
                Position = Vector2.Zero,
                Size = Vector2.Zero
            },
            Visual = new UiVisualStyle
            {
                BackgroundColor = Color.FromRgba(18, 20, 26, 0),
                BorderColor = Color.Clear
            }
        };

        var header = new Panel
        {
            Name = "Header",
            Transform = new UiTransform
            {
                AnchorMin = new Vector2(0.5f, 0f),
                AnchorMax = new Vector2(0.5f, 0f),
                Pivot = new Vector2(0.5f, 0f),
                Position = new Vector2(0, 24),
                Size = new Vector2(420, 72)
            },
            Visual = new UiVisualStyle
            {
                BackgroundColor = Color.FromRgba(26, 31, 44, 220),
                BorderColor = Color.FromRgba(82, 106, 153, 255),
                CornerRadius = 16f
            }
        };

        var title = new Label
        {
            Name = "Title",
            Text = name,
            Transform = new UiTransform
            {
                AnchorMin = new Vector2(0f, 0f),
                AnchorMax = new Vector2(1f, 1f),
                Size = Vector2.Zero
            },
            Visual = new UiVisualStyle
            {
                BackgroundColor = Color.Clear,
                ForegroundColor = Color.White,
                BorderColor = Color.Clear
            }
        };

        header.AddChild(title);
        root.AddChild(header);

        return new UIScreenAsset
        {
            Name = name,
            Root = root
        };
    }

    public static UiPrefabAsset CreatePrefab(string name, UiNode root)
    {
        return new UiPrefabAsset
        {
            Name = name,
            Root = CloneNode(root)
        };
    }
}
