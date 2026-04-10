using System.Collections;
using System.Reflection;
using System.Text;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Core.UI;

public sealed class Canvas
{
    private readonly Dictionary<string, object?> _screen = new(StringComparer.OrdinalIgnoreCase);
    private string? _hoveredId;
    private string? _pressedId;
    private string? _focusedId;
    private double _lastClickTime;

    public Entity? OwnerEntity { get; }
    public World.World? World { get; }
    public UIScreenAsset Screen { get; }
    public UiScript? UiScript { get; }
    public bool Visible { get; set; } = true;
    public string OpenedRole { get; internal set; } = string.Empty;

    public Canvas(UIScreenAsset screen, World.World? world = null, Entity? ownerEntity = null)
    {
        OwnerEntity = ownerEntity;
        World = world ?? ownerEntity?.World;
        Screen = UiSerializer.CloneScreen(screen);
        foreach (var variable in Screen.Variables)
            if (!string.IsNullOrWhiteSpace(variable.Name))
                _screen[variable.Name] = null;

        UiScript = UiSystem.CreateUiScript(Screen.UiScriptType);
        if (UiScript != null)
        {
            UiScript.Canvas = this;
            UiScript.OnOpen();
        }
    }

    public T? Query<T>(string nameOrId) where T : UiNode => Screen.Root.Query<T>(nameOrId);
    public UiNode? Query(string nameOrId) => Screen.Root.Query(nameOrId);
    public IReadOnlyDictionary<string, object?> GetVariables() => _screen;
    public bool TryGetVariable(string name, out object? value) => _screen.TryGetValue(name, out value);

    public void Set(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;
        _screen[name] = value;
        UiScript?.OnVariableChanged(name, value);
    }

    public void Send(string command, object? payload = null)
    {
        if (!string.IsNullOrWhiteSpace(command))
            UiScript?.OnCommand(command, payload);
    }

    public void Update(float viewportWidth, float viewportHeight)
    {
        if (!Visible)
            return;

        UiScript?.OnUpdate(Time.DeltaTime);
        RebuildDynamicAreas();
        ApplyBindings();
        UiLayoutEngine.Layout(Screen, viewportWidth, viewportHeight);
        UiScript?.OnLayout();
        UiLayoutEngine.Layout(Screen, viewportWidth, viewportHeight);
        ProcessInput();
    }

    public void Close() => UiScript?.OnClose();

    private void RebuildDynamicAreas()
    {
        foreach (var node in Screen.Root.DescendantsAndSelf())
        {
            if (node is not DynamicArea area)
                continue;

            area.Children.Clear();
            if (area.ItemTemplate == null)
                continue;

            foreach (var item in ResolveEnumerable(area, area.ItemsSource))
            {
                var clone = UiSerializer.CloneNode(area.ItemTemplate);
                clone.IsRuntimeGenerated = true;
                clone.SetBindingItemRecursive(item);
                area.AddChild(clone);
            }
        }
    }

    private IEnumerable ResolveEnumerable(UiNode node, string path)
    {
        if (!TryResolveBindingReference(node, path, out var source, out var memberPath))
            return Array.Empty<object>();

        object? resolved = UiBindingRuntime.ResolvePath(source, memberPath);
        return resolved is IEnumerable e && resolved is not string ? e : Array.Empty<object>();
    }

    private void ApplyBindings()
    {
        foreach (var node in Screen.Root.DescendantsAndSelf())
        foreach (var binding in node.Bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Path) || string.IsNullOrWhiteSpace(binding.TargetProperty))
                continue;
            if (!TryResolveBindingReference(node, binding.Path, out var source, out var memberPath))
                continue;

