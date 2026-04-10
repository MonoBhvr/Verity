using System.Numerics;
using System.Text.Json;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.UI;

namespace Verity.Editor.Windows;

public sealed unsafe class UIEditorWindow : EditorWindow
{
    private sealed class UiEditorUndoSnapshot
    {
        public string ScreenJson { get; init; } = string.Empty;
        public string? SelectedNodeId { get; init; }
        public int ResolutionPreset { get; init; }
    }

    private enum CanvasTool
    {
        Move,
        Scale,
        Rotate
    }

    private enum CanvasDragMode
    {
        None,
        Move,
        Resize,
        Rotate
    }

    private readonly EditorApp _app;
    private string? _assetPath;
    private UIScreenAsset? _screen;
    private UiNode? _selectedNode;
    private int _resolutionPreset;
    private Vector2 _canvasPan = Vector2.Zero;
    private float _canvasZoom = 1.15f;
    private bool _frameCanvasRequested = true;
    private CanvasTool _activeCanvasTool = CanvasTool.Move;
    private CanvasDragMode _canvasDragMode;
    private int _activeResizeHandle = -1;
    private Vector2 _dragStartMouseCanvas;
    private UiRect _dragStartRect;
    private Vector2 _dragStartPosition;
    private Vector2 _dragStartSize;
    private float _dragStartRotation;
    private float _dragStartAngle;
    private readonly Stack<UiEditorUndoSnapshot> _undoStack = new();
    private readonly Stack<UiEditorUndoSnapshot> _redoStack = new();
    private string _lastCommittedScreenJson = string.Empty;
    private bool _restoringUndo;
    private const int MaxUndoHistory = 100;

    private readonly (string LabelKey, Vector2 Size)[] _presets =
    [
        ("ui_preset_16_9", new Vector2(1920, 1080)),
        ("ui_preset_19_5_9", new Vector2(1170, 540)),
        ("ui_preset_4_3", new Vector2(1024, 768)),
        ("ui_preset_tablet", new Vector2(1280, 800))
    ];

    private readonly (UiNodeKind Kind, string LabelKey, Vector4 Accent)[] _paletteEntries =
    [
        (UiNodeKind.Container, "ui_node_container", new Vector4(0.21f, 0.51f, 0.96f, 1f)),
        (UiNodeKind.Panel, "ui_node_panel", new Vector4(0.17f, 0.40f, 0.80f, 1f)),
        (UiNodeKind.Label, "ui_node_label", new Vector4(0.96f, 0.64f, 0.18f, 1f)),
        (UiNodeKind.Image, "ui_node_image", new Vector4(0.87f, 0.34f, 0.50f, 1f)),
        (UiNodeKind.Button, "ui_node_button", new Vector4(0.14f, 0.73f, 0.56f, 1f)),
        (UiNodeKind.Toggle, "ui_node_toggle", new Vector4(0.20f, 0.75f, 0.77f, 1f)),
        (UiNodeKind.InputField, "ui_node_input_field", new Vector4(0.52f, 0.45f, 0.96f, 1f)),
        (UiNodeKind.TextArea, "ui_node_text_area", new Vector4(0.60f, 0.44f, 0.87f, 1f)),
        (UiNodeKind.Slider, "ui_node_slider", new Vector4(0.32f, 0.70f, 0.33f, 1f)),
        (UiNodeKind.ProgressBar, "ui_node_progress_bar", new Vector4(0.52f, 0.70f, 0.20f, 1f)),
        (UiNodeKind.ScrollView, "ui_node_scroll_view", new Vector4(0.86f, 0.50f, 0.16f, 1f)),
        (UiNodeKind.DynamicArea, "ui_node_dynamic_area", new Vector4(0.76f, 0.37f, 0.78f, 1f)),
        (UiNodeKind.Spacer, "ui_node_spacer", new Vector4(0.48f, 0.48f, 0.48f, 1f))
    ];

    public bool OverlayEnabled { get; set; } = true;
    public UIScreenAsset? PreviewScreen => _screen;

    public UIEditorWindow(EditorApp app) : base(L10n.Tr("window_ui_editor"))
    {
        _app = app;
    }

    private static string WithId(string label, string id)
    {
        return $"{label}##{id}";
    }

    private static string TrId(string key, string id)
    {
        return WithId(L10n.Tr(key), id);
    }

