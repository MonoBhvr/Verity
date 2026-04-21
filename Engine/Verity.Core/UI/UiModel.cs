using System.Collections;
using System.Globalization;
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

public enum UiTextHorizontalAlignment
{
    Default,
    Left,
    Center,
    Right
}

public enum UiTextVerticalAlignment
{
    Default,
    Top,
    Middle,
    Bottom
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

public static class UiDefaultDisplayStrings
{
    public const string Element = "Element";
    public const string Container = "Container";
    public const string Panel = "Panel";
    public const string Label = "Label";
    public const string RichText = "RichText";
    public const string RichTextMarkup = "<b>Rich Text</b>";
    public const string Image = "Image";
    public const string Button = "Button";
    public const string IconButton = "IconButton";
    public const string Toggle = "Toggle";
    public const string ToggleGroup = "ToggleGroup";
    public const string Dropdown = "Dropdown";
    public const string InputField = "InputField";
    public const string TextArea = "TextArea";
    public const string Slider = "Slider";
    public const string ProgressBar = "ProgressBar";
    public const string Scrollbar = "Scrollbar";
    public const string ScrollView = "ScrollView";
    public const string ListView = "ListView";
    public const string GridView = "GridView";
    public const string Window = "Window";
    public const string Modal = "Modal";
    public const string Tabs = "Tabs";
    public const string Tooltip = "Tooltip";
    public const string Spacer = "Spacer";
    public const string DynamicArea = "DynamicArea";
    public const string Text = "Text";
    public const string Placeholder = "Type here...";
    public const string NewUiStyle = "NewUiStyle";
    public const string NewUiPrefab = "NewUiPrefab";
    public const string NewScreen = "NewScreen";

    public static readonly string[] DropdownOptions = ["Option A", "Option B", "Option C"];
    public static readonly string[] TabTitles = ["Tab 1", "Tab 2"];
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
    public UiTextHorizontalAlignment TextHorizontalAlignment { get; set; } = UiTextHorizontalAlignment.Default;
    public UiTextVerticalAlignment TextVerticalAlignment { get; set; } = UiTextVerticalAlignment.Default;
    public bool AutoFitText { get; set; }
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
    public string DefaultValue { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
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
    public string Name { get; set; } = UiDefaultDisplayStrings.Element;
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
    private readonly List<object?> _cachedItems = [];
    private string _itemsSource = string.Empty;
    private UiNode? _itemTemplate;
    private bool _isDirty = true;
    private bool _requiresFullRefresh = true;

    public DynamicArea()
    {
        Kind = UiNodeKind.DynamicArea;
        Name = UiDefaultDisplayStrings.DynamicArea;
    }

    public string ItemsSource
    {
        get => _itemsSource;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(_itemsSource, next, StringComparison.Ordinal))
                return;

            _itemsSource = next;
            _isDirty = true;
        }
    }

    public UiNode? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            if (ReferenceEquals(_itemTemplate, value))
                return;

            _itemTemplate = value;
            _isDirty = true;
            _requiresFullRefresh = true;
        }
    }

    internal IReadOnlyList<object?> CachedItems => _cachedItems;
    internal bool RequiresRefresh => _isDirty;
    internal bool RequiresFullRefresh => _requiresFullRefresh;

    internal bool HasSameItems(IReadOnlyList<object?> items)
    {
        if (_cachedItems.Count != items.Count)
            return false;

        for (int i = 0; i < items.Count; i++)
        {
            if (!ItemsMatch(_cachedItems[i], items[i]))
                return false;
        }

        return true;
    }

    internal void CommitRefresh(IReadOnlyList<object?> items)
    {
        _cachedItems.Clear();
        foreach (var item in items)
            _cachedItems.Add(item);

        _isDirty = false;
        _requiresFullRefresh = false;
    }

    internal void ClearRefreshState()
    {
        _cachedItems.Clear();
        _isDirty = false;
        _requiresFullRefresh = false;
    }

    internal static bool ItemsMatch(object? left, object? right)
    {
        if (left == null || right == null)
            return left == right;

        Type leftType = left.GetType();
        Type rightType = right.GetType();
        if (leftType != rightType)
            return false;

        if (leftType == typeof(string) || leftType.IsValueType)
            return Equals(left, right);

        return ReferenceEquals(left, right);
    }
}