            UiBindingRuntime.TrySetPath(node, binding.TargetProperty, UiBindingRuntime.ResolvePath(source, memberPath));
        }
    }

    private bool TryResolveBindingReference(UiNode node, string path, out object? source, out string memberPath)
    {
        source = null;
        memberPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.StartsWith("Screen.", StringComparison.OrdinalIgnoreCase))
        {
            source = _screen;
            memberPath = path["Screen.".Length..];
            return true;
        }

        if (path.StartsWith("Item.", StringComparison.OrdinalIgnoreCase))
        {
            source = node.BindingItem;
            memberPath = path["Item.".Length..];
            return source != null;
        }

        if (path.StartsWith("State.", StringComparison.OrdinalIgnoreCase))
        {
            source = UiScript?.GetStateValues();
            memberPath = path["State.".Length..];
            return source != null;
        }

        int dot = path.IndexOf('.');
        string root = dot >= 0 ? path[..dot] : path;
        string rest = dot >= 0 ? path[(dot + 1)..] : string.Empty;

        if (_screen.TryGetValue(root, out var screenVariable))
        {
            source = screenVariable;
            memberPath = rest;
            return true;
        }

        if (UiScript != null && UiScript.TryResolveState(root, out var stateVariable))
        {
            source = stateVariable;
            memberPath = rest;
            return true;
        }

        if (node.BindingItem != null)
        {
            var directItem = UiBindingRuntime.ResolvePath(node.BindingItem, path);
            if (directItem != null)
            {
                source = directItem;
                return true;
            }
        }

        if (UiSystem.TryResolveBindingSource(root, out var global))
        {
            source = string.IsNullOrWhiteSpace(rest) ? global : UiBindingRuntime.ResolvePath(global, rest);
            return true;
        }

        if (_screen.ContainsKey(path))
        {
            source = _screen;
            memberPath = path;
            return true;
        }

        return false;
    }

    private void ProcessInput()
    {
        var pointer = UiSystem.PointerPosition;
        var hovered = HitTest(pointer);
        bool down = Verity.Input.Input.MousePressed(Verity.Input.MouseButton.Left);
        bool held = Verity.Input.Input.MouseDown(Verity.Input.MouseButton.Left);
        bool up = Verity.Input.Input.MouseReleased(Verity.Input.MouseButton.Left);
        float scroll = Verity.Input.Input.ScrollDelta;

        var previousHovered = _hoveredId != null ? Query(_hoveredId) : null;
        if (hovered?.Id != _hoveredId)
        {
            if (previousHovered != null)
            {
                previousHovered.RuntimeState &= ~UiStateFlags.Hover;
                DispatchEvent(previousHovered, new UiEvent { Type = UiEventType.PointerExit, Node = previousHovered, Position = pointer });
            }

            _hoveredId = hovered?.Id;
            if (hovered != null)
            {
                hovered.RuntimeState |= UiStateFlags.Hover;
                DispatchEvent(hovered, new UiEvent { Type = UiEventType.PointerEnter, Node = hovered, Position = pointer });
            }
        }

        if (hovered != null && scroll != 0f)
        {
            DispatchEvent(hovered, new UiEvent { Type = UiEventType.Scroll, Node = hovered, Position = pointer, ScrollDelta = scroll });
            if (hovered is ScrollView scrollView)
                scrollView.ScrollOffset += new Vector2(0, scroll * 16f);
        }

        if (down && hovered != null)
        {
            _pressedId = hovered.Id;
            hovered.RuntimeState |= UiStateFlags.Pressed | UiStateFlags.Focused;
            DispatchEvent(hovered, new UiEvent { Type = UiEventType.PointerDown, Node = hovered, Position = pointer });
        }

        if (held && _pressedId != null && Query(_pressedId) is Slider slider)
        {
            float pct = Math.Clamp((pointer.X - slider.LayoutRect.X) / Math.Max(1f, slider.LayoutRect.Width), 0f, 1f);
            float next = slider.Min + ((slider.Max - slider.Min) * pct);
            if (Math.Abs(next - slider.Value) > 0.0001f)
            {
                slider.Value = next;
                DispatchEvent(slider, new UiEvent { Type = UiEventType.ValueChanged, Node = slider, Position = pointer, Value = next });
            }
        }

        if (up)
        {
            var pressed = _pressedId != null ? Query(_pressedId) : null;
            if (pressed != null)
            {
                pressed.RuntimeState &= ~UiStateFlags.Pressed;
                DispatchEvent(pressed, new UiEvent { Type = UiEventType.PointerUp, Node = pressed, Position = pointer });
            }

            if (pressed != null && hovered?.Id == pressed.Id)
            {
                double now = Time.TotalTime;
                UiEventType clickType = now - _lastClickTime < 0.28 ? UiEventType.DoubleClick : UiEventType.Click;
                _lastClickTime = now;
                DispatchEvent(pressed, new UiEvent { Type = clickType, Node = pressed, Position = pointer });

                if (pressed is InputField or TextArea) SetFocusedNode(pressed, pointer);
                else if (pressed is not Slider) SetFocusedNode(null, pointer);

                if (pressed is Toggle toggle)
                {
                    toggle.IsChecked = !toggle.IsChecked;
                    toggle.RuntimeState = toggle.IsChecked ? toggle.RuntimeState | UiStateFlags.Checked : toggle.RuntimeState & ~UiStateFlags.Checked;
                    DispatchEvent(toggle, new UiEvent { Type = UiEventType.ValueChanged, Node = toggle, Position = pointer, Value = toggle.IsChecked });
                }
                else if (pressed is Dropdown dropdown)
                {
                    dropdown.Expanded = !dropdown.Expanded;
                    dropdown.RuntimeState = dropdown.Expanded ? dropdown.RuntimeState | UiStateFlags.Expanded : dropdown.RuntimeState & ~UiStateFlags.Expanded;
                }
            }

            _pressedId = null;
        }

        if (hovered == null && down)
            SetFocusedNode(null, pointer);

        ProcessFocusedInput(pointer);
    }

    private void DispatchEvent(UiNode? node, UiEvent evt)
    {
        if (node == null)
            return;
        if (evt.Type == UiEventType.ValueChanged)
            ApplyTwoWayBindings(node, evt.Value);
        node.RaiseEvent(evt);
        UiSystem.InvokeActions(this, node, evt);
    }

    private void SetFocusedNode(UiNode? node, Vector2 pointer)
    {
        if (node?.Id == _focusedId)
            return;

        var previous = _focusedId != null ? Query(_focusedId) : null;
        if (previous != null)
        {
            previous.RuntimeState &= ~UiStateFlags.Focused;
            DispatchEvent(previous, new UiEvent { Type = UiEventType.FocusChanged, Node = previous, Position = pointer, Value = false });
        }

        _focusedId = node?.Id;
        if (node != null)
        {
            node.RuntimeState |= UiStateFlags.Focused;
            DispatchEvent(node, new UiEvent { Type = UiEventType.FocusChanged, Node = node, Position = pointer, Value = true });
        }
    }

    private void ProcessFocusedInput(Vector2 pointer)
    {
        if (_focusedId == null)
            return;
        var focused = Query(_focusedId);
        if (focused is not InputField && focused is not TextArea)
            return;

        string appended = ReadTypedText();
        if (!string.IsNullOrEmpty(appended))
        {
            if (focused is InputField input)
            {
                input.Value += appended;
                DispatchEvent(input, new UiEvent { Type = UiEventType.ValueChanged, Node = input, Position = pointer, Value = input.Value });
            }
            else if (focused is TextArea area)
            {
                area.Value += appended;
                DispatchEvent(area, new UiEvent { Type = UiEventType.ValueChanged, Node = area, Position = pointer, Value = area.Value });
            }
        }

        if (Verity.Input.Input.Pressed(Verity.Input.KeyCode.Backspace))
        {
            if (focused is InputField i && i.Value.Length > 0)
            {
                i.Value = i.Value[..^1];
                DispatchEvent(i, new UiEvent { Type = UiEventType.ValueChanged, Node = i, Position = pointer, Value = i.Value });
            }
            else if (focused is TextArea a && a.Value.Length > 0)
            {
                a.Value = a.Value[..^1];
                DispatchEvent(a, new UiEvent { Type = UiEventType.ValueChanged, Node = a, Position = pointer, Value = a.Value });
            }
        }

        if (Verity.Input.Input.Pressed(Verity.Input.KeyCode.Return))
        {
            if (focused is TextArea area)
            {
                area.Value += Environment.NewLine;
                DispatchEvent(area, new UiEvent { Type = UiEventType.ValueChanged, Node = area, Position = pointer, Value = area.Value });
            }
            else if (focused != null)
            {
                DispatchEvent(focused, new UiEvent { Type = UiEventType.Submit, Node = focused, Position = pointer });
            }
        }
    }

    private static string ReadTypedText()
    {
        var builder = new StringBuilder();
        bool shift = Verity.Input.Input.Down(Verity.Input.KeyCode.LeftShift) || Verity.Input.Input.Down(Verity.Input.KeyCode.RightShift);
        for (int i = 0; i < 26; i++)
        {
            var key = (Verity.Input.KeyCode)((int)Verity.Input.KeyCode.A + i);
            if (!Verity.Input.Input.Pressed(key))
                continue;
            char ch = (char)('a' + i);
            builder.Append(shift ? char.ToUpperInvariant(ch) : ch);
        }

        for (int i = 0; i <= 9; i++)
        {
            var key = (Verity.Input.KeyCode)((int)Verity.Input.KeyCode.Alpha0 + i);
            if (Verity.Input.Input.Pressed(key))
                builder.Append((char)('0' + i));
        }

        if (Verity.Input.Input.Pressed(Verity.Input.KeyCode.Space))
            builder.Append(' ');
        return builder.ToString();
    }

    private void ApplyTwoWayBindings(UiNode node, object? value)
    {
        foreach (var binding in node.Bindings)
        {
            if (binding.Mode != UiBindingMode.TwoWay || string.IsNullOrWhiteSpace(binding.Path))
                continue;
            if (!TryResolveBindingReference(node, binding.Path, out var source, out var memberPath) || string.IsNullOrWhiteSpace(memberPath))
                continue;
            UiBindingRuntime.TrySetPath(source, memberPath, value);
        }
    }

    public UiNode? HitTest(Vector2 point)
    {
        UiNode? best = null;
        int bestDepth = int.MinValue;
        foreach (var node in Screen.Root.DescendantsAndSelf())
        {
            if (!node.Active || !node.Visible || !node.Interactable || !node.LayoutRect.Contains(point))
                continue;
            int depth = node.Transform.ZOrder + GetDepth(node);
            if (depth >= bestDepth)
            {
                bestDepth = depth;
                best = node;
            }
        }
        return best;
    }

    private static int GetDepth(UiNode node)
    {
        int depth = 0;
        for (var current = node.Parent; current != null; current = current.Parent)
            depth++;
        return depth;
    }
}

