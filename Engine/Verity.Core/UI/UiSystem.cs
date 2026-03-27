using System.Collections;
using System.Reflection;
using System.Text;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Core.UI;

public sealed class Canvas
{
    private string? _hoveredNodeId;
    private string? _pressedNodeId;
    private string? _focusedNodeId;
    private double _lastClickTime;

    public Entity? OwnerEntity { get; }
    public UIScreenAsset Screen { get; }
    public bool Visible { get; set; } = true;

    public Canvas(UIScreenAsset screen, Entity? ownerEntity = null)
    {
        OwnerEntity = ownerEntity;
        Screen = screen;
        Screen.RebindTree();
    }

    public T? Query<T>(string nameOrId) where T : UiNode => Screen.Root.Query<T>(nameOrId);
    public UiNode? Query(string nameOrId) => Screen.Root.Query(nameOrId);
    public void Bind(string path, object source) => UiSystem.Bind(path, source);

    public void Update(float viewportWidth, float viewportHeight)
    {
        if (!Visible)
            return;

        UiLayoutEngine.Layout(Screen, viewportWidth, viewportHeight);
        ApplyBindings();
        ProcessInput();
    }

    private void ApplyBindings()
    {
        foreach (var node in Screen.Root.DescendantsAndSelf())
        {
            foreach (var binding in node.Bindings)
            {
                if (string.IsNullOrWhiteSpace(binding.Path) || string.IsNullOrWhiteSpace(binding.TargetProperty))
                    continue;

                string sourceKey = binding.Path;
                string memberPath = string.Empty;
                int dot = binding.Path.IndexOf('.');
                if (dot >= 0)
                {
                    sourceKey = binding.Path[..dot];
                    memberPath = binding.Path[(dot + 1)..];
                }

                if (!UiSystem.TryResolveBindingSource(sourceKey, out var source))
                    continue;

                object? value = UiBindingRuntime.ResolvePath(source, memberPath);
                UiBindingRuntime.TrySetValue(node, binding.TargetProperty, value);
            }
        }
    }