public class TextNode : UiNode
{
    public string Text { get; set; } = UiDefaultDisplayStrings.Text;
    public float FontSize { get; set; } = 18f;
    public string FontPath { get; set; } = string.Empty;
    public string FontFamily { get; set; } = string.Empty;
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
        Name = UiDefaultDisplayStrings.Button;
        Text = UiDefaultDisplayStrings.Button;
    }

    public string Text { get; set; } = UiDefaultDisplayStrings.Button;
}

public sealed class IconButton : ClickableNode
{
    public IconButton()
    {
        Kind = UiNodeKind.IconButton;
        Name = UiDefaultDisplayStrings.IconButton;
    }

    [JsonConverter(typeof(SpriteConverter))]
    public Sprite Icon { get; set; }
}

public sealed class Toggle : ClickableNode
{
    public Toggle()
    {
        Kind = UiNodeKind.Toggle;
        Name = UiDefaultDisplayStrings.Toggle;
        Text = UiDefaultDisplayStrings.Toggle;
    }

    public bool IsChecked { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Text { get; set; } = UiDefaultDisplayStrings.Toggle;
}

public sealed class ToggleGroup : UiContainer
{
    public ToggleGroup()
    {
        Kind = UiNodeKind.ToggleGroup;
        Name = UiDefaultDisplayStrings.ToggleGroup;
    }

    public bool AllowSwitchOff { get; set; } = true;
}

public sealed class Dropdown : ClickableNode
{
    public Dropdown()
    {
        Kind = UiNodeKind.Dropdown;
        Name = UiDefaultDisplayStrings.Dropdown;
        Options = [.. UiDefaultDisplayStrings.DropdownOptions];
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
        Name = UiDefaultDisplayStrings.InputField;
    }

    public string Value { get; set; } = string.Empty;
    public string Placeholder { get; set; } = UiDefaultDisplayStrings.Placeholder;
}

public sealed class TextArea : ClickableNode
{
    public TextArea()
    {
        Kind = UiNodeKind.TextArea;
        Name = UiDefaultDisplayStrings.TextArea;
    }

    public string Value { get; set; } = string.Empty;
    public string Placeholder { get; set; } = UiDefaultDisplayStrings.Placeholder;
}

public sealed class Slider : ClickableNode
{
    public Slider()
    {
        Kind = UiNodeKind.Slider;
        Name = UiDefaultDisplayStrings.Slider;
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
        Name = UiDefaultDisplayStrings.ProgressBar;
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
        Name = UiDefaultDisplayStrings.Scrollbar;
    }

    public float Value { get; set; }
}

public sealed class ScrollView : UiContainer
{
    public ScrollView()
    {
        Kind = UiNodeKind.ScrollView;
        Name = UiDefaultDisplayStrings.ScrollView;
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
        Name = UiDefaultDisplayStrings.ListView;
    }

    public bool Virtualized { get; set; } = true;
    public int ItemCount { get; set; } = 10;
}

public sealed class GridView : UiContainer
{
    public GridView()
    {
        Kind = UiNodeKind.GridView;
        Name = UiDefaultDisplayStrings.GridView;
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
        Name = UiDefaultDisplayStrings.Window;
        Title = UiDefaultDisplayStrings.Window;
    }

    public string Title { get; set; } = UiDefaultDisplayStrings.Window;
}

public sealed class Modal : UiContainer
{
    public Modal()
    {
        Kind = UiNodeKind.Modal;
        Name = UiDefaultDisplayStrings.Modal;
    }

    public bool DismissOnBackgroundClick { get; set; } = true;
}

public sealed class Tabs : UiContainer
{
    public Tabs()
    {
        Kind = UiNodeKind.Tabs;
        Name = UiDefaultDisplayStrings.Tabs;
    }

    public int SelectedIndex { get; set; }
    public List<string> Titles { get; set; } = [.. UiDefaultDisplayStrings.TabTitles];
}

public sealed class Tooltip : UiNode
{
    public Tooltip()
    {
        Kind = UiNodeKind.Tooltip;
        Name = UiDefaultDisplayStrings.Tooltip;
        Text = UiDefaultDisplayStrings.Tooltip;
    }