public static class UiLayoutEngine
{
    public static void Layout(UIScreenAsset screen, float viewportWidth, float viewportHeight)
    {
        LayoutNode(screen.Root, new UiRect(0, 0, screen.ReferenceResolution.X, screen.ReferenceResolution.Y));
    }

    private static void LayoutNode(UiNode node, UiRect parentRect)
    {
        node.LayoutRect = ResolveRect(node.Transform, parentRect);
        if (node is UiContainer container && container.Layout.Mode != UiLayoutMode.Free)
            ApplyContainerLayout(container);
        foreach (var child in node.Children)
            LayoutNode(child, node.LayoutRect);
    }

    private static UiRect ResolveRect(UiTransform transform, UiRect parent)
    {
        var aMin = new Vector2(parent.X + (parent.Width * transform.AnchorMin.X), parent.Y + (parent.Height * transform.AnchorMin.Y));
        var aMax = new Vector2(parent.X + (parent.Width * transform.AnchorMax.X), parent.Y + (parent.Height * transform.AnchorMax.Y));
        var m = transform.Margin;
        if (transform.AnchorMin != transform.AnchorMax)
        {
            float x = aMin.X + transform.Position.X + m.X;
            float y = aMin.Y + transform.Position.Y + m.Y;
            float w = Math.Clamp((aMax.X - aMin.X) + transform.Size.X - m.X - m.Z, transform.MinSize.X, transform.MaxSize.X);
            float h = Math.Clamp((aMax.Y - aMin.Y) + transform.Size.Y - m.Y - m.W, transform.MinSize.Y, transform.MaxSize.Y);
            return new UiRect(x, y, w, h);
        }

        float width = Math.Clamp(transform.Size.X, transform.MinSize.X, transform.MaxSize.X);
        float height = Math.Clamp(transform.Size.Y, transform.MinSize.Y, transform.MaxSize.Y);
        return new UiRect(aMin.X + transform.Position.X - (width * transform.Pivot.X), aMin.Y + transform.Position.Y - (height * transform.Pivot.Y), width, height);
    }