    private void ProcessInput()
    {
        var pointer = UiSystem.PointerPosition;
        var hovered = HitTest(pointer);
        bool down = Verity.Input.Input.GetMouseButtonDown(Verity.Input.MouseButton.Left);
        bool held = Verity.Input.Input.GetMouseButton(Verity.Input.MouseButton.Left);
        bool up = Verity.Input.Input.GetMouseButtonUp(Verity.Input.MouseButton.Left);
        float scroll = Verity.Input.Input.ScrollDelta;

        if (hovered?.Id != _hoveredNodeId)
        {
            if (_hoveredNodeId != null)
                DispatchEvent(Query(_hoveredNodeId), new UiEvent { Type = UiEventType.PointerExit, Node = Query(_hoveredNodeId), Position = pointer });

            _hoveredNodeId = hovered?.Id;

            if (hovered != null)
                DispatchEvent(hovered, new UiEvent { Type = UiEventType.PointerEnter, Node = hovered, Position = pointer });
        }

        if (hovered != null && scroll != 0f)
        {
            DispatchEvent(hovered, new UiEvent
            {
                Type = UiEventType.Scroll,
                Node = hovered,
                Position = pointer,
                ScrollDelta = scroll
            });

            if (hovered is ScrollView scrollView)
                scrollView.ScrollOffset += new Vector2(0, scroll * 16f);
        }

        if (down && hovered != null)
        {
            _pressedNodeId = hovered.Id;
            hovered.RuntimeState |= UiStateFlags.Pressed | UiStateFlags.Focused;
            DispatchEvent(hovered, new UiEvent { Type = UiEventType.PointerDown, Node = hovered, Position = pointer });
        }

        if (held && _pressedNodeId != null)
        {
            var pressedNode = Query(_pressedNodeId);
            if (pressedNode is Slider slider)
            {
                float pct = Math.Clamp((pointer.X - slider.LayoutRect.X) / Math.Max(1f, slider.LayoutRect.Width), 0f, 1f);
                float nextValue = slider.Min + ((slider.Max - slider.Min) * pct);
                if (Math.Abs(nextValue - slider.Value) > 0.0001f)
                {
                    slider.Value = nextValue;
                    DispatchEvent(slider, new UiEvent { Type = UiEventType.ValueChanged, Node = slider, Position = pointer, Value = nextValue });
                }
            }
        }

        if (up)
        {
            var pressedNode = _pressedNodeId != null ? Query(_pressedNodeId) : null;
            if (pressedNode != null)
            {
                pressedNode.RuntimeState &= ~UiStateFlags.Pressed;
                DispatchEvent(pressedNode, new UiEvent { Type = UiEventType.PointerUp, Node = pressedNode, Position = pointer });
            }

            if (pressedNode != null && hovered?.Id == pressedNode.Id)
            {
                double now = Time.TotalTime;
                UiEventType clickType = now - _lastClickTime < 0.28 ? UiEventType.DoubleClick : UiEventType.Click;
                _lastClickTime = now;
                DispatchEvent(pressedNode, new UiEvent { Type = clickType, Node = pressedNode, Position = pointer });

                if (pressedNode is InputField or TextArea)
                    SetFocusedNode(pressedNode, pointer);
                else if (pressedNode is not Slider)
                    SetFocusedNode(null, pointer);

                if (pressedNode is Toggle toggle)
                {
                    toggle.IsChecked = !toggle.IsChecked;
                    toggle.RuntimeState = toggle.IsChecked
                        ? toggle.RuntimeState | UiStateFlags.Checked
                        : toggle.RuntimeState & ~UiStateFlags.Checked;
                    DispatchEvent(toggle, new UiEvent { Type = UiEventType.ValueChanged, Node = toggle, Position = pointer, Value = toggle.IsChecked });
                }
                else if (pressedNode is Dropdown dropdown)
                {
                    dropdown.Expanded = !dropdown.Expanded;
                    dropdown.RuntimeState = dropdown.Expanded
                        ? dropdown.RuntimeState | UiStateFlags.Expanded
                        : dropdown.RuntimeState & ~UiStateFlags.Expanded;
                }
            }

            _pressedNodeId = null;
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
        if (node?.Id == _focusedNodeId)
            return;

        var previous = _focusedNodeId != null ? Query(_focusedNodeId) : null;
        if (previous != null)
        {
            previous.RuntimeState &= ~UiStateFlags.Focused;
            DispatchEvent(previous, new UiEvent { Type = UiEventType.FocusChanged, Node = previous, Position = pointer, Value = false });
        }

        _focusedNodeId = node?.Id;
        if (node != null)
        {
            node.RuntimeState |= UiStateFlags.Focused;
            DispatchEvent(node, new UiEvent { Type = UiEventType.FocusChanged, Node = node, Position = pointer, Value = true });
        }
    }

    private void ProcessFocusedInput(Vector2 pointer)
    {
        if (_focusedNodeId == null)
            return;

        var focused = Query(_focusedNodeId);
        if (focused == null)
            return;

        if (focused is not InputField && focused is not TextArea)
            return;

        string appended = ReadTypedText();
        if (!string.IsNullOrEmpty(appended))
        {
            if (focused is InputField inputField)
            {
                inputField.Value += appended;
                DispatchEvent(inputField, new UiEvent { Type = UiEventType.ValueChanged, Node = inputField, Position = pointer, Value = inputField.Value });
            }
            else if (focused is TextArea textArea)
            {
                textArea.Value += appended;
                DispatchEvent(textArea, new UiEvent { Type = UiEventType.ValueChanged, Node = textArea, Position = pointer, Value = textArea.Value });
            }
        }

        if (Verity.Input.Input.GetKeyDown(Verity.Input.KeyCode.Backspace))
        {
            if (focused is InputField inputField && inputField.Value.Length > 0)
            {
                inputField.Value = inputField.Value[..^1];
                DispatchEvent(inputField, new UiEvent { Type = UiEventType.ValueChanged, Node = inputField, Position = pointer, Value = inputField.Value });
            }
            else if (focused is TextArea textArea && textArea.Value.Length > 0)
            {
                textArea.Value = textArea.Value[..^1];
                DispatchEvent(textArea, new UiEvent { Type = UiEventType.ValueChanged, Node = textArea, Position = pointer, Value = textArea.Value });
            }
        }

        if (Verity.Input.Input.GetKeyDown(Verity.Input.KeyCode.Return))
        {
            if (focused is TextArea textArea)
            {
                textArea.Value += Environment.NewLine;
                DispatchEvent(textArea, new UiEvent { Type = UiEventType.ValueChanged, Node = textArea, Position = pointer, Value = textArea.Value });
            }
            else
            {
                DispatchEvent(focused, new UiEvent { Type = UiEventType.Submit, Node = focused, Position = pointer });
            }
        }
    }

    private static string ReadTypedText()
    {
        var builder = new StringBuilder();
        bool shift = Verity.Input.Input.GetKey(Verity.Input.KeyCode.LeftShift) || Verity.Input.Input.GetKey(Verity.Input.KeyCode.RightShift);

        for (int i = 0; i < 26; i++)
        {
            var key = (Verity.Input.KeyCode)((int)Verity.Input.KeyCode.A + i);
            if (!Verity.Input.Input.GetKeyDown(key))
                continue;

            char ch = (char)('a' + i);
            builder.Append(shift ? char.ToUpperInvariant(ch) : ch);
        }

        for (int i = 0; i <= 9; i++)
        {
            var key = (Verity.Input.KeyCode)((int)Verity.Input.KeyCode.Alpha0 + i);
            if (Verity.Input.Input.GetKeyDown(key))
                builder.Append((char)('0' + i));
        }

        if (Verity.Input.Input.GetKeyDown(Verity.Input.KeyCode.Space))
            builder.Append(' ');

        return builder.ToString();
    }

    private void ApplyTwoWayBindings(UiNode node, object? value)
    {
        foreach (var binding in node.Bindings)
        {
            if (binding.Mode != UiBindingMode.TwoWay ||
                string.IsNullOrWhiteSpace(binding.Path) ||
                string.IsNullOrWhiteSpace(binding.TargetProperty))
            {
                continue;
            }

            string sourceKey = binding.Path;
            string memberPath = string.Empty;
            int dot = binding.Path.IndexOf('.');
            if (dot >= 0)
            {
                sourceKey = binding.Path[..dot];
                memberPath = binding.Path[(dot + 1)..];
            }

            if (string.IsNullOrWhiteSpace(memberPath))
                continue;

            if (!UiSystem.TryResolveBindingSource(sourceKey, out var source))
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
            if (!node.Active || !node.Visible || !node.Interactable)
                continue;

            if (!node.LayoutRect.Contains(point))
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
        var current = node.Parent;
        while (current != null)
        {
            depth++;
            current = current.Parent;
        }

        return depth;
    }
}

public static class UiLayoutEngine
{
    public static void Layout(UIScreenAsset screen, float viewportWidth, float viewportHeight)
    {
        var rootRect = new UiRect(0, 0, screen.ReferenceResolution.X, screen.ReferenceResolution.Y);
        LayoutNode(screen.Root, rootRect);
    }

    private static void LayoutNode(UiNode node, UiRect parentRect)
    {
        node.LayoutRect = ResolveRect(node.Transform, parentRect);

        if (node is UiContainer container && container.Layout.Mode != UiLayoutMode.None)
            ApplyContainerLayout(container);

        foreach (var child in node.Children)
            LayoutNode(child, node.LayoutRect);
    }

    private static UiRect ResolveRect(UiTransform transform, UiRect parent)
    {
        var anchorMinPos = new Vector2(parent.X + (parent.Width * transform.AnchorMin.X), parent.Y + (parent.Height * transform.AnchorMin.Y));
        var anchorMaxPos = new Vector2(parent.X + (parent.Width * transform.AnchorMax.X), parent.Y + (parent.Height * transform.AnchorMax.Y));
        var margin = transform.Margin;

        if (transform.AnchorMin != transform.AnchorMax)
        {
            float x = anchorMinPos.X + transform.Position.X + margin.X;
            float y = anchorMinPos.Y + transform.Position.Y + margin.Y;
            float width = (anchorMaxPos.X - anchorMinPos.X) + transform.Size.X - margin.X - margin.Z;
            float height = (anchorMaxPos.Y - anchorMinPos.Y) + transform.Size.Y - margin.Y - margin.W;
            width = Math.Clamp(width, transform.MinSize.X, transform.MaxSize.X);
            height = Math.Clamp(height, transform.MinSize.Y, transform.MaxSize.Y);
            return new UiRect(x, y, width, height);
        }

        float w = Math.Clamp(transform.Size.X, transform.MinSize.X, transform.MaxSize.X);
        float h = Math.Clamp(transform.Size.Y, transform.MinSize.Y, transform.MaxSize.Y);
        float px = anchorMinPos.X + transform.Position.X - (w * transform.Pivot.X);
        float py = anchorMinPos.Y + transform.Position.Y - (h * transform.Pivot.Y);
        return new UiRect(px, py, w, h);
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
            case UiLayoutMode.HorizontalStack:
                foreach (var child in container.Children)
                {
                    child.Transform.AnchorMin = Vector2.Zero;
                    child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = Vector2.Zero;
                    child.Transform.Position = new Vector2(x - rect.X, y - rect.Y);
                    if (container.Layout.FitChildren)
                        child.Transform.Size = new Vector2(Math.Max(child.Transform.Size.X, 96f), availableHeight);
                    x += child.Transform.Size.X + container.Layout.Spacing.X;
                }
                break;

            case UiLayoutMode.VerticalStack:
            case UiLayoutMode.ScrollContent:
                foreach (var child in container.Children)
                {
                    child.Transform.AnchorMin = Vector2.Zero;
                    child.Transform.AnchorMax = Vector2.Zero;
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
                for (int i = 0; i < container.Children.Count; i++)
                {
                    var child = container.Children[i];
                    int col = i % cols;
                    int row = i / cols;
                    child.Transform.AnchorMin = Vector2.Zero;
                    child.Transform.AnchorMax = Vector2.Zero;
                    child.Transform.Pivot = Vector2.Zero;
                    child.Transform.Position = new Vector2(
                        pad.X + (col * (cellWidth + container.Layout.Spacing.X)),
                        pad.Y + (row * (child.Transform.Size.Y + container.Layout.Spacing.Y)));
                    if (container.Layout.FitChildren)
                        child.Transform.Size = new Vector2(cellWidth, child.Transform.Size.Y);
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
    public static Vector2 PointerPosition { get; private set; }
    public static IReadOnlyList<Canvas> ActiveCanvases => Active;
    public static UIScreenAsset Load(string path) => UiSerializer.Load(path);
    public static string ResolveAssetPath(string path, string? guid = null) => AssetPathUtility.ResolvePath(AssetsRoot ?? AppContext.BaseDirectory, path, guid);
    public static UIScreenAsset LoadAsset(string path, string? guid = null) => UiSerializer.Load(ResolveAssetPath(path, guid));

    public static Canvas ShowScreen(UIScreenAsset screen, Entity? ownerEntity = null)
    {
        var canvas = new Canvas(screen, ownerEntity);
        Active.Add(canvas);
        Active.Sort((a, b) => a.Screen.SortingOrder.CompareTo(b.Screen.SortingOrder));
        return canvas;
    }

    public static void HideScreen(string id)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Active[i].Screen.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Active[i].Screen.Name, id, StringComparison.OrdinalIgnoreCase))
            {
                Active.RemoveAt(i);
            }
        }
    }

    public static void HideCanvas(Canvas canvas)
    {
        Active.Remove(canvas);
    }

    public static Canvas? FindCanvas(string screenNameOrId)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var canvas = Active[i];
            if (string.Equals(canvas.Screen.Id, screenNameOrId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(canvas.Screen.Name, screenNameOrId, StringComparison.OrdinalIgnoreCase))
            {
                return canvas;
            }
        }

        return null;
    }

