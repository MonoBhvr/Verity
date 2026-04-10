using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;
using Verity.Core.Serialization;

namespace Verity.Core.UI;

[Flags]
public enum UiStateFlags
{
    None = 0,
    Hover = 1 << 0,
    Pressed = 1 << 1,
    Disabled = 1 << 2,
    Selected = 1 << 3,
    Focused = 1 << 4,
    Expanded = 1 << 5,
    Checked = 1 << 6
}

public enum UiRenderMode
{
    ScreenSpaceOverlay,
    ScreenSpaceCamera,
    WorldSpace
}

public enum UiNodeKind
{
    Container,
    Panel,
    Label,
    RichText,
    Image,
    Button,
    IconButton,
    Toggle,
    ToggleGroup,
    Dropdown,
    InputField,
    TextArea,
    Slider,
    ProgressBar,
    Scrollbar,
    ScrollView,
    ListView,
    GridView,
    Window,
    Modal,
    Tabs,
    Tooltip,
    Spacer,
    DynamicArea
}

public enum UiLayoutMode
{
    Free = 0,
    None = 0,
    Horizontal = 1,
    HorizontalStack = 1,
    Vertical = 2,
    VerticalStack = 2,
    Grid = 3,
    Wrap = 4,
    Circle = 5,
    ScrollContent = 6
}

public enum UiNavigationMode
{
    Automatic,
    Explicit
}

public enum UiBindingMode
{
    OneWay,
    TwoWay
}

public enum UiEventType
{
    PointerEnter,
    PointerExit,
    PointerDown,
    PointerUp,
    Click,
    DoubleClick,
    DragBegin,
    Drag,
    DragEnd,
    Scroll,
    ValueChanged,
    Submit,
    Cancel,
    FocusChanged
}

public readonly record struct UiRect(float X, float Y, float Width, float Height)
{
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(Vector2 point) =>
        point.X >= X && point.X <= Right &&
        point.Y >= Y && point.Y <= Bottom;
}

public sealed class UiEvent
{
    public UiEventType Type { get; init; }
    public UiNode? Node { get; init; }

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Position { get; init; }

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Delta { get; init; }

    public float ScrollDelta { get; init; }
    public object? Value { get; init; }
}

public sealed class UiTransform
{
    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 AnchorMin { get; set; } = Vector2.Zero;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 AnchorMax { get; set; } = Vector2.Zero;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Position { get; set; } = Vector2.Zero;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Size { get; set; } = new(160, 40);

    [JsonConverter(typeof(Vector4Converter))]
    public System.Numerics.Vector4 Margin { get; set; } = System.Numerics.Vector4.Zero;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 MinSize { get; set; } = Vector2.Zero;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 MaxSize { get; set; } = new(float.MaxValue, float.MaxValue);

    public float Rotation { get; set; }
    public float Scale { get; set; } = 1f;
    public int ZOrder { get; set; }
}

public sealed class UiVisualStyle
{
    [JsonConverter(typeof(ColorConverter))]
    public Color BackgroundColor { get; set; } = Color.FromRgba(36, 40, 48, 220);

    [JsonConverter(typeof(ColorConverter))]
    public Color ForegroundColor { get; set; } = Color.White;

    [JsonConverter(typeof(ColorConverter))]
    public Color BorderColor { get; set; } = Color.FromRgba(90, 98, 120, 255);

    [JsonConverter(typeof(ColorConverter))]
    public Color HoverColor { get; set; } = Color.FromRgba(56, 76, 112, 255);

    [JsonConverter(typeof(ColorConverter))]
    public Color PressedColor { get; set; } = Color.FromRgba(44, 58, 86, 255);

    [JsonConverter(typeof(ColorConverter))]
    public Color DisabledColor { get; set; } = Color.FromRgba(60, 60, 60, 180);

    public float BorderThickness { get; set; } = 1f;
    public float CornerRadius { get; set; } = 8f;
    public float FontSize { get; set; } = 16f;

    [JsonConverter(typeof(Vector4Converter))]
    public System.Numerics.Vector4 Padding { get; set; } = new(8, 8, 8, 8);

    public string FontPath { get; set; } = string.Empty;
    public string FontFamily { get; set; } = string.Empty;
    public string BackgroundToken { get; set; } = string.Empty;
    public string ForegroundToken { get; set; } = string.Empty;
}