    private static void ApplyContainerLayout(UiContainer container)
    {
        if (container.Children.Count == 0)
            return;
        var rect = container.LayoutRect;
        var pad = container.Layout.Padding;
        float x = rect.X + pad.X;
        float y = rect.Y + pad.Y;
        float availableWidth = Math.Max(0, rect.Width - pad.X - pad.Z);
        float availableHeight = Math.Max(0, rect.Height - pad.Y - pad.W);

        switch (container.Layout.Mode)
        {
            case UiLayoutMode.Horizontal:
                foreach (var child in container.Children)
                {
                    child.Transform.AnchorMin = child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = Vector2.Zero;
                    child.Transform.Position = new Vector2(x - rect.X, y - rect.Y);
                    if (container.Layout.FitChildren)
                        child.Transform.Size = new Vector2(Math.Max(child.Transform.Size.X, 96f), availableHeight);
                    x += child.Transform.Size.X + container.Layout.Spacing.X;
                }
                break;

            case UiLayoutMode.Vertical:
            case UiLayoutMode.ScrollContent:
                foreach (var child in container.Children)
                {
                    child.Transform.AnchorMin = child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = Vector2.Zero;
                    child.Transform.Position = new Vector2(x - rect.X, y - rect.Y);
                    if (container.Layout.FitChildren)
                        child.Transform.Size = new Vector2(availableWidth, Math.Max(child.Transform.Size.Y, 40f));
                    y += child.Transform.Size.Y + container.Layout.Spacing.Y;
                }
                break;

            case UiLayoutMode.Grid:
                int cols = Math.Max(1, container.Layout.Columns);
                float cellWidth = (availableWidth - ((cols - 1) * container.Layout.Spacing.X)) / cols;
                float rowHeight = 0f;
                for (int i = 0; i < container.Children.Count; i++)
                {
                    var child = container.Children[i];
                    int col = i % cols;
                    int row = i / cols;
                    child.Transform.AnchorMin = child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = Vector2.Zero;
                    child.Transform.Position = new Vector2(pad.X + (col * (cellWidth + container.Layout.Spacing.X)), pad.Y + (row * (Math.Max(rowHeight, child.Transform.Size.Y) + container.Layout.Spacing.Y)));
                    if (container.Layout.FitChildren)
                        child.Transform.Size = new Vector2(cellWidth, child.Transform.Size.Y);
                    rowHeight = Math.Max(rowHeight, child.Transform.Size.Y);
                }
                break;

            case UiLayoutMode.Wrap:
                float localX = pad.X, localY = pad.Y, wrapHeight = 0f;
                foreach (var child in container.Children)
                {
                    float childWidth = container.Layout.FitChildren ? Math.Min(Math.Max(child.Transform.Size.X, 96f), availableWidth) : child.Transform.Size.X;
                    if (localX + childWidth > pad.X + availableWidth && localX > pad.X)
                    {
                        localX = pad.X;
                        localY += wrapHeight + container.Layout.Spacing.Y;
                        wrapHeight = 0f;
                    }
                    child.Transform.AnchorMin = child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = Vector2.Zero;
                    child.Transform.Position = new Vector2(localX, localY);
                    child.Transform.Size = new Vector2(childWidth, child.Transform.Size.Y);
                    localX += childWidth + container.Layout.Spacing.X;
                    wrapHeight = Math.Max(wrapHeight, child.Transform.Size.Y);
                }
                break;

            case UiLayoutMode.Circle:
                int count = container.Children.Count;
                if (count == 0)
                    return;
                Vector2 center = new(rect.Width * 0.5f, rect.Height * 0.5f);
                float step = 360f / count;
                float dir = container.Layout.CircleClockwise ? 1f : -1f;
                for (int i = 0; i < count; i++)
                {
                    var child = container.Children[i];
                    float angle = container.Layout.CircleStartAngle + (step * i * dir);
                    float rad = angle * (MathF.PI / 180f);
                    Vector2 offset = new(MathF.Cos(rad) * container.Layout.CircleRadius, MathF.Sin(rad) * container.Layout.CircleRadius);
                    child.Transform.AnchorMin = child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = new Vector2(0.5f, 0.5f);
                    child.Transform.Position = center + offset;
                }
                break;
        }
    }
}