    public override void OnGui()
    {
        TryLoadSelectedAsset();
        if (_screen == null || string.IsNullOrWhiteSpace(_assetPath))
        {
            ImGui.TextDisabled(L10n.Tr("msg_select_ui_asset_to_edit_here"));
            return;
        }

        HandleCanvasShortcuts();
        DrawToolbar();
        DrawAddNodePopup();

        var avail = ImGui.GetContentRegionAvail();
        float leftWidth = 210f;
        float rightWidth = 280f;
        float gap = 8f;
        float centerWidth = Math.Max(320f, avail.X - leftWidth - rightWidth - gap * 2f);

        if (ImGui.BeginChild("UiHierarchy", new Vector2(leftWidth, avail.Y), ImGuiChildFlags.Borders))
            DrawHierarchy(_screen.Root);
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("UiCanvas", new Vector2(centerWidth, avail.Y), ImGuiChildFlags.Borders))
            DrawCanvasPreview();
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("UiInspector", new Vector2(rightWidth, avail.Y), ImGuiChildFlags.Borders))
            DrawInspector();
        ImGui.EndChild();
    }

    private void TryLoadSelectedAsset()
    {
        string? path = EditorSelection.SelectedAssetPath;
        if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".ui", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.Equals(path, _assetPath, StringComparison.OrdinalIgnoreCase) && _screen != null)
            return;

        _assetPath = path;
        _screen = UiSerializer.Load(path);
        _selectedNode = _screen.Root;
        _lastCommittedScreenJson = JsonSerializer.Serialize(_screen, UiSerializer.Options);
        _undoStack.Clear();
        _redoStack.Clear();
        ResetCanvasView();
        IsOpen = true;
    }

    private void DrawToolbar()
    {
        if (ImGui.Button(TrId("btn_save", "ToolbarSave"), new Vector2(76, 0)))
            Save();

        ImGui.SameLine();
        if (ImGui.Button(TrId("ui_btn_add_node", "ToolbarAddNode"), new Vector2(104, 0)))
            ImGui.OpenPopup("UiAddNodePopup");

        ImGui.SameLine();
        bool canDelete = _selectedNode != null && _screen != null && _selectedNode != _screen.Root;
        if (!canDelete)
            ImGui.BeginDisabled();
        if (ImGui.Button(TrId("btn_delete", "ToolbarDelete"), new Vector2(76, 0)) && canDelete)
            DeleteSelected();
        if (!canDelete)
            ImGui.EndDisabled();

        ImGui.SameLine();
        bool canSavePrefab = _selectedNode != null;
        if (!canSavePrefab)
            ImGui.BeginDisabled();
        if (ImGui.Button(TrId("ui_btn_save_prefab", "ToolbarSavePrefab"), new Vector2(112, 0)) && canSavePrefab)
            SaveSelectedAsPrefab();
        if (!canSavePrefab)
            ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button(TrId("ui_btn_frame_view", "ToolbarFrameView"), new Vector2(84, 0)))
            ResetCanvasView();

        ImGui.SameLine();
        DrawToolButton("Move", CanvasTool.Move, new Vector2(56f, 0f));
        ImGui.SameLine();
        DrawToolButton("Scale", CanvasTool.Scale, new Vector2(56f, 0f));
        ImGui.SameLine();
        DrawToolButton("Rotate", CanvasTool.Rotate, new Vector2(60f, 0f));

        ImGui.SameLine();
        if (ImGui.Button("-", new Vector2(24, 0)))
            _canvasZoom = Math.Clamp(_canvasZoom / 1.12f, 0.1f, 12f);

        ImGui.SameLine(0, 4);
        ImGui.TextDisabled(L10n.Tr("ui_label_zoom", _canvasZoom.ToString("F2")));

        ImGui.SameLine(0, 4);
        if (ImGui.Button("+", new Vector2(24, 0)))
            _canvasZoom = Math.Clamp(_canvasZoom * 1.12f, 0.1f, 12f);

        ImGui.SameLine();
        bool overlayEnabled = OverlayEnabled;
        if (ImGui.Checkbox(TrId("label_overlay", "ToolbarOverlay"), ref overlayEnabled))
            OverlayEnabled = overlayEnabled;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(140f);
        if (ImGui.BeginCombo(TrId("label_preview", "ToolbarPreviewPreset"), GetPresetLabel(_resolutionPreset)))
        {
            for (int i = 0; i < _presets.Length; i++)
            {
                bool selected = i == _resolutionPreset;
                if (ImGui.Selectable(GetPresetLabel(i), selected))
                {
                    _resolutionPreset = i;
                    _screen!.ReferenceResolution = _presets[i].Size;
                    ResetCanvasView();
                    Save();
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.Separator();
    }

    private void DrawAddNodePopup()
    {
        if (!ImGui.BeginPopup("UiAddNodePopup"))
            return;

        ImGui.TextDisabled(L10n.Tr("ui_msg_add_node_hint"));
        ImGui.Separator();

        float itemWidth = 148f;
        int column = 0;
        for (int i = 0; i < _paletteEntries.Length; i++)
        {
            var entry = _paletteEntries[i];
            DrawPaletteButton(entry.Kind, L10n.Tr(entry.LabelKey), entry.Accent, itemWidth);
            column++;
            if (column < 3 && i < _paletteEntries.Length - 1)
                ImGui.SameLine();
            else
                column = 0;
        }

        ImGui.EndPopup();
    }

    private void DrawPaletteButton(UiNodeKind kind, string label, Vector4 accent, float width)
    {
        var hovered = new Vector4(MathF.Min(1f, accent.X + 0.08f), MathF.Min(1f, accent.Y + 0.08f), MathF.Min(1f, accent.Z + 0.08f), 0.95f);
        var active = new Vector4(accent.X * 0.9f, accent.Y * 0.9f, accent.Z * 0.9f, 0.95f);

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(accent.X, accent.Y, accent.Z, 0.90f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f);
        if (ImGui.Button(WithId(label, $"Palette{kind}"), new Vector2(width, 34f)))
        {
            AddNode(kind);
            ImGui.CloseCurrentPopup();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(3);
    }

    private void DrawHierarchy(UiNode node)
    {
        ImGui.PushID(node.Id);
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_selectedNode == node)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        bool open = ImGui.TreeNodeEx($"{node.Name} ({GetNodeKindLabel(node.Kind)})", flags);
        if (ImGui.IsItemClicked())
            _selectedNode = node;

        if (open)
        {
            if (node is DynamicArea area && area.ItemTemplate != null)
                DrawTemplateHierarchy(area.ItemTemplate);

            foreach (var child in node.Children)
                DrawHierarchy(child);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private void DrawTemplateHierarchy(UiNode node)
    {
        ImGui.PushID($"template-{node.Id}");
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_selectedNode == node)
            flags |= ImGuiTreeNodeFlags.Selected;
        if (node.Children.Count == 0)
            flags |= ImGuiTreeNodeFlags.Leaf;

        bool open = ImGui.TreeNodeEx($"{L10n.Tr("ui_hierarchy_template_prefix")} {node.Name} ({GetNodeKindLabel(node.Kind)})", flags);
        if (ImGui.IsItemClicked())
            _selectedNode = node;

        if (open)
        {
            foreach (var child in node.Children)
                DrawTemplateHierarchy(child);
            ImGui.TreePop();
        }

        ImGui.PopID();
    }

    private void DrawCanvasPreview()
    {
        if (_screen == null)
            return;

        var avail = ImGui.GetContentRegionAvail();
        if (avail.X <= 0 || avail.Y <= 0)
            return;

        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##ui-canvas-hit", new Vector2(MathF.Max(1f, avail.X), MathF.Max(1f, avail.Y)));
        bool hovered = ImGui.IsItemHovered();
        bool clicked = ImGui.IsItemClicked(ImGuiMouseButton.Left);

        float fitScale = ComputeFitScale(avail);
        if (_frameCanvasRequested)
        {
            _canvasZoom = 1.15f;
            _canvasPan = Vector2.Zero;
            _frameCanvasRequested = false;
        }

        HandleCanvasNavigation(origin, avail, hovered, fitScale);

        float scale = MathF.Max(0.0001f, fitScale * _canvasZoom);
        Vector2 canvasSize = new(_screen.ReferenceResolution.X * scale, _screen.ReferenceResolution.Y * scale);
        Vector2 center = origin + (avail * 0.5f);
        Vector2 canvasPos = center - (canvasSize * 0.5f) + _canvasPan;

        var draw = ImGui.GetWindowDrawList();
        DrawCanvasBackdrop(draw, origin, avail, canvasPos, canvasSize, scale);

        UiLayoutEngine.Layout(_screen, _screen.ReferenceResolution.X, _screen.ReferenceResolution.Y);
        foreach (var node in _screen.Root.DescendantsAndSelf().Where(n => n.Visible && n.Active))
            DrawNodePreview(node, draw, canvasPos, scale);

        HandleCanvasEditing(origin, avail, canvasPos, scale, hovered);
        DrawSelectionGizmo(draw, canvasPos, scale);

        DrawCanvasOverlay(draw, origin, avail);

        if (clicked && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            Vector2 mouse = ImGui.GetIO().MousePos;
            Vector2 local = (mouse - canvasPos) / scale;
            _selectedNode = HitTest(local) ?? _screen.Root;
        }
    }

    private void HandleCanvasNavigation(Vector2 origin, Vector2 avail, bool hovered, float fitScale)
    {
        if (_screen == null || !hovered)
            return;

        var io = ImGui.GetIO();
        Vector2 center = origin + (avail * 0.5f);
        float currentScale = MathF.Max(0.0001f, fitScale * _canvasZoom);
        Vector2 currentCanvasSize = new(_screen.ReferenceResolution.X * currentScale, _screen.ReferenceResolution.Y * currentScale);
        Vector2 currentCanvasPos = center - (currentCanvasSize * 0.5f) + _canvasPan;

        if (io.MouseWheel != 0f)
        {
            Vector2 mouse = io.MousePos;
            Vector2 localBeforeZoom = (mouse - currentCanvasPos) / currentScale;
            float zoomFactor = 1.0f + io.MouseWheel * 0.1f;
            if (zoomFactor <= 0.01f)
                zoomFactor = 0.01f;

            _canvasZoom = Math.Clamp(_canvasZoom * zoomFactor, 0.1f, 12f);

            float nextScale = MathF.Max(0.0001f, fitScale * _canvasZoom);
            Vector2 nextCanvasSize = new(_screen.ReferenceResolution.X * nextScale, _screen.ReferenceResolution.Y * nextScale);
            Vector2 basePos = center - (nextCanvasSize * 0.5f);
            _canvasPan = mouse - (localBeforeZoom * nextScale) - basePos;
        }

        if (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right))
            _canvasPan = new Vector2(_canvasPan.X + io.MouseDelta.X, _canvasPan.Y + io.MouseDelta.Y);
    }

    private void DrawCanvasBackdrop(ImDrawListPtr draw, Vector2 origin, Vector2 avail, Vector2 canvasPos, Vector2 canvasSize, float scale)
    {
        draw.AddRectFilled(origin, origin + avail, ImGui.GetColorU32(new Vector4(0.055f, 0.06f, 0.075f, 1f)));

        Vector2 canvasMin = canvasPos;
        Vector2 canvasMax = canvasPos + canvasSize;
        draw.AddRectFilled(canvasMin, canvasMax, ImGui.GetColorU32(new Vector4(0.115f, 0.125f, 0.15f, 1f)));

        float majorStep = MathF.Max(32f, 64f * scale);
        float minorStep = MathF.Max(16f, 16f * scale);

        DrawCanvasGrid(draw, canvasMin, canvasMax, minorStep, new Vector4(1f, 1f, 1f, 0.035f));
        DrawCanvasGrid(draw, canvasMin, canvasMax, majorStep, new Vector4(1f, 1f, 1f, 0.07f));

        Vector2 canvasCenter = canvasMin + (canvasSize * 0.5f);
        draw.AddLine(new Vector2(canvasMin.X, canvasCenter.Y), new Vector2(canvasMax.X, canvasCenter.Y), ImGui.GetColorU32(new Vector4(0.33f, 0.48f, 0.84f, 0.30f)));
        draw.AddLine(new Vector2(canvasCenter.X, canvasMin.Y), new Vector2(canvasCenter.X, canvasMax.Y), ImGui.GetColorU32(new Vector4(0.84f, 0.44f, 0.33f, 0.30f)));
        draw.AddRect(canvasMin, canvasMax, ImGui.GetColorU32(new Vector4(0.38f, 0.43f, 0.52f, 0.85f)), 0f, ImDrawFlags.None, 2f);
    }

    private static void DrawCanvasGrid(ImDrawListPtr draw, Vector2 min, Vector2 max, float step, Vector4 color)
    {
        if (step <= 0f || max.X <= min.X || max.Y <= min.Y)
            return;

        uint lineColor = ImGui.GetColorU32(color);
        for (float x = min.X + step; x < max.X; x += step)
            draw.AddLine(new Vector2(x, min.Y), new Vector2(x, max.Y), lineColor);

        for (float y = min.Y + step; y < max.Y; y += step)
            draw.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), lineColor);
    }

    private void DrawCanvasOverlay(ImDrawListPtr draw, Vector2 origin, Vector2 avail)
    {
        string toolName = _activeCanvasTool switch
        {
            CanvasTool.Move => "Move",
            CanvasTool.Scale => "Scale",
            CanvasTool.Rotate => "Rotate",
            _ => "Move"
        };
        string info = $"{(int)_screen!.ReferenceResolution.X} x {(int)_screen.ReferenceResolution.Y}  |  {L10n.Tr("ui_label_zoom", _canvasZoom.ToString("F2"))}  |  {toolName}";
        draw.AddText(origin + new Vector2(12f, 10f), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.85f)), info);

        Vector2 hintPos = new(origin.X + 12f, origin.Y + avail.Y - 22f);
        draw.AddText(hintPos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)), L10n.Tr("ui_msg_canvas_controls"));
    }

    private void DrawNodePreview(UiNode node, ImDrawListPtr draw, Vector2 canvasPos, float scale)
    {
        var rect = node.LayoutRect;
        Vector2[] corners = GetNodeScreenCorners(node, canvasPos, scale);
        Vector2 min = GetMin(corners);
        Vector2 max = GetMax(corners);
        Vector2 size = max - min;
        if (size.X <= 0f || size.Y <= 0f)
            return;

        var fill = new Vector4(node.Visual.BackgroundColor.R, node.Visual.BackgroundColor.G, node.Visual.BackgroundColor.B, Math.Max(0.10f, node.Visual.BackgroundColor.A));
        var border = _selectedNode == node
            ? new Vector4(0.23f, 0.78f, 1f, 1f)
            : new Vector4(node.Visual.BorderColor.R, node.Visual.BorderColor.G, node.Visual.BorderColor.B, Math.Max(0.35f, node.Visual.BorderColor.A));

        if (_selectedNode == node)
            draw.AddQuad(corners[0], corners[1], corners[2], corners[3], ImGui.GetColorU32(new Vector4(0.15f, 0.65f, 1f, 0.30f)), 3f);

        draw.AddQuadFilled(corners[0], corners[1], corners[2], corners[3], ImGui.GetColorU32(fill));
        draw.AddQuad(corners[0], corners[1], corners[2], corners[3], ImGui.GetColorU32(border), _selectedNode == node ? 2f : 1f);

        string label = node switch
        {
            Button button => Coerce(button.Text),
            Label text => Coerce(text.Text),
            Dropdown dropdown => dropdown.Options.Count > 0 ? Coerce(dropdown.Options[Math.Clamp(dropdown.SelectedIndex, 0, dropdown.Options.Count - 1)]) : L10n.Tr("ui_default_dropdown"),
            Toggle toggle => Coerce(toggle.Text),
            Window window => Coerce(window.Title),
            Tooltip tooltip => Coerce(tooltip.Text),
            InputField input => string.IsNullOrWhiteSpace(input.Value) ? Coerce(input.Placeholder) : Coerce(input.Value),
            TextArea area => string.IsNullOrWhiteSpace(area.Value) ? Coerce(area.Placeholder) : Coerce(area.Value),
            _ => string.Empty
        };

        if (!string.IsNullOrWhiteSpace(label))
        {
            float previewFontSize = ResolvePreviewFontSize(node, scale);
            Vector2 textPos = TransformLocalPoint(node, canvasPos, scale, new Vector2(8f, 8f));
            draw.AddText(
                null,
                previewFontSize,
                textPos,
                ImGui.GetColorU32(new Vector4(node.Visual.ForegroundColor.R, node.Visual.ForegroundColor.G, node.Visual.ForegroundColor.B, 1f)),
                label);
        }
    }

    private static float ResolvePreviewFontSize(UiNode node, float scale)
    {
        float baseSize = node switch
        {
            TextNode textNode when textNode.FontSize > 0f => textNode.FontSize,
            _ when node.Visual.FontSize > 0f => node.Visual.FontSize,
            _ => 16f
        };

        return Math.Clamp(baseSize * scale, 10f, 72f);
    }

    private UiNode? HitTest(Vector2 point)
    {
        if (_screen == null)
            return null;

        UiNode? best = null;
        int depth = int.MinValue;
        foreach (var node in _screen.Root.DescendantsAndSelf())
        {
            if (!node.Visible || !node.Active)
                continue;
            if (!ContainsCanvasPoint(node, point))
                continue;
            int nodeDepth = node.Transform.ZOrder;
            if (nodeDepth >= depth)
            {
                depth = nodeDepth;
                best = node;
            }
        }

        return best;
    }

    private void DrawInspector()
    {
        ImGui.PushID("ScreenInspector");
        DrawScreenInspector();
        ImGui.PopID();
        ImGui.Separator();

        if (_selectedNode == null)
        {
            ImGui.TextDisabled(L10n.Tr("msg_select_ui_node"));
            return;
        }

        ImGui.PushID(_selectedNode.Id);
        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), GetNodeKindLabel(_selectedNode.Kind));
        ImGui.Separator();

        string name = Coerce(_selectedNode.Name);
        if (ImGui.InputText(TrId("label_name", "InspectorNodeName"), ref name, 128)) { _selectedNode.Name = name; Save(); }
        bool active = _selectedNode.Active;
        if (ImGui.Checkbox(TrId("label_active", "InspectorNodeActive"), ref active)) { _selectedNode.Active = active; Save(); }
        bool visible = _selectedNode.Visible;
        if (ImGui.Checkbox(TrId("label_visible", "InspectorNodeVisible"), ref visible)) { _selectedNode.Visible = visible; Save(); }

        DrawTransformEditor(_selectedNode.Transform);
        if (_selectedNode is UiContainer container)
            DrawContainerLayoutEditor(container);
        DrawVisualEditor(_selectedNode.Visual);
        DrawBindingsEditor(_selectedNode);
        DrawEventsEditor(_selectedNode);

        switch (_selectedNode)
        {
            case Label label:
                DrawTextEditor(label);
                break;
            case RichText richText:
                DrawTextEditor(richText);
                break;
            case DynamicArea dynamicArea:
                DrawDynamicAreaEditor(dynamicArea);
                break;
            case Button button:
                string buttonText = Coerce(button.Text);
                if (ImGui.InputText(TrId("ui_field_button_text", "ButtonText"), ref buttonText, 128)) { button.Text = buttonText; Save(); }
                break;
            case Dropdown dropdown:
                DrawDropdownEditor(dropdown);
                break;
            case Toggle toggle:
                string toggleText = Coerce(toggle.Text);
                if (ImGui.InputText(TrId("ui_field_toggle_text", "ToggleText"), ref toggleText, 128)) { toggle.Text = toggleText; Save(); }
                bool isChecked = toggle.IsChecked;
                if (ImGui.Checkbox(TrId("ui_field_checked", "ToggleChecked"), ref isChecked)) { toggle.IsChecked = isChecked; Save(); }
                break;
            case Image image:
                string spritePath = Coerce(image.Sprite.Path);
                if (ImGui.InputText(TrId("ui_field_sprite_path", "ImageSpritePath"), ref spritePath, 260)) { image.Sprite = _app.CreateSpriteReference(spritePath); Save(); }
                break;
            case ScrollView scroll:
                bool vertical = scroll.Vertical;
                if (ImGui.Checkbox(TrId("ui_field_vertical", "ScrollVertical"), ref vertical)) { scroll.Vertical = vertical; Save(); }
                bool horizontal = scroll.Horizontal;
                if (ImGui.Checkbox(TrId("ui_field_horizontal", "ScrollHorizontal"), ref horizontal)) { scroll.Horizontal = horizontal; Save(); }
                break;
            case InputField inputField:
                DrawInputFieldEditor(inputField);
                break;
            case TextArea textArea:
                DrawTextAreaEditor(textArea);
                break;
            case Slider slider:
                DrawSliderEditor(slider);
                break;
            case ProgressBar progressBar:
                DrawProgressEditor(progressBar);
                break;
        }
        ImGui.PopID();
    }

    private void DrawScreenInspector()
    {
        if (_screen == null)
            return;

        if (!ImGui.CollapsingHeader(TrId("ui_header_screen", "SectionScreen"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string screenName = Coerce(_screen.Name);
        if (ImGui.InputText(TrId("ui_field_screen_name", "ScreenName"), ref screenName, 128))
        {
            _screen.Name = screenName;
            Save();
        }

        int renderMode = (int)_screen.RenderMode;
        if (ImGui.Combo(TrId("ui_field_render_mode", "ScreenRenderMode"), ref renderMode, $"{L10n.Tr("ui_render_mode_screen_overlay")}\0{L10n.Tr("ui_render_mode_screen_camera")}\0{L10n.Tr("ui_render_mode_world_space")}\0"))
        {
            _screen.RenderMode = (UiRenderMode)renderMode;
            Save();
        }

        int sortingOrder = _screen.SortingOrder;
        if (ImGui.DragInt(TrId("ui_label_sorting_order", "ScreenSortingOrder"), ref sortingOrder, 1f))
        {
            _screen.SortingOrder = sortingOrder;
            Save();
        }

        string uiScriptType = Coerce(_screen.UiScriptType);
        if (ImGui.InputText(TrId("ui_field_ui_script_type", "ScreenUiScriptType"), ref uiScriptType, 256))
        {
            _screen.UiScriptType = uiScriptType;
            Save();
        }

        Vector2 refResolution = _screen.ReferenceResolution;
        if (ImGui.DragFloat2(TrId("ui_label_reference_resolution", "ScreenReferenceResolution"), (float*)&refResolution, 1f, 1f, 8192f))
        {
            _screen.ReferenceResolution = refResolution;
            Save();
        }

        ImGui.TextDisabled(L10n.Tr("ui_msg_screen_variables_hint"));
        for (int i = 0; i < _screen.Variables.Count; i++)
        {
            var variable = _screen.Variables[i];
            ImGui.PushID($"screen-var-{i}");

            string name = Coerce(variable.Name);
            if (ImGui.InputText(TrId("label_name", "ScreenVariableName"), ref name, 128))
            {
                variable.Name = name;
                Save();
            }

            string typeName = Coerce(variable.TypeName);
            if (ImGui.InputText(TrId("label_type", "ScreenVariableType"), ref typeName, 128))
            {
                variable.TypeName = typeName;
                Save();
            }

            if (ImGui.Button(TrId("ctx_remove", "ScreenVariableRemove"), new Vector2(80f, 0f)))
            {
                _screen.Variables.RemoveAt(i);
                Save();
                ImGui.PopID();
                break;
            }

            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(TrId("ui_btn_add_screen_variable", "ScreenAddVariable"), new Vector2(-1, 0)))
        {
            _screen.Variables.Add(new UiScreenVariableDefinition { Name = L10n.Tr("ui_default_screen_variable_name"), TypeName = "object" });
            Save();
        }
    }

    private void DrawTransformEditor(UiTransform transform)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_layout", "SectionLayout"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        Vector2 pos = transform.Position;
        if (ImGui.DragFloat2(TrId("field_Position", "LayoutPosition"), (float*)&pos, 1f)) { transform.Position = pos; Save(); }
        Vector2 size = transform.Size;
        if (ImGui.DragFloat2(TrId("field_Size", "LayoutSize"), (float*)&size, 1f, 0f, 10000f)) { transform.Size = size; Save(); }
        Vector2 anchorMin = transform.AnchorMin;
        if (ImGui.DragFloat2(TrId("ui_field_anchor_min", "LayoutAnchorMin"), (float*)&anchorMin, 0.01f, 0f, 1f)) { transform.AnchorMin = anchorMin; Save(); }
        Vector2 anchorMax = transform.AnchorMax;
        if (ImGui.DragFloat2(TrId("ui_field_anchor_max", "LayoutAnchorMax"), (float*)&anchorMax, 0.01f, 0f, 1f)) { transform.AnchorMax = anchorMax; Save(); }
        Vector2 pivot = transform.Pivot;
        if (ImGui.DragFloat2(TrId("field_Pivot", "LayoutPivot"), (float*)&pivot, 0.01f, 0f, 1f)) { transform.Pivot = pivot; Save(); }
        float rotation = transform.Rotation;
        if (ImGui.DragFloat(TrId("field_Rotation", "LayoutRotation"), ref rotation, 1f)) { transform.Rotation = rotation; Save(); }
    }

    private void DrawContainerLayoutEditor(UiContainer container)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_container_layout", "SectionContainerLayout"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        int layoutMode = (int)container.Layout.Mode;
        if (ImGui.Combo(TrId("ui_field_layout_mode", "ContainerLayoutMode"), ref layoutMode, $"{L10n.Tr("ui_layout_free")}\0{L10n.Tr("ui_layout_horizontal")}\0{L10n.Tr("ui_layout_vertical")}\0{L10n.Tr("ui_layout_grid")}\0{L10n.Tr("ui_layout_wrap")}\0{L10n.Tr("ui_layout_circle")}\0{L10n.Tr("ui_layout_scroll_content")}\0"))
        {
            container.Layout.Mode = (UiLayoutMode)layoutMode;
            Save();
        }

        Vector2 spacing = container.Layout.Spacing;
        if (ImGui.DragFloat2(TrId("ui_field_spacing", "ContainerSpacing"), (float*)&spacing, 1f, 0f, 1000f))
        {
            container.Layout.Spacing = spacing;
            Save();
        }

        Vector4 padding = container.Layout.Padding;
        if (ImGui.DragFloat4(TrId("ui_field_padding", "ContainerPadding"), (float*)&padding, 1f, 0f, 1000f))
        {
            container.Layout.Padding = padding;
            Save();
        }

        bool fitChildren = container.Layout.FitChildren;
        if (ImGui.Checkbox(TrId("ui_field_fit_children", "ContainerFitChildren"), ref fitChildren))
        {
            container.Layout.FitChildren = fitChildren;
            Save();
        }

        if (container.Layout.Mode == UiLayoutMode.Grid)
        {
            int columns = container.Layout.Columns;
            if (ImGui.DragInt(TrId("ui_field_columns", "ContainerColumns"), ref columns, 1f, 1, 32))
            {
                container.Layout.Columns = Math.Max(1, columns);
                Save();
            }
        }

        if (container.Layout.Mode == UiLayoutMode.Circle)
        {
            float radius = container.Layout.CircleRadius;
            if (ImGui.DragFloat(TrId("ui_field_circle_radius", "ContainerCircleRadius"), ref radius, 1f, 0f, 5000f))
            {
                container.Layout.CircleRadius = radius;
                Save();
            }

            float startAngle = container.Layout.CircleStartAngle;
            if (ImGui.DragFloat(TrId("ui_field_start_angle", "ContainerStartAngle"), ref startAngle, 1f, -360f, 360f))
            {
                container.Layout.CircleStartAngle = startAngle;
                Save();
            }

            bool clockwise = container.Layout.CircleClockwise;
            if (ImGui.Checkbox(TrId("ui_field_clockwise", "ContainerClockwise"), ref clockwise))
            {
                container.Layout.CircleClockwise = clockwise;
                Save();
            }
        }
    }

    private void DrawVisualEditor(UiVisualStyle visual)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_visual", "SectionVisual"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        Vector4 bg = visual.BackgroundColor;
        if (ImGui.ColorEdit4(TrId("ui_field_background", "VisualBackground"), ref bg)) { visual.BackgroundColor = bg; Save(); }
        Vector4 fg = visual.ForegroundColor;
        if (ImGui.ColorEdit4(TrId("ui_field_foreground", "VisualForeground"), ref fg)) { visual.ForegroundColor = fg; Save(); }
        Vector4 border = visual.BorderColor;
        if (ImGui.ColorEdit4(TrId("ui_field_border", "VisualBorder"), ref border)) { visual.BorderColor = border; Save(); }
    }

    private void DrawBindingsEditor(UiNode node)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_data", "SectionBindings"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled(L10n.Tr("ui_msg_binding_examples"));
        for (int i = 0; i < node.Bindings.Count; i++)
        {
            var binding = node.Bindings[i];
            ImGui.PushID($"binding-{i}");
            string path = Coerce(binding.Path);
            if (ImGui.InputText(TrId("ui_field_path", "BindingPath"), ref path, 256)) { binding.Path = path; Save(); }
            string targetProperty = Coerce(binding.TargetProperty);
            if (ImGui.InputText(TrId("ui_field_property", "BindingProperty"), ref targetProperty, 128)) { binding.TargetProperty = targetProperty; Save(); }
            int mode = (int)binding.Mode;
            if (ImGui.Combo(TrId("ui_field_mode", "BindingMode"), ref mode, $"{L10n.Tr("ui_binding_mode_one_way")}\0{L10n.Tr("ui_binding_mode_two_way")}\0")) { binding.Mode = (UiBindingMode)mode; Save(); }
            if (ImGui.SmallButton(TrId("ctx_remove", "BindingRemove"))) { node.Bindings.RemoveAt(i); Save(); ImGui.PopID(); break; }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(TrId("ui_btn_add_binding", "AddBinding")))
        {
            node.Bindings.Add(new UiBinding { Path = L10n.Tr("ui_default_binding_path"), TargetProperty = L10n.Tr("ui_default_binding_target_property") });
            Save();
        }
    }

    private void DrawEventsEditor(UiNode node)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_events", "SectionEvents"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled(L10n.Tr("ui_msg_event_target_example"));
        for (int i = 0; i < node.Events.Count; i++)
        {
            var action = node.Events[i];
            ImGui.PushID($"event-{i}");
            int trigger = (int)action.Trigger;
            if (ImGui.Combo(TrId("ui_field_trigger", "EventTrigger"), ref trigger, $"{L10n.Tr("ui_event_pointer_enter")}\0{L10n.Tr("ui_event_pointer_exit")}\0{L10n.Tr("ui_event_pointer_down")}\0{L10n.Tr("ui_event_pointer_up")}\0{L10n.Tr("ui_event_click")}\0{L10n.Tr("ui_event_double_click")}\0{L10n.Tr("ui_event_drag_begin")}\0{L10n.Tr("ui_event_drag")}\0{L10n.Tr("ui_event_drag_end")}\0{L10n.Tr("ui_event_scroll")}\0{L10n.Tr("ui_event_value_changed")}\0{L10n.Tr("ui_event_submit")}\0{L10n.Tr("ui_event_cancel")}\0{L10n.Tr("ui_event_focus_changed")}\0"))
            {
                action.Trigger = (UiEventType)trigger;
                Save();
            }

            string target = Coerce(action.Target);
            if (ImGui.InputText(TrId("ui_field_target", "EventTarget"), ref target, 256)) { action.Target = target; Save(); }
            string method = Coerce(action.Method);
            if (ImGui.InputText(TrId("ui_field_method", "EventMethod"), ref method, 128)) { action.Method = method; Save(); }
            if (ImGui.SmallButton(TrId("ctx_remove", "EventRemove"))) { node.Events.RemoveAt(i); Save(); ImGui.PopID(); break; }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(TrId("ui_btn_add_event", "AddEvent")))
        {
            node.Events.Add(new UiEventAction { Trigger = UiEventType.Click, Target = L10n.Tr("ui_default_event_target"), Method = L10n.Tr("ui_default_event_method") });
            Save();
        }
    }

    private void DrawTextEditor(TextNode text)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_text", "SectionText"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string value = Coerce(text.Text);
        if (ImGui.InputTextMultiline(TrId("field_Text", "TextValue"), ref value, 1024, new Vector2(-1, 90))) { text.Text = value; Save(); }
        float fontSize = text.FontSize;
        if (ImGui.DragFloat(TrId("ui_field_font_size", "TextFontSize"), ref fontSize, 0.5f, 8f, 96f)) { text.FontSize = fontSize; Save(); }
        bool wrap = text.WordWrap;
        if (ImGui.Checkbox(TrId("ui_field_word_wrap", "TextWordWrap"), ref wrap)) { text.WordWrap = wrap; Save(); }
    }

    private void DrawDynamicAreaEditor(DynamicArea dynamicArea)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_dynamic_area", "SectionDynamicArea"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string itemsSource = Coerce(dynamicArea.ItemsSource);
        if (ImGui.InputText(TrId("ui_field_items_source", "DynamicAreaItemsSource"), ref itemsSource, 256))
        {
            dynamicArea.ItemsSource = itemsSource;
            Save();
        }

        ImGui.TextDisabled(L10n.Tr("ui_msg_item_template_hint"));
        dynamicArea.ItemTemplate ??= UiNodeFactory.Create(UiNodeKind.Panel);
        if (dynamicArea.ItemTemplate != null)
        {
            string templateName = Coerce(dynamicArea.ItemTemplate.Name);
            if (ImGui.InputText(TrId("ui_field_template_name", "DynamicAreaTemplateName"), ref templateName, 128))
            {
                dynamicArea.ItemTemplate.Name = templateName;
                Save();
            }

            if (ImGui.Button(TrId("ui_btn_edit_item_template", "DynamicAreaEditTemplate"), new Vector2(-1, 0)))
                _selectedNode = dynamicArea.ItemTemplate;
        }
    }

    private void DrawDropdownEditor(Dropdown dropdown)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_dropdown", "SectionDropdown"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        for (int i = 0; i < dropdown.Options.Count; i++)
        {
            ImGui.PushID(i);
            string option = Coerce(dropdown.Options[i]);
            if (ImGui.InputText("##option", ref option, 128)) { dropdown.Options[i] = option; Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton(TrId("ctx_remove", "DropdownRemove"))) { dropdown.Options.RemoveAt(i); Save(); ImGui.PopID(); break; }
            ImGui.PopID();
        }

        if (ImGui.Button(TrId("ui_btn_add_option", "DropdownAddOption")))
        {
            dropdown.Options.Add(L10n.Tr("ui_option_n", dropdown.Options.Count + 1));
            Save();
        }
    }

    private void DrawInputFieldEditor(InputField inputField)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_input", "SectionInputField"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string value = Coerce(inputField.Value);
        if (ImGui.InputText(TrId("field_Value", "InputFieldValue"), ref value, 256)) { inputField.Value = value; Save(); }
        string placeholder = Coerce(inputField.Placeholder);
        if (ImGui.InputText(TrId("ui_field_placeholder", "InputFieldPlaceholder"), ref placeholder, 256)) { inputField.Placeholder = placeholder; Save(); }
    }

    private void DrawTextAreaEditor(TextArea textArea)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_text_area", "SectionTextArea"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string value = Coerce(textArea.Value);
        if (ImGui.InputTextMultiline(TrId("field_Value", "TextAreaValue"), ref value, 2048, new Vector2(-1, 110))) { textArea.Value = value; Save(); }
        string placeholder = Coerce(textArea.Placeholder);
        if (ImGui.InputText(TrId("ui_field_placeholder", "TextAreaPlaceholder"), ref placeholder, 256)) { textArea.Placeholder = placeholder; Save(); }
    }

    private void DrawSliderEditor(Slider slider)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_slider", "SectionSlider"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        float min = slider.Min;
        if (ImGui.DragFloat(TrId("ui_field_min", "SliderMin"), ref min, 0.1f)) { slider.Min = min; Save(); }
        float max = slider.Max;
        if (ImGui.DragFloat(TrId("ui_field_max", "SliderMax"), ref max, 0.1f)) { slider.Max = max; Save(); }
        float value = slider.Value;
        if (ImGui.DragFloat(TrId("field_Value", "SliderValue"), ref value, 0.01f, slider.Min, slider.Max)) { slider.Value = value; Save(); }
    }

    private void DrawProgressEditor(ProgressBar progressBar)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_progress", "SectionProgress"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        float min = progressBar.Min;
        if (ImGui.DragFloat(TrId("ui_field_min", "ProgressMin"), ref min, 0.1f)) { progressBar.Min = min; Save(); }
        float max = progressBar.Max;
        if (ImGui.DragFloat(TrId("ui_field_max", "ProgressMax"), ref max, 0.1f)) { progressBar.Max = max; Save(); }
        float value = progressBar.Value;
        if (ImGui.DragFloat(TrId("field_Value", "ProgressValue"), ref value, 0.01f, progressBar.Min, progressBar.Max)) { progressBar.Value = value; Save(); }
    }

    private void DrawListViewEditor(ListView listView)
    {
        if (!ImGui.CollapsingHeader(TrId("ui_header_list_view", "SectionListView"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        int itemCount = listView.ItemCount;
        if (ImGui.DragInt(TrId("ui_field_item_count", "ListViewItemCount"), ref itemCount, 1f, 0, 10000)) { listView.ItemCount = itemCount; Save(); }
        bool virtualized = listView.Virtualized;
        if (ImGui.Checkbox(TrId("ui_field_virtualized", "ListViewVirtualized"), ref virtualized)) { listView.Virtualized = virtualized; Save(); }
    }

    private void AddNode(UiNodeKind kind)
    {
        if (_screen == null)
            return;

        var parent = _selectedNode as UiContainer ?? _screen.Root as UiContainer;
        if (parent == null)
            return;

        var node = UiNodeFactory.Create(kind);
        ApplyLocalizedDisplayDefaults(node);
        parent.AddChild(node);
        _selectedNode = node;
        Save();
    }

    private void DeleteSelected()
    {
        if (_selectedNode == null || _selectedNode.Parent == null)
            return;

        var parent = _selectedNode.Parent;
        parent.RemoveChild(_selectedNode);
        _selectedNode = parent;
        Save();
    }

    private void SaveSelectedAsPrefab()
    {
        if (_selectedNode == null || string.IsNullOrWhiteSpace(_assetPath))
            return;

        string directory = Path.GetDirectoryName(_assetPath)!;
        string baseName = string.IsNullOrWhiteSpace(_selectedNode.Name) ? "UiPrefab" : _selectedNode.Name;
        string safeName = string.Join("_", baseName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "UiPrefab";

        string prefabPath = Path.Combine(directory, safeName + ".uiprefab");
        int suffix = 1;
        while (File.Exists(prefabPath))
            prefabPath = Path.Combine(directory, $"{safeName}_{suffix++}.uiprefab");

        UiSerializer.SavePrefab(prefabPath, UiSerializer.CreatePrefab(baseName, _selectedNode));
        AssetPathUtility.EnsureMetaAndGetGuid(prefabPath);
    }

    private void Save()
    {
        if (_screen == null || string.IsNullOrWhiteSpace(_assetPath))
            return;

        string currentJson = JsonSerializer.Serialize(_screen, UiSerializer.Options);
        if (string.IsNullOrWhiteSpace(_lastCommittedScreenJson))
            _lastCommittedScreenJson = currentJson;

        if (!_restoringUndo && !string.Equals(currentJson, _lastCommittedScreenJson, StringComparison.Ordinal))
        {
            PushUndoSnapshot(new UiEditorUndoSnapshot
            {
                ScreenJson = _lastCommittedScreenJson,
                SelectedNodeId = _selectedNode?.Id,
                ResolutionPreset = _resolutionPreset
            });
            _redoStack.Clear();
        }

        UiSerializer.Save(_assetPath, _screen);
        AssetPathUtility.EnsureMetaAndGetGuid(_assetPath);
        _lastCommittedScreenJson = currentJson;
    }

    public override void RefreshTitle()
    {
        Title = L10n.Tr("window_ui_editor");
    }

    private float ComputeFitScale(Vector2 avail)
    {
        if (_screen == null)
            return 1f;

        float usableWidth = Math.Max(64f, avail.X - 64f);
        float usableHeight = Math.Max(64f, avail.Y - 64f);
        float sx = usableWidth / Math.Max(1f, _screen.ReferenceResolution.X);
        float sy = usableHeight / Math.Max(1f, _screen.ReferenceResolution.Y);
        return MathF.Max(0.02f, MathF.Min(sx, sy));
    }

    private void ResetCanvasView()
    {
        _canvasPan = Vector2.Zero;
        _canvasZoom = 1.15f;
        _frameCanvasRequested = true;
        CancelCanvasDrag();
    }

    private void HandleCanvasShortcuts()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows))
            return;

        var io = ImGui.GetIO();
        if (io.WantTextInput)
            return;

        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Z))
        {
            Undo();
            return;
        }

        if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.Y))
        {
            Redo();
            return;
        }

        if (ImGui.IsKeyPressed(ImGuiKey.F))
            ResetCanvasView();
        if (ImGui.IsKeyPressed(ImGuiKey.W))
            _activeCanvasTool = CanvasTool.Move;
        if (ImGui.IsKeyPressed(ImGuiKey.E))
            _activeCanvasTool = CanvasTool.Scale;
        if (ImGui.IsKeyPressed(ImGuiKey.R))
            _activeCanvasTool = CanvasTool.Rotate;
    }

    private void DrawToolButton(string label, CanvasTool tool, Vector2 size)
    {
        bool isActive = _activeCanvasTool == tool;
        if (isActive)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.18f, 0.52f, 0.88f, 1f));

        if (ImGui.Button($"{label}##CanvasTool{tool}", size))
            _activeCanvasTool = tool;

        if (isActive)
            ImGui.PopStyleColor();
    }

    private void HandleCanvasEditing(Vector2 origin, Vector2 avail, Vector2 canvasPos, float scale, bool hovered)
    {
        if (_screen == null)
            return;

        var io = ImGui.GetIO();
        Vector2 mouseScreen = new(io.MousePos.X, io.MousePos.Y);
        Vector2 mouseCanvas = (mouseScreen - canvasPos) / scale;

        if (_canvasDragMode != CanvasDragMode.None)
        {
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                UpdateCanvasDrag(mouseCanvas);
            }
            else
            {
                Save();
                CancelCanvasDrag();
            }

            return;
        }

        if (!hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        if (_selectedNode != null && _selectedNode != _screen.Root)
        {
            int handleIndex = GetResizeHandleAt(mouseScreen, canvasPos, scale);
            if (_activeCanvasTool == CanvasTool.Scale && handleIndex >= 0)
            {
                BeginResizeDrag(handleIndex, mouseCanvas);
                return;
            }

            if (_activeCanvasTool == CanvasTool.Rotate && IsRotateHandleHovered(mouseScreen, canvasPos, scale))
            {
                BeginRotateDrag(mouseCanvas);
                return;
            }
        }

        UiNode picked = HitTest(mouseCanvas) ?? _screen.Root;
        _selectedNode = picked;

        if (_activeCanvasTool == CanvasTool.Move && picked != _screen.Root)
            BeginMoveDrag(mouseCanvas);
    }

    private void BeginMoveDrag(Vector2 mouseCanvas)
    {
        if (_selectedNode == null)
            return;

        _canvasDragMode = CanvasDragMode.Move;
        _dragStartMouseCanvas = mouseCanvas;
        _dragStartRect = _selectedNode.LayoutRect;
        _dragStartPosition = _selectedNode.Transform.Position;
        _dragStartSize = _selectedNode.Transform.Size;
        _dragStartRotation = _selectedNode.Transform.Rotation;
    }

    private void BeginResizeDrag(int handleIndex, Vector2 mouseCanvas)
    {
        if (_selectedNode == null)
            return;

        _canvasDragMode = CanvasDragMode.Resize;
        _activeResizeHandle = handleIndex;
        _dragStartMouseCanvas = mouseCanvas;
        _dragStartRect = _selectedNode.LayoutRect;
        _dragStartPosition = _selectedNode.Transform.Position;
        _dragStartSize = _selectedNode.Transform.Size;
        _dragStartRotation = _selectedNode.Transform.Rotation;
    }

    private void BeginRotateDrag(Vector2 mouseCanvas)
    {
        if (_selectedNode == null)
            return;

        _canvasDragMode = CanvasDragMode.Rotate;
        _dragStartMouseCanvas = mouseCanvas;
        _dragStartRect = _selectedNode.LayoutRect;
        _dragStartPosition = _selectedNode.Transform.Position;
        _dragStartSize = _selectedNode.Transform.Size;
        _dragStartRotation = _selectedNode.Transform.Rotation;
        Vector2 pivot = GetPivotPoint(_selectedNode);
        _dragStartAngle = MathF.Atan2(mouseCanvas.Y - pivot.Y, mouseCanvas.X - pivot.X);
    }

    private void UpdateCanvasDrag(Vector2 mouseCanvas)
    {
        if (_selectedNode == null)
            return;

        switch (_canvasDragMode)
        {
            case CanvasDragMode.Move:
            {
                Vector2 delta = mouseCanvas - _dragStartMouseCanvas;
                ApplyRectToTransform(_selectedNode, new UiRect(
                    _dragStartRect.X + delta.X,
                    _dragStartRect.Y + delta.Y,
                    _dragStartRect.Width,
                    _dragStartRect.Height));
                break;
            }
            case CanvasDragMode.Resize:
            {
                UiRect resized = ResizeRect(_dragStartRect, _activeResizeHandle, mouseCanvas - _dragStartMouseCanvas);
                ApplyRectToTransform(_selectedNode, resized);
                break;
            }
            case CanvasDragMode.Rotate:
            {
                Vector2 pivot = GetPivotPoint(_selectedNode);
                float currentAngle = MathF.Atan2(mouseCanvas.Y - pivot.Y, mouseCanvas.X - pivot.X);
                float deltaDegrees = (currentAngle - _dragStartAngle) * (180f / MathF.PI);
                _selectedNode.Transform.Rotation = _dragStartRotation + deltaDegrees;
                break;
            }
        }

        UiLayoutEngine.Layout(_screen!, _screen!.ReferenceResolution.X, _screen.ReferenceResolution.Y);
    }

    private static UiRect ResizeRect(UiRect rect, int handleIndex, Vector2 delta)
    {
        float minSize = 8f;
        float x = rect.X;
        float y = rect.Y;
        float width = rect.Width;
        float height = rect.Height;
        Vector2 dir = ResizeHandleDirections[handleIndex];

        if (dir.X < 0f)
        {
            x += delta.X;
            width -= delta.X;
        }
        else if (dir.X > 0f)
        {
            width += delta.X;
        }

        if (dir.Y < 0f)
        {
            y += delta.Y;
            height -= delta.Y;
        }
        else if (dir.Y > 0f)
        {
            height += delta.Y;
        }

        if (width < minSize)
        {
            if (dir.X < 0f)
                x -= minSize - width;
            width = minSize;
        }

        if (height < minSize)
        {
            if (dir.Y < 0f)
                y -= minSize - height;
            height = minSize;
        }

        return new UiRect(x, y, width, height);
    }

    private void CancelCanvasDrag()
    {
        _canvasDragMode = CanvasDragMode.None;
        _activeResizeHandle = -1;
    }

    private void Undo()
    {
        if (_screen == null || _undoStack.Count == 0)
            return;

        UiEditorUndoSnapshot current = CaptureCurrentUndoSnapshot();
        UiEditorUndoSnapshot snapshot = _undoStack.Pop();
        _redoStack.Push(current);
        LimitUndoHistory(_redoStack);
        RestoreUndoSnapshot(snapshot);
    }

    private void Redo()
    {
        if (_screen == null || _redoStack.Count == 0)
            return;

        UiEditorUndoSnapshot current = CaptureCurrentUndoSnapshot();
        UiEditorUndoSnapshot snapshot = _redoStack.Pop();
        _undoStack.Push(current);
        LimitUndoHistory(_undoStack);
        RestoreUndoSnapshot(snapshot);
    }

    private UiEditorUndoSnapshot CaptureCurrentUndoSnapshot()
    {
        return new UiEditorUndoSnapshot
        {
            ScreenJson = _screen == null ? string.Empty : JsonSerializer.Serialize(_screen, UiSerializer.Options),
            SelectedNodeId = _selectedNode?.Id,
            ResolutionPreset = _resolutionPreset
        };
    }

    private void RestoreUndoSnapshot(UiEditorUndoSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(_assetPath))
            return;

        UIScreenAsset? restored = JsonSerializer.Deserialize<UIScreenAsset>(snapshot.ScreenJson, UiSerializer.Options);
        if (restored == null)
            return;

        restored.RebindTree();
        _restoringUndo = true;
        _screen = restored;
        _resolutionPreset = Math.Clamp(snapshot.ResolutionPreset, 0, _presets.Length - 1);
        _selectedNode = FindNodeById(snapshot.SelectedNodeId) ?? _screen.Root;
        UiSerializer.Save(_assetPath, _screen);
        AssetPathUtility.EnsureMetaAndGetGuid(_assetPath);
        _lastCommittedScreenJson = snapshot.ScreenJson;
        _restoringUndo = false;
    }

    private UiNode? FindNodeById(string? nodeId)
    {
        if (_screen == null || string.IsNullOrWhiteSpace(nodeId))
            return null;

        return _screen.Root.DescendantsAndSelf()
            .FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));
    }

    private void PushUndoSnapshot(UiEditorUndoSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ScreenJson))
            return;

        if (_undoStack.Count > 0 &&
            string.Equals(_undoStack.Peek().ScreenJson, snapshot.ScreenJson, StringComparison.Ordinal) &&
            string.Equals(_undoStack.Peek().SelectedNodeId, snapshot.SelectedNodeId, StringComparison.Ordinal))
        {
            return;
        }

        _undoStack.Push(snapshot);
        LimitUndoHistory(_undoStack);
    }

    private static void LimitUndoHistory(Stack<UiEditorUndoSnapshot> stack)
    {
        if (stack.Count <= MaxUndoHistory)
            return;

        var items = stack.ToList();
        items.RemoveAt(items.Count - 1);
        stack.Clear();
        for (int i = items.Count - 1; i >= 0; i--)
            stack.Push(items[i]);
    }

    private void DrawSelectionGizmo(ImDrawListPtr draw, Vector2 canvasPos, float scale)
    {
        if (_selectedNode == null || _screen == null || _selectedNode == _screen.Root)
            return;

        Vector2[] corners = GetNodeScreenCorners(_selectedNode, canvasPos, scale);
        uint outline = ImGui.GetColorU32(new Vector4(0.20f, 0.74f, 1f, 1f));
        draw.AddQuad(corners[0], corners[1], corners[2], corners[3], outline, 2f);

        if (_activeCanvasTool == CanvasTool.Scale)
        {
            foreach (Vector2 handle in GetResizeHandlePositions(corners))
            {
                draw.AddRectFilled(handle - new Vector2(4f, 4f), handle + new Vector2(4f, 4f), outline);
                draw.AddRect(handle - new Vector2(4f, 4f), handle + new Vector2(4f, 4f), ImGui.GetColorU32(new Vector4(0.03f, 0.05f, 0.08f, 1f)));
            }
        }

        if (_activeCanvasTool == CanvasTool.Rotate)
        {
            Vector2 rotateHandle = GetRotateHandlePosition(corners);
            Vector2 topMid = (corners[0] + corners[1]) * 0.5f;
            draw.AddLine(topMid, rotateHandle, outline, 2f);
            draw.AddCircleFilled(rotateHandle, 6f, outline);
            draw.AddCircle(rotateHandle, 6f, ImGui.GetColorU32(new Vector4(0.03f, 0.05f, 0.08f, 1f)));
        }
    }

    private static readonly Vector2[] ResizeHandleDirections =
    [
        new Vector2(-1f, -1f),
        new Vector2(0f, -1f),
        new Vector2(1f, -1f),
        new Vector2(1f, 0f),
        new Vector2(1f, 1f),
        new Vector2(0f, 1f),
        new Vector2(-1f, 1f),
        new Vector2(-1f, 0f)
    ];

    private static Vector2[] GetResizeHandlePositions(Vector2[] corners)
    {
        Vector2 topMid = (corners[0] + corners[1]) * 0.5f;
        Vector2 rightMid = (corners[1] + corners[2]) * 0.5f;
        Vector2 bottomMid = (corners[2] + corners[3]) * 0.5f;
        Vector2 leftMid = (corners[3] + corners[0]) * 0.5f;
        return
        [
            corners[0],
            topMid,
            corners[1],
            rightMid,
            corners[2],
            bottomMid,
            corners[3],
            leftMid
        ];
    }

    private int GetResizeHandleAt(Vector2 mouseScreen, Vector2 canvasPos, float scale)
    {
        if (_selectedNode == null)
            return -1;

        Vector2[] handles = GetResizeHandlePositions(GetNodeScreenCorners(_selectedNode, canvasPos, scale));
        for (int i = 0; i < handles.Length; i++)
        {
            if (Vector2.DistanceSquared(mouseScreen, handles[i]) <= 64f)
                return i;
        }

        return -1;
    }

    private bool IsRotateHandleHovered(Vector2 mouseScreen, Vector2 canvasPos, float scale)
    {
        if (_selectedNode == null)
            return false;

        Vector2 handle = GetRotateHandlePosition(GetNodeScreenCorners(_selectedNode, canvasPos, scale));
        return Vector2.DistanceSquared(mouseScreen, handle) <= 100f;
    }

    private static Vector2 GetRotateHandlePosition(Vector2[] corners)
    {
        Vector2 topMid = (corners[0] + corners[1]) * 0.5f;
        Vector2 outward = Vector2.Normalize(topMid - ((corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f));
        if (float.IsNaN(outward.X) || float.IsNaN(outward.Y))
            outward = new Vector2(0f, -1f);
        return topMid + outward * 28f;
    }

    private Vector2[] GetNodeScreenCorners(UiNode node, Vector2 canvasPos, float scale)
    {
        UiRect rect = node.LayoutRect;
        Vector2 pivot = GetPivotPoint(node);
        float radians = node.Transform.Rotation * (MathF.PI / 180f);

        Vector2 topLeft = RotatePoint(new Vector2(rect.X, rect.Y) - pivot, radians) + pivot;
        Vector2 topRight = RotatePoint(new Vector2(rect.Right, rect.Y) - pivot, radians) + pivot;
        Vector2 bottomRight = RotatePoint(new Vector2(rect.Right, rect.Bottom) - pivot, radians) + pivot;
        Vector2 bottomLeft = RotatePoint(new Vector2(rect.X, rect.Bottom) - pivot, radians) + pivot;

        return
        [
            canvasPos + topLeft * scale,
            canvasPos + topRight * scale,
            canvasPos + bottomRight * scale,
            canvasPos + bottomLeft * scale
        ];
    }

    private static Vector2 RotatePoint(Vector2 point, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector2(point.X * cos - point.Y * sin, point.X * sin + point.Y * cos);
    }

    private static Vector2 GetMin(Vector2[] points)
    {
        float minX = points.Min(p => p.X);
        float minY = points.Min(p => p.Y);
        return new Vector2(minX, minY);
    }

    private static Vector2 GetMax(Vector2[] points)
    {
        float maxX = points.Max(p => p.X);
        float maxY = points.Max(p => p.Y);
        return new Vector2(maxX, maxY);
    }

    private static Vector2 GetPivotPoint(UiNode node)
    {
        UiRect rect = node.LayoutRect;
        return new Vector2(
            rect.X + rect.Width * node.Transform.Pivot.X,
            rect.Y + rect.Height * node.Transform.Pivot.Y);
    }

    private static Vector2 TransformLocalPoint(UiNode node, Vector2 canvasPos, float scale, Vector2 offset)
    {
        UiRect rect = node.LayoutRect;
        Vector2 point = new Vector2(rect.X + offset.X, rect.Y + offset.Y);
        Vector2 pivot = GetPivotPoint(node);
        float radians = node.Transform.Rotation * (MathF.PI / 180f);
        Vector2 rotated = RotatePoint(point - pivot, radians) + pivot;
        return canvasPos + rotated * scale;
    }

    private bool ContainsCanvasPoint(UiNode node, Vector2 point)
    {
        UiRect rect = node.LayoutRect;
        Vector2 pivot = GetPivotPoint(node);
        float radians = -(node.Transform.Rotation * (MathF.PI / 180f));
        Vector2 local = RotatePoint(point - pivot, radians) + pivot;
        return rect.Contains(local);
    }

    private void ApplyRectToTransform(UiNode node, UiRect desiredRect)
    {
        UiRect parentRect = node.Parent?.LayoutRect ?? new UiRect(0f, 0f, _screen!.ReferenceResolution.X, _screen.ReferenceResolution.Y);
        UiTransform transform = node.Transform;
        Vector2 aMin = new(
            parentRect.X + parentRect.Width * transform.AnchorMin.X,
            parentRect.Y + parentRect.Height * transform.AnchorMin.Y);
        Vector2 aMax = new(
            parentRect.X + parentRect.Width * transform.AnchorMax.X,
            parentRect.Y + parentRect.Height * transform.AnchorMax.Y);
        Vector4 margin = transform.Margin;

        if (transform.AnchorMin != transform.AnchorMax)
        {
            transform.Position = new Vector2(
                desiredRect.X - aMin.X - margin.X,
                desiredRect.Y - aMin.Y - margin.Y);
            transform.Size = new Vector2(
                desiredRect.Width - (aMax.X - aMin.X) + margin.X + margin.Z,
                desiredRect.Height - (aMax.Y - aMin.Y) + margin.Y + margin.W);
            return;
        }

        transform.Size = new Vector2(
            Math.Max(8f, desiredRect.Width),
            Math.Max(8f, desiredRect.Height));
        transform.Position = new Vector2(
            desiredRect.X - aMin.X + transform.Size.X * transform.Pivot.X,
            desiredRect.Y - aMin.Y + transform.Size.Y * transform.Pivot.Y);
    }

    private string GetPresetLabel(int index) => L10n.Tr(_presets[index].LabelKey);

    private static string Coerce(string? value) => value ?? string.Empty;

    private static void ApplyLocalizedDisplayDefaults(UiNode node)
    {
        switch (node)
        {
            case UiContainer container when string.Equals(container.Name, "Container", StringComparison.Ordinal):
                container.Name = L10n.Tr("ui_node_container");
                break;
            case Panel panel when string.Equals(panel.Name, "Panel", StringComparison.Ordinal):
                panel.Name = L10n.Tr("ui_node_panel");
                break;
            case Label label when string.Equals(label.Text, "Label", StringComparison.Ordinal):
                label.Name = L10n.Tr("ui_node_label");
                label.Text = L10n.Tr("ui_default_label_text");
                break;
            case RichText richText when string.Equals(richText.Text, "<b>Rich Text</b>", StringComparison.Ordinal):
                richText.Name = L10n.Tr("ui_node_rich_text");
                richText.Text = L10n.Tr("ui_default_rich_text");
                break;
            case Image image when string.Equals(image.Name, "Image", StringComparison.Ordinal):
                image.Name = L10n.Tr("ui_node_image");
                break;
            case Button button when string.Equals(button.Text, "Button", StringComparison.Ordinal):
                button.Name = L10n.Tr("ui_node_button");
                button.Text = L10n.Tr("ui_default_button_text");
                break;
            case IconButton iconButton when string.Equals(iconButton.Name, "IconButton", StringComparison.Ordinal):
                iconButton.Name = L10n.Tr("ui_node_icon_button");
                break;
            case Toggle toggle when string.Equals(toggle.Text, "Toggle", StringComparison.Ordinal):
                toggle.Name = L10n.Tr("ui_node_toggle");
                toggle.Text = L10n.Tr("ui_default_toggle_text");
                break;
            case Dropdown dropdown:
                dropdown.Name = L10n.Tr("ui_node_dropdown");
                dropdown.Options =
                [
                    L10n.Tr("ui_option_n", 1),
                    L10n.Tr("ui_option_n", 2),
                    L10n.Tr("ui_option_n", 3)
                ];
                break;
            case InputField inputField when string.Equals(inputField.Placeholder, "Type here...", StringComparison.Ordinal):
                inputField.Name = L10n.Tr("ui_node_input_field");
                inputField.Placeholder = L10n.Tr("ui_default_placeholder");
                break;
            case TextArea textArea when string.Equals(textArea.Placeholder, "Type here...", StringComparison.Ordinal):
                textArea.Name = L10n.Tr("ui_node_text_area");
                textArea.Placeholder = L10n.Tr("ui_default_placeholder");
                break;
            case Slider slider when string.Equals(slider.Name, "Slider", StringComparison.Ordinal):
                slider.Name = L10n.Tr("ui_node_slider");
                break;
            case ProgressBar progressBar when string.Equals(progressBar.Name, "ProgressBar", StringComparison.Ordinal):
                progressBar.Name = L10n.Tr("ui_node_progress_bar");
                break;
            case Scrollbar scrollbar when string.Equals(scrollbar.Name, "Scrollbar", StringComparison.Ordinal):
                scrollbar.Name = L10n.Tr("ui_node_scrollbar");
                break;
            case ScrollView scrollView when string.Equals(scrollView.Name, "ScrollView", StringComparison.Ordinal):
                scrollView.Name = L10n.Tr("ui_node_scroll_view");
                break;
            case ListView listView when string.Equals(listView.Name, "ListView", StringComparison.Ordinal):
                listView.Name = L10n.Tr("ui_node_list_view");
                break;
            case GridView gridView when string.Equals(gridView.Name, "GridView", StringComparison.Ordinal):
                gridView.Name = L10n.Tr("ui_node_grid_view");
                break;
            case Window window when string.Equals(window.Title, "Window", StringComparison.Ordinal):
                window.Name = L10n.Tr("ui_node_window");
                window.Title = L10n.Tr("ui_default_window_title");
                break;
            case Modal modal when string.Equals(modal.Name, "Modal", StringComparison.Ordinal):
                modal.Name = L10n.Tr("ui_node_modal");
                break;
            case Tooltip tooltip when string.Equals(tooltip.Text, "Tooltip", StringComparison.Ordinal):
                tooltip.Name = L10n.Tr("ui_node_tooltip");
                tooltip.Text = L10n.Tr("ui_default_tooltip_text");
                break;
            case Tabs tabs:
                tabs.Name = L10n.Tr("ui_node_tabs");
                tabs.Titles =
                [
                    L10n.Tr("ui_default_tab_n", 1),
                    L10n.Tr("ui_default_tab_n", 2)
                ];
                break;
            case ToggleGroup toggleGroup when string.Equals(toggleGroup.Name, "ToggleGroup", StringComparison.Ordinal):
                toggleGroup.Name = L10n.Tr("ui_node_toggle_group");
                break;
            case Spacer spacer when string.Equals(spacer.Name, "Spacer", StringComparison.Ordinal):
                spacer.Name = L10n.Tr("ui_node_spacer");
                break;
            case DynamicArea dynamicArea when string.Equals(dynamicArea.Name, "DynamicArea", StringComparison.Ordinal):
                dynamicArea.Name = L10n.Tr("ui_node_dynamic_area");
                dynamicArea.ItemsSource = L10n.Tr("ui_default_dynamic_area_items_source");
                break;
        }
    }

    private static string GetNodeKindLabel(UiNodeKind kind)
    {
        return kind switch
        {
            UiNodeKind.Container => L10n.Tr("ui_node_container"),
            UiNodeKind.Panel => L10n.Tr("ui_node_panel"),
            UiNodeKind.Label => L10n.Tr("ui_node_label"),
            UiNodeKind.RichText => L10n.Tr("ui_node_rich_text"),
            UiNodeKind.Image => L10n.Tr("ui_node_image"),
            UiNodeKind.Button => L10n.Tr("ui_node_button"),
            UiNodeKind.IconButton => L10n.Tr("ui_node_icon_button"),
            UiNodeKind.Toggle => L10n.Tr("ui_node_toggle"),
            UiNodeKind.ToggleGroup => L10n.Tr("ui_node_toggle_group"),
            UiNodeKind.Dropdown => L10n.Tr("ui_node_dropdown"),
            UiNodeKind.InputField => L10n.Tr("ui_node_input_field"),
            UiNodeKind.TextArea => L10n.Tr("ui_node_text_area"),
            UiNodeKind.Slider => L10n.Tr("ui_node_slider"),
            UiNodeKind.ProgressBar => L10n.Tr("ui_node_progress_bar"),
            UiNodeKind.Scrollbar => L10n.Tr("ui_node_scrollbar"),
            UiNodeKind.ScrollView => L10n.Tr("ui_node_scroll_view"),
            UiNodeKind.ListView => L10n.Tr("ui_node_list_view"),
            UiNodeKind.GridView => L10n.Tr("ui_node_grid_view"),
            UiNodeKind.Window => L10n.Tr("ui_node_window"),
            UiNodeKind.Modal => L10n.Tr("ui_node_modal"),
            UiNodeKind.Tabs => L10n.Tr("ui_node_tabs"),
            UiNodeKind.Tooltip => L10n.Tr("ui_node_tooltip"),
            UiNodeKind.Spacer => L10n.Tr("ui_node_spacer"),
            UiNodeKind.DynamicArea => L10n.Tr("ui_node_dynamic_area"),
            _ => kind.ToString()
        };
    }
}