public sealed class UiLayoutGroup
{
    public UiLayoutMode Mode { get; set; } = UiLayoutMode.Free;
    public int Columns { get; set; } = 2;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 Spacing { get; set; } = new(8, 8);

    [JsonConverter(typeof(Vector4Converter))]
    public System.Numerics.Vector4 Padding { get; set; } = new(8, 8, 8, 8);

    public bool FitChildren { get; set; } = true;
    public float CircleRadius { get; set; } = 120f;
    public float CircleStartAngle { get; set; } = -90f;
    public bool CircleClockwise { get; set; } = true;
}

public sealed class UiNavigation
{
    public UiNavigationMode Mode { get; set; } = UiNavigationMode.Automatic;
    public string Up { get; set; } = string.Empty;
    public string Down { get; set; } = string.Empty;
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;
}

public sealed class UiAnimationState
{
    public string Name { get; set; } = "Default";
    public float Duration { get; set; } = 0.12f;
}

public sealed class UiBinding
{
    public string Path { get; set; } = string.Empty;
    public string TargetProperty { get; set; } = string.Empty;
    public UiBindingMode Mode { get; set; } = UiBindingMode.OneWay;
}

public sealed class UiEventAction
{
    public UiEventType Trigger { get; set; } = UiEventType.Click;
    public string Target { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
}

public sealed class UiScreenVariableDefinition
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = "object";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(UiContainer), "container")]
[JsonDerivedType(typeof(Panel), "panel")]
[JsonDerivedType(typeof(Label), "label")]
[JsonDerivedType(typeof(RichText), "richtext")]
[JsonDerivedType(typeof(Image), "image")]
[JsonDerivedType(typeof(Button), "button")]
[JsonDerivedType(typeof(IconButton), "iconbutton")]
[JsonDerivedType(typeof(Toggle), "toggle")]
[JsonDerivedType(typeof(ToggleGroup), "togglegroup")]
[JsonDerivedType(typeof(Dropdown), "dropdown")]
[JsonDerivedType(typeof(InputField), "inputfield")]
[JsonDerivedType(typeof(TextArea), "textarea")]
[JsonDerivedType(typeof(Slider), "slider")]
[JsonDerivedType(typeof(ProgressBar), "progressbar")]
[JsonDerivedType(typeof(Scrollbar), "scrollbar")]
[JsonDerivedType(typeof(ScrollView), "scrollview")]
[JsonDerivedType(typeof(ListView), "listview")]
[JsonDerivedType(typeof(GridView), "gridview")]
[JsonDerivedType(typeof(Window), "window")]
[JsonDerivedType(typeof(Modal), "modal")]
[JsonDerivedType(typeof(Tabs), "tabs")]
[JsonDerivedType(typeof(Tooltip), "tooltip")]
[JsonDerivedType(typeof(Spacer), "spacer")]
[JsonDerivedType(typeof(DynamicArea), "dynamicarea")]
public abstract class UiNode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Element";
    public string Tag { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public UiNodeKind Kind { get; protected init; }
    public UiTransform Transform { get; set; } = new();
    public UiVisualStyle Visual { get; set; } = new();
    public UiNavigation Navigation { get; set; } = new();
    public UiAnimationState Animation { get; set; } = new();
    public List<UiBinding> Bindings { get; set; } = [];
    public List<UiEventAction> Events { get; set; } = [];
    public List<UiNode> Children { get; set; } = [];
    public bool Interactable { get; set; }
    public bool Visible { get; set; } = true;

    [JsonIgnore]
    public UiNode? Parent { get; private set; }

    [JsonIgnore]
    public UiRect LayoutRect { get; set; }

    [JsonIgnore]
    public UiStateFlags RuntimeState { get; set; }

    [JsonIgnore]
    internal object? BindingItem { get; set; }

    [JsonIgnore]
    internal bool IsRuntimeGenerated { get; set; }

    public event Action<UiEvent>? OnEvent;
    public event Action<UiEvent>? OnClick;
    public event Action<UiEvent>? OnValueChanged;
    public event Action<UiEvent>? OnSubmit;

    public void AddChild(UiNode child)
    {
        if (child.Parent == this)
            return;

        child.Parent?.RemoveChild(child);
        child.Parent = this;
        Children.Add(child);
    }

    public void RemoveChild(UiNode child)
    {
        if (Children.Remove(child))
            child.Parent = null;
    }

    public IEnumerable<UiNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var nested in child.DescendantsAndSelf())
                yield return nested;
        }
    }

    public T? Query<T>(string nameOrId) where T : UiNode
    {
        foreach (var node in DescendantsAndSelf())
        {
            if (node is T typed &&
                (string.Equals(node.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(node.Id, nameOrId, StringComparison.OrdinalIgnoreCase)))
            {
                return typed;
            }
        }

        return null;
    }

    public UiNode? Query(string nameOrId)
    {
        foreach (var node in DescendantsAndSelf())
        {
            if (string.Equals(node.Name, nameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(node.Id, nameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }
        }

        return null;
    }

    public void RebindTree()
    {
        foreach (var child in Children)
        {
            child.Parent = this;
            child.RebindTree();
        }
    }

    internal void SetBindingItemRecursive(object? item)
    {
        BindingItem = item;
        foreach (var child in Children)
            child.SetBindingItemRecursive(item);
    }

    internal void RaiseEvent(UiEvent evt)
    {
        OnEvent?.Invoke(evt);
        if (evt.Type == UiEventType.Click) OnClick?.Invoke(evt);
        if (evt.Type == UiEventType.ValueChanged) OnValueChanged?.Invoke(evt);
        if (evt.Type == UiEventType.Submit) OnSubmit?.Invoke(evt);
    }
}

public class UiContainer : UiNode
{
    public UiContainer()
    {
        Kind = UiNodeKind.Container;
    }

    public UiLayoutGroup Layout { get; set; } = new();
    public bool ClipChildren { get; set; }
}

public sealed class Panel : UiContainer
{
    public Panel()
    {
        Kind = UiNodeKind.Panel;
    }
}

public sealed class DynamicArea : UiContainer
{
    public DynamicArea()
    {
        Kind = UiNodeKind.DynamicArea;
        Name = "DynamicArea";
    }

    public string ItemsSource { get; set; } = string.Empty;
    public UiNode? ItemTemplate { get; set; }
}

public class TextNode : UiNode
{
    public string Text { get; set; } = "Text";
    public float FontSize { get; set; } = 18f;
    public bool AutoSize { get; set; }
    public bool WordWrap { get; set; } = true;
    public string LocalizationKey { get; set; } = string.Empty;
}

public sealed class Label : TextNode
{
    public Label()
    {
        Kind = UiNodeKind.Label;
    }
}

public sealed class RichText : TextNode
{
    public RichText()
    {
        Kind = UiNodeKind.RichText;
    }
}

public class VisualNode : UiNode
{
    [JsonConverter(typeof(SpriteConverter))]
    public Sprite Sprite { get; set; }

    public bool PreserveAspect { get; set; } = true;
}

public sealed class Image : VisualNode
{
    public Image()
    {
        Kind = UiNodeKind.Image;
    }
}

public class ClickableNode : UiContainer
{
    protected ClickableNode()
    {
        Interactable = true;
    }
}

public sealed class Button : ClickableNode
{
    public Button()
    {
        Kind = UiNodeKind.Button;
        Name = "Button";
        Text = "Button";
    }

    public string Text { get; set; } = "Button";
}

public sealed class IconButton : ClickableNode
{
    public IconButton()
    {
        Kind = UiNodeKind.IconButton;
        Name = "IconButton";
    }

    [JsonConverter(typeof(SpriteConverter))]
    public Sprite Icon { get; set; }
}

public sealed class Toggle : ClickableNode
{
    public Toggle()
    {
        Kind = UiNodeKind.Toggle;
        Name = "Toggle";
        Text = "Toggle";
    }

    public bool IsChecked { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Text { get; set; } = "Toggle";
}

public sealed class ToggleGroup : UiContainer
{
    public ToggleGroup()
    {
        Kind = UiNodeKind.ToggleGroup;
        Name = "ToggleGroup";
    }

    public bool AllowSwitchOff { get; set; } = true;
}

public sealed class Dropdown : ClickableNode
{
    public Dropdown()
    {
        Kind = UiNodeKind.Dropdown;
        Name = "Dropdown";
        Options = ["Option A", "Option B", "Option C"];
    }

    public List<string> Options { get; set; } = [];
    public int SelectedIndex { get; set; }
    public bool Expanded { get; set; }
}

public sealed class InputField : ClickableNode
{
    public InputField()
    {
        Kind = UiNodeKind.InputField;
        Name = "InputField";
    }

    public string Value { get; set; } = string.Empty;
    public string Placeholder { get; set; } = "Type here...";
}

public sealed class TextArea : ClickableNode
{
    public TextArea()
    {
        Kind = UiNodeKind.TextArea;
        Name = "TextArea";
    }

    public string Value { get; set; } = string.Empty;
    public string Placeholder { get; set; } = "Type here...";
}

public sealed class Slider : ClickableNode
{
    public Slider()
    {
        Kind = UiNodeKind.Slider;
        Name = "Slider";
    }

    public float Min { get; set; }
    public float Max { get; set; } = 1f;
    public float Value { get; set; } = 0.5f;
}

public sealed class ProgressBar : UiNode
{
    public ProgressBar()
    {
        Kind = UiNodeKind.ProgressBar;
        Name = "ProgressBar";
    }

    public float Min { get; set; }
    public float Max { get; set; } = 1f;
    public float Value { get; set; } = 0.5f;
}

public sealed class Scrollbar : ClickableNode
{
    public Scrollbar()
    {
        Kind = UiNodeKind.Scrollbar;
        Name = "Scrollbar";
    }

    public float Value { get; set; }
}

public sealed class ScrollView : UiContainer
{
    public ScrollView()
    {
        Kind = UiNodeKind.ScrollView;
        Name = "ScrollView";
    }

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 ScrollOffset { get; set; }

    public bool Horizontal { get; set; }
    public bool Vertical { get; set; } = true;
}

public sealed class ListView : UiContainer
{
    public ListView()
    {
        Kind = UiNodeKind.ListView;
        Name = "ListView";
    }

    public bool Virtualized { get; set; } = true;
    public int ItemCount { get; set; } = 10;
}

public sealed class GridView : UiContainer
{
    public GridView()
    {
        Kind = UiNodeKind.GridView;
        Name = "GridView";
    }

    public bool Virtualized { get; set; } = true;
    public int ItemCount { get; set; } = 12;
    public int Columns { get; set; } = 4;
}

public sealed class Window : UiContainer
{
    public Window()
    {
        Kind = UiNodeKind.Window;
        Name = "Window";
        Title = "Window";
    }

    public string Title { get; set; } = "Window";
}

public sealed class Modal : UiContainer
{
    public Modal()
    {
        Kind = UiNodeKind.Modal;
        Name = "Modal";
    }

    public bool DismissOnBackgroundClick { get; set; } = true;
}

public sealed class Tabs : UiContainer
{
    public Tabs()
    {
        Kind = UiNodeKind.Tabs;
        Name = "Tabs";
    }

    public int SelectedIndex { get; set; }
    public List<string> Titles { get; set; } = ["Tab 1", "Tab 2"];
}

public sealed class Tooltip : UiNode
{
    public Tooltip()
    {
        Kind = UiNodeKind.Tooltip;
        Name = "Tooltip";
        Text = "Tooltip";
    }

    public string Text { get; set; } = "Tooltip";
}

public sealed class Spacer : UiNode
{
    public Spacer()
    {
        Kind = UiNodeKind.Spacer;
        Name = "Spacer";
    }
}

public sealed class UiStateStyleOverride
{
    public UiStateFlags State { get; set; }
    public UiVisualStyle Visual { get; set; } = new();
}

public sealed class UiStyleAsset
{
    public string Name { get; set; } = "NewUiStyle";
    public Dictionary<string, Color> Colors { get; set; } = new();
    public Dictionary<string, float> Numbers { get; set; } = new();
    public Dictionary<string, string> Strings { get; set; } = new();
    public List<UiStateStyleOverride> States { get; set; } = [];
}

public sealed class UiPrefabAsset
{
    public string Name { get; set; } = "NewUiPrefab";
    public UiNode Root { get; set; } = new Panel { Name = "PrefabRoot" };
}

public sealed class UIScreenAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "NewScreen";
    public UiRenderMode RenderMode { get; set; } = UiRenderMode.ScreenSpaceOverlay;

    [JsonConverter(typeof(Vector2Converter))]
    public Vector2 ReferenceResolution { get; set; } = new(1920, 1080);

    public float MatchWidthOrHeight { get; set; } = 0.5f;
    public int SortingOrder { get; set; }
    public string UiScriptType { get; set; } = string.Empty;
    public List<UiScreenVariableDefinition> Variables { get; set; } = [];
    public UiNode Root { get; set; } = new Panel
    {
        Name = "Root",
        Transform = new UiTransform { AnchorMin = Vector2.Zero, AnchorMax = Vector2.One, Size = Vector2.Zero }
    };

    public void RebindTree()
    {
        Root.RebindTree();
    }
}

public static class UiNodeFactory
{
    public static UiNode Create(UiNodeKind kind) => kind switch
    {
        UiNodeKind.Container => new UiContainer { Name = "Container" },
        UiNodeKind.Panel => new Panel { Name = "Panel" },
        UiNodeKind.Label => new Label { Name = "Label", Text = "Label" },
        UiNodeKind.RichText => new RichText { Name = "RichText", Text = "<b>Rich Text</b>" },
        UiNodeKind.Image => new Image { Name = "Image" },
        UiNodeKind.Button => new Button(),
        UiNodeKind.IconButton => new IconButton(),
        UiNodeKind.Toggle => new Toggle(),
        UiNodeKind.ToggleGroup => new ToggleGroup(),
        UiNodeKind.Dropdown => new Dropdown(),
        UiNodeKind.InputField => new InputField(),
        UiNodeKind.TextArea => new TextArea(),
        UiNodeKind.Slider => new Slider(),
        UiNodeKind.ProgressBar => new ProgressBar(),
        UiNodeKind.Scrollbar => new Scrollbar(),
        UiNodeKind.ScrollView => new ScrollView(),
        UiNodeKind.ListView => new ListView(),
        UiNodeKind.GridView => new GridView(),
        UiNodeKind.Window => new Window(),
        UiNodeKind.Modal => new Modal(),
        UiNodeKind.Tabs => new Tabs(),
        UiNodeKind.Tooltip => new Tooltip(),
        UiNodeKind.Spacer => new Spacer(),
        UiNodeKind.DynamicArea => new DynamicArea(),
        _ => new Panel()
    };
}

public static class UiBindingRuntime
{
    public static object? ResolvePath(object? source, string memberPath)
    {
        if (source == null)
            return null;

        if (string.IsNullOrWhiteSpace(memberPath))
            return source;

        object? current = source;
        foreach (var segment in memberPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current == null)
                return null;

            if (current is IDictionary dictionary && dictionary.Contains(segment))
            {
                current = dictionary[segment];
                continue;
            }

            var type = current.GetType();
            var property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property != null)
            {
                current = property.GetValue(current);
                continue;
            }

            var field = type.GetField(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (field != null)
            {
                current = field.GetValue(current);
                continue;
            }

            return null;
        }

        return current;
    }

    public static bool TrySetValue(object target, string propertyName, object? value)
    {
        var type = target.GetType();
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, ConvertValue(property.PropertyType, value));
            return true;
        }

        var field = type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (field != null)
        {
            field.SetValue(target, ConvertValue(field.FieldType, value));
            return true;
        }

        return false;
    }

    public static bool TrySetPath(object? source, string memberPath, object? value)
    {
        if (source == null || string.IsNullOrWhiteSpace(memberPath))
            return false;

        string[] segments = memberPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return false;

        object? current = source;
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (current == null)
                return false;

            current = ResolveSingleMember(current, segments[i]);
        }

        if (current == null)
            return false;

        if (current is IDictionary dictionary)
        {
            dictionary[segments[^1]] = value;
            return true;
        }

        return TrySetValue(current, segments[^1], value);
    }

    private static object? ResolveSingleMember(object current, string segment)
    {
        if (current is IDictionary dictionary && dictionary.Contains(segment))
            return dictionary[segment];

        var type = current.GetType();
        var property = type.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (property != null)
            return property.GetValue(current);

        var field = type.GetField(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        if (field != null)
            return field.GetValue(current);

        return null;
    }

    private static object? ConvertValue(Type targetType, object? value)
    {
        if (value == null)
            return null;

        Type nonNullable = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (nonNullable.IsInstanceOfType(value))
            return value;

        if (nonNullable.IsEnum)
        {
            if (value is string enumText)
                return Enum.Parse(nonNullable, enumText, true);

            return Enum.ToObject(nonNullable, value);
        }

        if (nonNullable == typeof(string))
            return Convert.ToString(value);

        return Convert.ChangeType(value, nonNullable);
    }
}