public static class UiSystem
{
    private static readonly Dictionary<string, object> BindingSources = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Canvas> Active = [];

    public static string? AssetsRoot { get; set; }
    public static ProjectSettings? ProjectSettings { get; set; }
    public static Vector2 PointerPosition { get; private set; }
    public static IReadOnlyList<Canvas> ActiveCanvases => Active;
    public static UIScreenAsset Load(string path) => UiSerializer.Load(path);
    public static string ResolveAssetPath(string path, string? guid = null) => AssetPathUtility.ResolvePath(AssetsRoot ?? AppContext.BaseDirectory, path, guid);
    public static UIScreenAsset LoadAsset(string path, string? guid = null) => UiSerializer.Load(ResolveAssetPath(path, guid));
    public static UiRoleBinding? ResolveRoleBinding(string role, World.World? world = null)
    {
        if (string.IsNullOrWhiteSpace(role))
            return null;

        UiRoleBinding? worldOverride = world?.UiRoleOverrides.LastOrDefault(binding =>
            string.Equals(binding.Role, role, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(binding.Path));
        if (worldOverride != null)
            return worldOverride;

        return ProjectSettings?.UiRoleDefaults.LastOrDefault(binding =>
            string.Equals(binding.Role, role, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(binding.Path));
    }

    public static Canvas ShowRole(string role, World.World? world = null, Entity? ownerEntity = null)
    {
        UiRoleBinding? binding = ResolveRoleBinding(role, world ?? ownerEntity?.World);
        if (binding == null)
            throw new InvalidOperationException($"UI role '{role}' is not configured.");

        Canvas canvas = ShowScreen(LoadAsset(binding.Path, binding.Guid), world, ownerEntity);
        canvas.OpenedRole = role;
        return canvas;
    }

    public static Canvas ShowScreen(UIScreenAsset screen, World.World? world = null, Entity? ownerEntity = null)
    {
        var canvas = new Canvas(screen, world, ownerEntity);
        Active.Add(canvas);
        Active.Sort((a, b) => a.Screen.SortingOrder.CompareTo(b.Screen.SortingOrder));
        return canvas;
    }

    public static void HideScreen(string id, World.World? world = null)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (world != null && Active[i].World != world)
                continue;
            if (string.Equals(Active[i].Screen.Id, id, StringComparison.OrdinalIgnoreCase) || string.Equals(Active[i].Screen.Name, id, StringComparison.OrdinalIgnoreCase))
            {
                Active[i].Close();
                Active.RemoveAt(i);
            }
        }
    }