    public string Text { get; set; } = UiDefaultDisplayStrings.Tooltip;
}

public sealed class Spacer : UiNode
{
    public Spacer()
    {
        Kind = UiNodeKind.Spacer;
        Name = UiDefaultDisplayStrings.Spacer;
    }
}

public sealed class UiStateStyleOverride
{
    public UiStateFlags State { get; set; }
    public UiVisualStyle Visual { get; set; } = new();
}

public sealed class UiStyleAsset
{
    public string Name { get; set; } = UiDefaultDisplayStrings.NewUiStyle;
    public Dictionary<string, Color> Colors { get; set; } = new();
    public Dictionary<string, float> Numbers { get; set; } = new();
    public Dictionary<string, string> Strings { get; set; } = new();
    public List<UiStateStyleOverride> States { get; set; } = [];
}

public sealed class UiPrefabAsset
{
    public string Name { get; set; } = UiDefaultDisplayStrings.NewUiPrefab;
    public UiNode Root { get; set; } = new Panel { Name = "PrefabRoot" };
}

public sealed class UIScreenAsset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = UiDefaultDisplayStrings.NewScreen;
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
        UiNodeKind.Container => new UiContainer { Name = UiDefaultDisplayStrings.Container },
        UiNodeKind.Panel => new Panel { Name = UiDefaultDisplayStrings.Panel },
        UiNodeKind.Label => new Label { Name = UiDefaultDisplayStrings.Label, Text = UiDefaultDisplayStrings.Label },
        UiNodeKind.RichText => new RichText { Name = UiDefaultDisplayStrings.RichText, Text = UiDefaultDisplayStrings.RichTextMarkup },
        UiNodeKind.Image => new Image { Name = UiDefaultDisplayStrings.Image },
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
            var property = GetProperty(type, segment, requireSetter: false);
            if (property != null)
            {
                current = property.GetValue(current);
                continue;
            }

            var field = GetField(type, segment);
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
        var property = GetProperty(type, propertyName, requireSetter: true);
        if (property != null && property.CanWrite)
        {
            property.SetValue(target, ConvertValue(property.PropertyType, value));
            return true;
        }

        var field = GetField(type, propertyName);
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