    public static T? Query<T>(string nameOrId) where T : UiNode
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var found = Active[i].Query<T>(nameOrId);
            if (found != null)
                return found;
        }

        return null;
    }

    public static UiNode? Query(string nameOrId)
    {
        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var found = Active[i].Query(nameOrId);
            if (found != null)
                return found;
        }

        return null;
    }

    public static void Bind(string path, object source)
    {
        if (string.IsNullOrWhiteSpace(path) || source == null)
            return;

        BindingSources[path] = source;
    }

    public static void Unbind(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        BindingSources.Remove(path);
    }

    public static bool TryResolveBindingSource(string key, out object source) => BindingSources.TryGetValue(key, out source!);

    public static void Update(float viewportWidth, float viewportHeight)
    {
        PointerPosition = new Vector2(Verity.Input.Input.MousePosition.X, Verity.Input.Input.MousePosition.Y);
        foreach (var canvas in Active)
            canvas.Update(viewportWidth, viewportHeight);
    }

    public static void Clear()
    {
        Active.Clear();
        BindingSources.Clear();
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
        if (string.IsNullOrWhiteSpace(target) ||
            string.Equals(target, "self", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "owner", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var receiver in ExpandReceivers(canvas.OwnerEntity))
                yield return receiver;
            yield break;
        }

        if (target.StartsWith("binding:", StringComparison.OrdinalIgnoreCase))
        {
            string key = target["binding:".Length..];
            if (TryResolveBindingSource(key, out var source))
            {
                foreach (var receiver in ExpandReceivers(source))
                    yield return receiver;
            }
            yield break;
        }

        if (target.StartsWith("entity:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var receiver in ExpandReceivers(Entity.Find(target["entity:".Length..])))
                yield return receiver;
            yield break;
        }

        if (target.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entity in Entity.FindEntitiesWithTag(target["tag:".Length..]))
            {
                foreach (var receiver in ExpandReceivers(entity))
                    yield return receiver;
            }
            yield break;
        }

        if (TryResolveBindingSource(target, out var directSource))
        {
            foreach (var receiver in ExpandReceivers(directSource))
                yield return receiver;
        }
    }

    private static IEnumerable<object> ExpandReceivers(object? source)
    {
        if (source == null)
            yield break;

        if (source is Entity entity)
        {
            yield return entity;
            foreach (var component in entity.GetAllComponents())
                yield return component;
            yield break;
        }

        yield return source;
    }

    private static void TryInvokeTarget(object target, string methodName, UiEvent evt)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var method in target.GetType().GetMethods(flags))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryBuildArguments(method, evt, out object?[]? args))
                continue;

            object? result = method.Invoke(target, args);
            if (result is IEnumerator routine && target is Script script)
                script.StartCoroutine(routine);
            return;
        }
    }

    private static bool TryBuildArguments(MethodInfo method, UiEvent evt, out object?[]? args)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            args = [];
            return true;
        }

        if (parameters.Length == 1 && TryResolveArgument(parameters[0].ParameterType, evt, out var arg))
        {
            args = [arg];
            return true;
        }

        if (parameters.Length == 2 &&
            TryResolveArgument(parameters[0].ParameterType, evt, out var first) &&
            TryResolveArgument(parameters[1].ParameterType, evt, out var second))
        {
            args = [first, second];
            return true;
        }

        args = null;
        return false;
    }

    private static bool TryResolveArgument(Type parameterType, UiEvent evt, out object? arg)
    {
        if (parameterType == typeof(UiEvent) || parameterType.IsAssignableFrom(typeof(UiEvent)))
        {
            arg = evt;
            return true;
        }

        if (evt.Node != null && parameterType.IsInstanceOfType(evt.Node))
        {
            arg = evt.Node;
            return true;
        }

        if (parameterType == typeof(object))
        {
            arg = evt.Value;
            return true;
        }

        if (evt.Value != null)
        {
            if (parameterType.IsInstanceOfType(evt.Value))
            {
                arg = evt.Value;
                return true;
            }

            try
            {
                if (parameterType.IsEnum && evt.Value is string enumText)
                {
                    arg = Enum.Parse(parameterType, enumText, true);
                    return true;
                }

                arg = Convert.ChangeType(evt.Value, parameterType);
                return true;
            }
            catch
            {
            }
        }

        arg = null;
        return false;
    }
}