    public static void HideCanvas(Canvas canvas) { canvas.Close(); Active.Remove(canvas); }
    public static Canvas? FindCanvas(string screenNameOrId, World.World? world = null) => Active.LastOrDefault(c => (world == null || c.World == world) && (string.Equals(c.Screen.Id, screenNameOrId, StringComparison.OrdinalIgnoreCase) || string.Equals(c.Screen.Name, screenNameOrId, StringComparison.OrdinalIgnoreCase)));
    public static Canvas? FindCanvasByRole(string role, World.World? world = null) => Active.LastOrDefault(c => (world == null || c.World == world) && string.Equals(c.OpenedRole, role, StringComparison.OrdinalIgnoreCase));
    public static void HideRole(string role, World.World? world = null)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if ((world == null || Active[i].World == world) && string.Equals(Active[i].OpenedRole, role, StringComparison.OrdinalIgnoreCase))
            {
                Active[i].Close();
                Active.RemoveAt(i);
            }
        }
    }
    public static IReadOnlyList<Canvas> GetCanvases(World.World? world = null) => world == null ? Active : Active.Where(c => c.World == world).ToArray();
    public static T? Query<T>(string nameOrId) where T : UiNode => Active.AsEnumerable().Reverse().Select(c => c.Query<T>(nameOrId)).FirstOrDefault(v => v != null);
    public static UiNode? Query(string nameOrId) => Active.AsEnumerable().Reverse().Select(c => c.Query(nameOrId)).FirstOrDefault(v => v != null);
    public static void Bind(string path, object source) { if (!string.IsNullOrWhiteSpace(path) && source != null) BindingSources[path] = source; }
    public static void Unbind(string path) { if (!string.IsNullOrWhiteSpace(path)) BindingSources.Remove(path); }
    public static bool TryResolveBindingSource(string key, out object source) => BindingSources.TryGetValue(key, out source!);

    public static void Update(float viewportWidth, float viewportHeight)
    {
        PointerPosition = new Vector2(Verity.Input.Input.MousePosition.X, Verity.Input.Input.MousePosition.Y);
        foreach (var canvas in Active.ToArray())
            canvas.Update(viewportWidth, viewportHeight);
    }

    public static void Clear()
    {
        foreach (var canvas in Active)
            canvas.Close();
        Active.Clear();
        BindingSources.Clear();
    }

    public static Vector2 ViewportToCanvas(Vector2 viewportPosition, UIScreenAsset screen)
    {
        return new Vector2(viewportPosition.X * screen.ReferenceResolution.X, viewportPosition.Y * screen.ReferenceResolution.Y);
    }

    public static Vector2 CanvasToViewport(Vector2 canvasPosition, UIScreenAsset screen)
    {
        float width = Math.Max(1f, screen.ReferenceResolution.X);
        float height = Math.Max(1f, screen.ReferenceResolution.Y);
        return new Vector2(canvasPosition.X / width, canvasPosition.Y / height);
    }

    internal static UiScript? CreateUiScript(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(typeName, false, true) ?? assembly.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (type != null && !type.IsAbstract && typeof(UiScript).IsAssignableFrom(type))
                return Activator.CreateInstance(type) as UiScript;
        }
        return null;
    }

    internal static void InvokeActions(Canvas canvas, UiNode node, UiEvent evt)
    {
        foreach (var action in node.Events)
        {
            if (action.Trigger != evt.Type || string.IsNullOrWhiteSpace(action.Method))
                continue;
            foreach (var target in ResolveActionTargets(canvas, action.Target))
                TryInvokeTarget(target, action.Method, evt);
        }
    }

    private static IEnumerable<object> ResolveActionTargets(Canvas canvas, string? target)
    {
        if (string.IsNullOrWhiteSpace(target) || string.Equals(target, "self", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "ui", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "script", StringComparison.OrdinalIgnoreCase))
        {
            if (canvas.UiScript != null) yield return canvas.UiScript;
            foreach (var receiver in ExpandReceivers(canvas.OwnerEntity)) yield return receiver;
            yield break;
        }

        if (target.StartsWith("binding:", StringComparison.OrdinalIgnoreCase))
        {
            string key = target["binding:".Length..];
            if (TryResolveBindingSource(key, out var source))
                foreach (var receiver in ExpandReceivers(source)) yield return receiver;
            yield break;
        }

        if (target.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var receiver in ExpandReceivers(Entity.Find(target["entity:".Length..]))) yield return receiver;
            yield break;
        }

        if (target.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entity in Entity.FindEntitiesWithTag(target["tag:".Length..]))
            foreach (var receiver in ExpandReceivers(entity)) yield return receiver;
            yield break;
        }

        if (canvas.TryGetVariable(target, out var variable) && variable != null)
        {
            foreach (var receiver in ExpandReceivers(variable)) yield return receiver;
            yield break;
        }

        if (TryResolveBindingSource(target, out var directSource))
            foreach (var receiver in ExpandReceivers(directSource)) yield return receiver;
    }

    private static IEnumerable<object> ExpandReceivers(object? source)
    {
        if (source == null) yield break;
        if (source is Entity entity)
        {
            yield return entity;
            foreach (var component in entity.GetAllComponents()) yield return component;
            yield break;
        }
        yield return source;
    }

    private static void TryInvokeTarget(object target, string methodName, UiEvent evt)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in target.GetType().GetMethods(flags))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase) || !TryBuildArguments(method, evt, out object?[]? args))
                continue;
            object? result = method.Invoke(target, args);
            if (result is IEnumerator routine && target is Script script) script.StartCoroutine(routine);
            return;
        }
    }

    private static bool TryBuildArguments(MethodInfo method, UiEvent evt, out object?[]? args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0) { args = []; return true; }
        if (parameters.Length == 1 && TryResolveArgument(parameters[0].ParameterType, evt, out var arg)) { args = [arg]; return true; }
        if (parameters.Length == 2 && TryResolveArgument(parameters[0].ParameterType, evt, out var first) && TryResolveArgument(parameters[1].ParameterType, evt, out var second)) { args = [first, second]; return true; }
        args = null;
        return false;
    }

    private static bool TryResolveArgument(Type parameterType, UiEvent evt, out object? arg)
    {
        if (parameterType == typeof(UiEvent) || parameterType.IsAssignableFrom(typeof(UiEvent))) { arg = evt; return true; }
        if (evt.Node != null && parameterType.IsInstanceOfType(evt.Node)) { arg = evt.Node; return true; }
        if (parameterType == typeof(object)) { arg = evt.Value; return true; }
        if (evt.Value != null)
        {
            if (parameterType.IsInstanceOfType(evt.Value)) { arg = evt.Value; return true; }
            try
            {
                arg = parameterType.IsEnum && evt.Value is string text ? Enum.Parse(parameterType, text, true) : Convert.ChangeType(evt.Value, parameterType);
                return true;
            }
            catch { }
        }
        arg = null;
        return false;
    }
}