        return TrySetPathRecursive(source, segments, 0, value);
    }

    private static bool TrySetPathRecursive(object current, string[] segments, int index, object? value)
    {
        if (index == segments.Length - 1)
        {
            if (current is IDictionary dictionary)
            {
                dictionary[segments[index]] = value;
                return true;
            }

            return TrySetValue(current, segments[index], value);
        }

        object? child = ResolveSingleMember(current, segments[index]);
        if (child == null)
            return false;

        if (!TrySetPathRecursive(child, segments, index + 1, value))
            return false;

        if (current is IDictionary dictionaryParent)
        {
            dictionaryParent[segments[index]] = child;
            return true;
        }

        return TrySetValue(current, segments[index], child);
    }

    private static object? ResolveSingleMember(object current, string segment)
    {
        if (current is IDictionary dictionary && dictionary.Contains(segment))
            return dictionary[segment];

        var type = current.GetType();
        var property = GetProperty(type, segment, requireSetter: false);
        if (property != null)
            return property.GetValue(current);

        var field = GetField(type, segment);
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

    private static PropertyInfo? GetProperty(Type type, string name, bool requireSetter)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        PropertyInfo[] properties = type.GetProperties(flags);

        PropertyInfo? exact = properties.FirstOrDefault(property =>
            string.Equals(property.Name, name, StringComparison.Ordinal) && (!requireSetter || property.CanWrite));
        if (exact != null)
            return exact;

        return properties.FirstOrDefault(property =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && (!requireSetter || property.CanWrite));
    }

    private static FieldInfo? GetField(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        FieldInfo[] fields = type.GetFields(flags);

        FieldInfo? exact = fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.Ordinal));
        if (exact != null)
            return exact;

        return fields.FirstOrDefault(field => string.Equals(field.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static object? ParseTypedValue(string? typeName, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        string normalizedType = string.IsNullOrWhiteSpace(typeName)
            ? "object"
            : typeName.Trim().ToLowerInvariant();

        string text = rawValue.Trim();

        try
        {
            return normalizedType switch
            {
                "string" => text,
                "bool" or "boolean" => bool.Parse(text),
                "byte" => byte.Parse(text, CultureInfo.InvariantCulture),
                "int" or "int32" => int.Parse(text, CultureInfo.InvariantCulture),
                "long" or "int64" => long.Parse(text, CultureInfo.InvariantCulture),
                "float" or "single" => float.Parse(text, CultureInfo.InvariantCulture),
                "double" => double.Parse(text, CultureInfo.InvariantCulture),
                "decimal" => decimal.Parse(text, CultureInfo.InvariantCulture),
                "vector2" => ParseVector2(text),
                _ => TryParseLooseLiteral(text, out object? parsed) ? parsed : text
            };
        }
        catch
        {
            return text;
        }
    }

    private static bool TryParseLooseLiteral(string text, out object? value)
    {
        if (bool.TryParse(text, out bool boolValue))
        {
            value = boolValue;
            return true;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
        {
            value = intValue;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double doubleValue))
        {
            value = doubleValue;
            return true;
        }

        value = null;
        return false;
    }

    private static Vector2 ParseVector2(string text)
    {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return Vector2.Zero;

        float x = float.Parse(parts[0], CultureInfo.InvariantCulture);
        float y = float.Parse(parts[1], CultureInfo.InvariantCulture);
        return new Vector2(x, y);
    }
}

public static class UiExpressionRuntime
{
    public static bool TryEvaluate(string expression, Func<string, object?> resolver, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        string body = expression.Trim();
        if (body.StartsWith("="))
            body = body[1..].Trim();

        if (body.Length == 0)
            return false;

        try
        {
            var parser = new Parser(body, resolver);
            value = parser.Parse();
            return true;
        }
        catch
        {
            value = null;
            return false;
        }
    }

    private sealed class Parser
    {
        private readonly string _text;
        private readonly Func<string, object?> _resolver;
        private int _index;

        public Parser(string text, Func<string, object?> resolver)
        {
            _text = text;
            _resolver = resolver;
        }

        public object? Parse()
        {
            object? result = ParseExpression();
            SkipWhitespace();
            if (_index < _text.Length)
                throw new FormatException("Unexpected token.");

            return result;
        }

        private object? ParseExpression()
        {
            object? left = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    left = Add(left, ParseTerm());
                    continue;
                }

                if (Match('-'))
                {
                    left = ToNumber(left) - ToNumber(ParseTerm());
                    continue;
                }

                return left;
            }
        }

        private object? ParseTerm()
        {
            object? left = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    left = ToNumber(left) * ToNumber(ParseFactor());
                    continue;
                }

                if (Match('/'))
                {
                    left = ToNumber(left) / ToNumber(ParseFactor());
                    continue;
                }

                if (Match('%'))
                {
                    left = ToNumber(left) % ToNumber(ParseFactor());
                    continue;
                }

                return left;
            }
        }

        private object? ParseFactor()
        {
            SkipWhitespace();

            if (Match('+'))
                return ParseFactor();

            if (Match('-'))
                return -ToNumber(ParseFactor());

            if (Match('('))
            {
                object? inner = ParseExpression();
                Expect(')');
                return inner;
            }

            if (Peek() is '\'' or '"')
                return ParseString();

            if (char.IsDigit(Peek()) || Peek() == '.')
                return ParseNumber();

            string identifier = ParseIdentifier();
            SkipWhitespace();
            if (Match('('))
            {
                var args = new List<object?>();
                SkipWhitespace();
                if (!Match(')'))
                {
                    do
                    {
                        args.Add(ParseExpression());
                        SkipWhitespace();
                    }
                    while (Match(','));

                    Expect(')');
                }

                return EvaluateFunction(identifier, args);
            }

            return _resolver(identifier);
        }

        private string ParseIdentifier()
        {
            SkipWhitespace();
            int start = _index;
            while (_index < _text.Length)
            {
                char ch = _text[_index];
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '.')
                {
                    _index++;
                    continue;
                }

                break;
            }

            if (start == _index)
                throw new FormatException("Identifier expected.");

            return _text[start.._index];
        }

        private object ParseNumber()
        {
            int start = _index;
            bool seenExponent = false;
            bool seenDot = false;

            while (_index < _text.Length)
            {
                char ch = _text[_index];
                if (char.IsDigit(ch))
                {
                    _index++;
                    continue;
                }

                if (ch == '.' && !seenDot)
                {
                    seenDot = true;
                    _index++;
                    continue;
                }

                if ((ch == 'e' || ch == 'E') && !seenExponent)
                {
                    seenExponent = true;
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-'))
                        _index++;
                    continue;
                }

                break;
            }

            string slice = _text[start.._index];
            return double.Parse(slice, CultureInfo.InvariantCulture);
        }

        private string ParseString()
        {
            char quote = _text[_index++];
            var builder = new System.Text.StringBuilder();
            while (_index < _text.Length)
            {
                char ch = _text[_index++];
                if (ch == quote)
                    return builder.ToString();

                if (ch == '\\' && _index < _text.Length)
                {
                    char escaped = _text[_index++];
                    builder.Append(escaped switch
                    {
                        'n' => '\n',
                        'r' => '\r',
                        't' => '\t',
                        '\\' => '\\',
                        '\'' => '\'',
                        '"' => '"',
                        _ => escaped
                    });
                    continue;
                }

                builder.Append(ch);
            }

            throw new FormatException("Unterminated string literal.");
        }

        private object? EvaluateFunction(string name, IReadOnlyList<object?> args)
        {
            return name.ToLowerInvariant() switch
            {
                "min" => args.Count >= 2 ? Math.Min(ToNumber(args[0]), ToNumber(args[1])) : 0d,
                "max" => args.Count >= 2 ? Math.Max(ToNumber(args[0]), ToNumber(args[1])) : 0d,
                "clamp" => args.Count >= 3 ? Math.Clamp(ToNumber(args[0]), ToNumber(args[1]), ToNumber(args[2])) : 0d,
                "abs" => args.Count >= 1 ? Math.Abs(ToNumber(args[0])) : 0d,
                "round" => args.Count >= 1 ? Math.Round(ToNumber(args[0])) : 0d,
                "floor" => args.Count >= 1 ? Math.Floor(ToNumber(args[0])) : 0d,
                "ceil" or "ceiling" => args.Count >= 1 ? Math.Ceiling(ToNumber(args[0])) : 0d,
                "lerp" => args.Count >= 3 ? Lerp(args[0], args[1], args[2]) : 0d,
                _ => throw new FormatException($"Unknown function: {name}")
            };
        }

        private static double Lerp(object? a, object? b, object? t)
        {
            double tValue = ToNumber(t);
            return ToNumber(a) + ((ToNumber(b) - ToNumber(a)) * tValue);
        }

        private static object Add(object? left, object? right)
        {
            if (left is string || right is string)
                return $"{left}{right}";

            return ToNumber(left) + ToNumber(right);
        }

        private static double ToNumber(object? value)
        {
            if (value == null)
                return 0d;

            return value switch
            {
                double d => d,
                float f => f,
                decimal dec => (double)dec,
                byte b => b,
                sbyte sb => sb,
                short s => s,
                ushort us => us,
                int i => i,
                uint ui => ui,
                long l => l,
                ulong ul => ul,
                bool boolValue => boolValue ? 1d : 0d,
                string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double parsed) => parsed,
                _ => Convert.ToDouble(value, CultureInfo.InvariantCulture)
            };
        }

        private char Peek() => _index < _text.Length ? _text[_index] : '\0';

        private bool Match(char expected)
        {
            SkipWhitespace();
            if (Peek() != expected)
                return false;

            _index++;
            return true;
        }

        private void Expect(char expected)
        {
            if (!Match(expected))
                throw new FormatException($"Expected '{expected}'.");
        }

        private void SkipWhitespace()
        {
            while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                _index++;
        }
    }
}
