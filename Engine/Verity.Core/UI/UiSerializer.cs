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
        SanitizeScreen(asset);
        asset.RebindTree();
        return asset;
    }

    public static UiPrefabAsset LoadPrefab(string path)
    {
        var json = File.ReadAllText(path);
        var asset = JsonSerializer.Deserialize<UiPrefabAsset>(json, Options) ?? new UiPrefabAsset();
        SanitizePrefab(asset);
        asset.Root.RebindTree();
        return asset;
    }

    public static UiStyleAsset LoadStyle(string path)
    {
        var json = File.ReadAllText(path);
        var asset = JsonSerializer.Deserialize<UiStyleAsset>(json, Options) ?? new UiStyleAsset();
        SanitizeStyle(asset);
        return asset;
    }

    public static void Save(string path, UIScreenAsset asset)
    {
        SanitizeScreen(asset);
        asset.RebindTree();
        File.WriteAllText(path, JsonSerializer.Serialize(asset, Options));
    }

    public static void SavePrefab(string path, UiPrefabAsset asset)
    {
        SanitizePrefab(asset);
        asset.Root.RebindTree();
        File.WriteAllText(path, JsonSerializer.Serialize(asset, Options));
    }

    public static void SaveStyle(string path, UiStyleAsset asset)
    {
        SanitizeStyle(asset);
        File.WriteAllText(path, JsonSerializer.Serialize(asset, Options));
    }

    public static UiNode CloneNode(UiNode node)
    {
        string json = JsonSerializer.Serialize<UiNode>(node, Options);
        var clone = JsonSerializer.Deserialize<UiNode>(json, Options) ?? UiNodeFactory.Create(node.Kind);
        clone.RebindTree();
        return clone;
    }

    public static UIScreenAsset CloneScreen(UIScreenAsset screen)
    {
        string json = JsonSerializer.Serialize(screen, Options);
        var clone = JsonSerializer.Deserialize<UIScreenAsset>(json, Options) ?? new UIScreenAsset();
        SanitizeScreen(clone);
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

        var frame = new Panel
        {
            Name = "Frame",
            Transform = new UiTransform
            {
                AnchorMin = new Vector2(0.5f, 0.5f),
                AnchorMax = new Vector2(0.5f, 0.5f),
                Pivot = new Vector2(0.5f, 0.5f),
                Position = Vector2.Zero,
                Size = new Vector2(760, 460)
            },
            Visual = new UiVisualStyle
            {
                BackgroundColor = Color.FromRgba(24, 29, 40, 235),
                BorderColor = Color.FromRgba(78, 102, 148, 255),
                CornerRadius = 18f
            }
        };

        var header = new Panel
        {
            Name = "Header",
            Transform = new UiTransform
            {
                AnchorMin = new Vector2(0f, 0f),
                AnchorMax = new Vector2(1f, 0f),
                Pivot = new Vector2(0.5f, 0f),
                Position = Vector2.Zero,
                Size = new Vector2(0, 92)
            },
            Visual = new UiVisualStyle
            {
                BackgroundColor = Color.FromRgba(31, 38, 56, 245),
                BorderColor = Color.Clear,
                CornerRadius = 18f
            }
        };

        var body = new Panel
        {
            Name = "Body",
            Transform = new UiTransform
            {
                AnchorMin = new Vector2(0f, 0f),
                AnchorMax = new Vector2(1f, 1f),
                Position = new Vector2(24, 116),
                Size = new Vector2(-48, -140)
            },
            Visual = new UiVisualStyle
            {
                BackgroundColor = Color.FromRgba(17, 21, 30, 140),
                BorderColor = Color.FromRgba(58, 72, 102, 180),
                CornerRadius = 14f
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
        frame.AddChild(header);
        frame.AddChild(body);
        root.AddChild(frame);

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

    private static void SanitizeScreen(UIScreenAsset asset)
    {
        asset.Id = string.IsNullOrWhiteSpace(asset.Id) ? Guid.NewGuid().ToString("N") : asset.Id;
        asset.Name ??= "NewScreen";
        asset.UiScriptType ??= string.Empty;
        asset.Variables ??= [];
        asset.Root ??= new Panel
        {
            Name = "Root",
            Transform = new UiTransform { AnchorMin = Vector2.Zero, AnchorMax = Vector2.One, Size = Vector2.Zero }
        };

        SanitizeNode(asset.Root);
    }

    private static void SanitizePrefab(UiPrefabAsset asset)
    {
        asset.Name ??= "NewUiPrefab";
        asset.Root ??= new Panel { Name = "PrefabRoot" };
        SanitizeNode(asset.Root);
    }

    private static void SanitizeStyle(UiStyleAsset asset)
    {
        asset.Name ??= "NewUiStyle";
        asset.Colors ??= new Dictionary<string, Color>();
        asset.Numbers ??= new Dictionary<string, float>();
        asset.Strings ??= new Dictionary<string, string>();
        asset.States ??= [];

        for (int i = 0; i < asset.States.Count; i++)
        {
            asset.States[i] ??= new UiStateStyleOverride();
            asset.States[i].Visual ??= new UiVisualStyle();
        }
    }

    private static void SanitizeNode(UiNode node)
    {
        node.Id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id;
        node.Name ??= node.Kind.ToString();
        node.Tag ??= string.Empty;
        node.Transform ??= new UiTransform();
        node.Visual ??= new UiVisualStyle();
        node.Navigation ??= new UiNavigation();
        node.Animation ??= new UiAnimationState();
        node.Bindings ??= [];
        node.Events ??= [];
        node.Children ??= [];

        foreach (var binding in node.Bindings)
        {
            if (binding == null)
                continue;

            binding.Path ??= string.Empty;
            binding.TargetProperty ??= string.Empty;
        }

        foreach (var action in node.Events)
        {
            if (action == null)
                continue;

            action.Target ??= string.Empty;
            action.Method ??= string.Empty;
        }

        if (node is UiContainer container)
            container.Layout ??= new UiLayoutGroup();

        if (node is DynamicArea dynamicArea)
        {
            dynamicArea.ItemsSource ??= string.Empty;
            dynamicArea.ItemTemplate ??= new Panel { Name = "ItemTemplate" };
            SanitizeNode(dynamicArea.ItemTemplate);
        }

        switch (node)
        {
            case TextNode text:
                text.Text ??= string.Empty;
                text.LocalizationKey ??= string.Empty;
                break;
            case Button button:
                button.Text ??= string.Empty;
                break;
            case Toggle toggle:
                toggle.Group ??= string.Empty;
                toggle.Text ??= string.Empty;
                break;
            case Dropdown dropdown:
                dropdown.Options ??= [];
                for (int i = 0; i < dropdown.Options.Count; i++)
                    dropdown.Options[i] ??= string.Empty;
                break;
            case InputField inputField:
                inputField.Value ??= string.Empty;
                inputField.Placeholder ??= string.Empty;
                break;
            case TextArea textArea:
                textArea.Value ??= string.Empty;
                textArea.Placeholder ??= string.Empty;
                break;
            case Window window:
                window.Title ??= string.Empty;
                break;
            case Tabs tabs:
                tabs.Titles ??= [];
                for (int i = 0; i < tabs.Titles.Count; i++)
                    tabs.Titles[i] ??= string.Empty;
                break;
            case Tooltip tooltip:
                tooltip.Text ??= string.Empty;
                break;
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            if (node.Children[i] == null)
                continue;

            SanitizeNode(node.Children[i]);
        }
    }
}
