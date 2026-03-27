using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Core;
using Verity.Core.UI;

namespace Verity.Editor.Windows;

public sealed unsafe class UIEditorWindow : EditorWindow
{
    private readonly EditorApp _app;
    private string? _assetPath;
    private UIScreenAsset? _screen;
    private UiNode? _selectedNode;
    private int _resolutionPreset;
    private readonly (string LabelKey, Vector2 Size)[] _presets =
    [
        ("ui_preset_16_9", new Vector2(1920, 1080)),
        ("ui_preset_19_5_9", new Vector2(1170, 540)),
        ("ui_preset_4_3", new Vector2(1024, 768)),
        ("ui_preset_tablet", new Vector2(1280, 800))
    ];

    public bool OverlayEnabled { get; set; } = true;
    public UIScreenAsset? PreviewScreen => _screen;

    public UIEditorWindow(EditorApp app) : base(L10n.Tr("window_ui_editor"))
    {
        _app = app;
    }

    public override void OnGui()
    {
        TryLoadSelectedAsset();
        if (_screen == null || string.IsNullOrWhiteSpace(_assetPath))
        {
            ImGui.TextDisabled(L10n.Tr("msg_select_ui_asset_to_edit_here"));
            return;
        }

        DrawToolbar();
        var avail = ImGui.GetContentRegionAvail();
        float leftWidth = 240f;
        float rightWidth = 300f;
        float gap = 8f;
        float centerWidth = Math.Max(200f, avail.X - leftWidth - rightWidth - gap * 2f);

        if (ImGui.BeginChild("UiHierarchy", new Vector2(leftWidth, avail.Y), ImGuiChildFlags.Borders))
        {
            DrawHierarchy(_screen.Root);
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("UiCanvas", new Vector2(centerWidth, avail.Y), ImGuiChildFlags.Borders))
        {
            DrawCanvasPreview();
        }
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("UiInspector", new Vector2(rightWidth, avail.Y), ImGuiChildFlags.Borders))
        {
            DrawInspector();
        }
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
        IsOpen = true;
    }

    private void DrawToolbar()
    {
        if (ImGui.Button(L10n.Tr("btn_save"), new Vector2(72, 0)))
            Save();
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_panel"))) AddNode(UiNodeKind.Panel);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_button"))) AddNode(UiNodeKind.Button);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_text"))) AddNode(UiNodeKind.Label);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_image"))) AddNode(UiNodeKind.Image);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_input"))) AddNode(UiNodeKind.InputField);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_toggle"))) AddNode(UiNodeKind.Toggle);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_scroll"))) AddNode(UiNodeKind.ScrollView);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_dropdown"))) AddNode(UiNodeKind.Dropdown);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_slider"))) AddNode(UiNodeKind.Slider);
        ImGui.SameLine();
        if (ImGui.Button(L10n.Tr("ui_btn_add_list"))) AddNode(UiNodeKind.ListView);
        ImGui.SameLine();
        if (_selectedNode != null && _selectedNode != _screen!.Root && ImGui.Button(L10n.Tr("btn_delete"))) DeleteSelected();
        ImGui.SameLine();
        if (_selectedNode != null && ImGui.Button(L10n.Tr("ui_btn_save_prefab"))) SaveSelectedAsPrefab();
        ImGui.SameLine();
        bool overlayEnabled = OverlayEnabled;
        if (ImGui.Checkbox(L10n.Tr("label_overlay"), ref overlayEnabled))
            OverlayEnabled = overlayEnabled;
        ImGui.SameLine();
        if (ImGui.BeginCombo(L10n.Tr("label_preview"), GetPresetLabel(_resolutionPreset)))
        {
            for (int i = 0; i < _presets.Length; i++)
            {
                bool selected = i == _resolutionPreset;
                if (ImGui.Selectable(GetPresetLabel(i), selected))
                {
                    _resolutionPreset = i;
                    _screen!.ReferenceResolution = _presets[i].Size;
                    Save();
                }
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.Separator();
    }

    private void DrawHierarchy(UiNode node)
    {
        ImGui.PushID(node.Id);
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (_selectedNode == node) flags |= ImGuiTreeNodeFlags.Selected;
        if (node.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        bool open = ImGui.TreeNodeEx($"{node.Name} ({node.Kind})", flags);
        if (ImGui.IsItemClicked()) _selectedNode = node;
        if (open)
        {
            foreach (var child in node.Children)
                DrawHierarchy(child);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    private void DrawCanvasPreview()
    {
        if (_screen == null)
            return;

        var avail = ImGui.GetContentRegionAvail();
        var origin = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + avail, ImGui.GetColorU32(new Vector4(0.07f, 0.08f, 0.1f, 1f)), 6f);

        float sx = avail.X / _screen.ReferenceResolution.X;
        float sy = avail.Y / _screen.ReferenceResolution.Y;
        float scale = MathF.Min(sx, sy);
        Vector2 canvasSize = new(_screen.ReferenceResolution.X * scale, _screen.ReferenceResolution.Y * scale);
        Vector2 canvasPos = new(origin.X + ((avail.X - canvasSize.X) * 0.5f), origin.Y + ((avail.Y - canvasSize.Y) * 0.5f));
        draw.AddRectFilled(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(new Vector4(0.12f, 0.13f, 0.16f, 1f)), 8f);
        draw.AddRect(canvasPos, canvasPos + canvasSize, ImGui.GetColorU32(new Vector4(0.35f, 0.4f, 0.5f, 1f)), 8f);

        UiLayoutEngine.Layout(_screen, _screen.ReferenceResolution.X, _screen.ReferenceResolution.Y);
        foreach (var node in _screen.Root.DescendantsAndSelf().Where(n => n.Visible && n.Active))
            DrawNodePreview(node, draw, canvasPos, scale);

        if (ImGui.InvisibleButton("##ui-canvas-hit", avail) && ImGui.IsItemClicked())
        {
            Vector2 mouse = ImGui.GetIO().MousePos;
            Vector2 local = (mouse - canvasPos) / MathF.Max(scale, 0.0001f);
            _selectedNode = HitTest(local);
        }
    }

    private void DrawNodePreview(UiNode node, ImDrawListPtr draw, Vector2 canvasPos, float scale)
    {
        var rect = node.LayoutRect;
        Vector2 min = canvasPos + new Vector2(rect.X, rect.Y) * scale;
        Vector2 max = canvasPos + new Vector2(rect.Right, rect.Bottom) * scale;
        var fill = new Vector4(node.Visual.BackgroundColor.R, node.Visual.BackgroundColor.G, node.Visual.BackgroundColor.B, Math.Max(0.08f, node.Visual.BackgroundColor.A));
        var stroke = _selectedNode == node ? new Vector4(0.25f, 0.8f, 1f, 1f) : new Vector4(node.Visual.BorderColor.R, node.Visual.BorderColor.G, node.Visual.BorderColor.B, Math.Max(0.35f, node.Visual.BorderColor.A));
        draw.AddRectFilled(min, max, ImGui.GetColorU32(fill), MathF.Min(10f, node.Visual.CornerRadius));
        draw.AddRect(min, max, ImGui.GetColorU32(stroke), MathF.Min(10f, node.Visual.CornerRadius), ImDrawFlags.None, _selectedNode == node ? 2f : 1f);

        string label = node switch
        {
            Button button => button.Text,
            Label text => text.Text,
            Dropdown dropdown => dropdown.Options.Count > 0 ? dropdown.Options[Math.Clamp(dropdown.SelectedIndex, 0, dropdown.Options.Count - 1)] : L10n.Tr("ui_default_dropdown"),
            Toggle toggle => toggle.Text,
            Window window => window.Title,
            Tooltip tooltip => tooltip.Text,
            _ => node.Name
        };

        if (!string.IsNullOrWhiteSpace(label))
        {
            draw.AddText(min + new Vector2(8, 8), ImGui.GetColorU32(new Vector4(node.Visual.ForegroundColor.R, node.Visual.ForegroundColor.G, node.Visual.ForegroundColor.B, 1f)), label);
        }
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
            if (!node.LayoutRect.Contains(point))
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
        if (_selectedNode == null)
        {
            ImGui.TextDisabled(L10n.Tr("msg_select_ui_node"));
            return;
        }

        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), _selectedNode.Kind.ToString());
        ImGui.Separator();

        string name = _selectedNode.Name;
        if (ImGui.InputText(L10n.Tr("label_name"), ref name, 128)) { _selectedNode.Name = name; Save(); }
        bool active = _selectedNode.Active;
        if (ImGui.Checkbox(L10n.Tr("label_active"), ref active)) { _selectedNode.Active = active; Save(); }
        bool visible = _selectedNode.Visible;
        if (ImGui.Checkbox(L10n.Tr("label_visible"), ref visible)) { _selectedNode.Visible = visible; Save(); }

        DrawTransformEditor(_selectedNode.Transform);
        DrawVisualEditor(_selectedNode.Visual);
        DrawBindingsEditor(_selectedNode);
        DrawEventsEditor(_selectedNode);

        switch (_selectedNode)
        {
            case Label label:
                DrawTextEditor(label);
                break;
            case Button button:
                string buttonText = button.Text;
                if (ImGui.InputText(L10n.Tr("ui_field_button_text"), ref buttonText, 128)) { button.Text = buttonText; Save(); }
                break;
            case Dropdown dropdown:
                DrawDropdownEditor(dropdown);
                break;
            case Toggle toggle:
                string toggleText = toggle.Text;
                if (ImGui.InputText(L10n.Tr("ui_field_toggle_text"), ref toggleText, 128)) { toggle.Text = toggleText; Save(); }
                bool isChecked = toggle.IsChecked;
                if (ImGui.Checkbox(L10n.Tr("ui_field_checked"), ref isChecked)) { toggle.IsChecked = isChecked; Save(); }
                break;
            case Image image:
                string spritePath = image.Sprite.Path;
                if (ImGui.InputText(L10n.Tr("ui_field_sprite_path"), ref spritePath, 260)) { image.Sprite = _app.CreateSpriteReference(spritePath); Save(); }
                break;
            case ScrollView scroll:
                bool vertical = scroll.Vertical;
                if (ImGui.Checkbox(L10n.Tr("ui_field_vertical"), ref vertical)) { scroll.Vertical = vertical; Save(); }
                bool horizontal = scroll.Horizontal;
                if (ImGui.Checkbox(L10n.Tr("ui_field_horizontal"), ref horizontal)) { scroll.Horizontal = horizontal; Save(); }
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
            case ListView listView:
                DrawListViewEditor(listView);
                break;
        }
    }

    private void DrawTransformEditor(UiTransform transform)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_layout"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        Vector2 pos = transform.Position;
        if (ImGui.DragFloat2(L10n.Tr("field_Position"), (float*)&pos, 1f)) { transform.Position = pos; Save(); }
        Vector2 size = transform.Size;
        if (ImGui.DragFloat2(L10n.Tr("field_Size"), (float*)&size, 1f)) { transform.Size = size; Save(); }
        Vector2 anchorMin = transform.AnchorMin;
        if (ImGui.DragFloat2(L10n.Tr("ui_field_anchor_min"), (float*)&anchorMin, 0.01f, 0f, 1f)) { transform.AnchorMin = anchorMin; Save(); }
        Vector2 anchorMax = transform.AnchorMax;
        if (ImGui.DragFloat2(L10n.Tr("ui_field_anchor_max"), (float*)&anchorMax, 0.01f, 0f, 1f)) { transform.AnchorMax = anchorMax; Save(); }
        Vector2 pivot = transform.Pivot;
        if (ImGui.DragFloat2(L10n.Tr("field_Pivot"), (float*)&pivot, 0.01f, 0f, 1f)) { transform.Pivot = pivot; Save(); }
    }

    private void DrawVisualEditor(UiVisualStyle visual)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_visual"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        Vector4 bg = visual.BackgroundColor;
        if (ImGui.ColorEdit4(L10n.Tr("ui_field_background"), ref bg)) { visual.BackgroundColor = bg; Save(); }
        Vector4 fg = visual.ForegroundColor;
        if (ImGui.ColorEdit4(L10n.Tr("ui_field_foreground"), ref fg)) { visual.ForegroundColor = fg; Save(); }
        Vector4 border = visual.BorderColor;
        if (ImGui.ColorEdit4(L10n.Tr("ui_field_border"), ref border)) { visual.BorderColor = border; Save(); }
    }

    private void DrawBindingsEditor(UiNode node)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_data"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled(L10n.Tr("ui_msg_binding_example"));
        for (int i = 0; i < node.Bindings.Count; i++)
        {
            var binding = node.Bindings[i];
            ImGui.PushID($"binding-{i}");
            string path = binding.Path;
            if (ImGui.InputText(L10n.Tr("ui_field_path"), ref path, 256)) { binding.Path = path; Save(); }
            string targetProperty = binding.TargetProperty;
            if (ImGui.InputText(L10n.Tr("ui_field_property"), ref targetProperty, 128)) { binding.TargetProperty = targetProperty; Save(); }
            int mode = (int)binding.Mode;
            if (ImGui.Combo(L10n.Tr("ui_field_mode"), ref mode, $"{L10n.Tr("ui_binding_mode_one_way")}\0{L10n.Tr("ui_binding_mode_two_way")}\0")) { binding.Mode = (UiBindingMode)mode; Save(); }
            if (ImGui.SmallButton(L10n.Tr("ctx_remove"))) { node.Bindings.RemoveAt(i); Save(); ImGui.PopID(); break; }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(L10n.Tr("ui_btn_add_binding")))
        {
            node.Bindings.Add(new UiBinding { Path = "Hud:Entity", TargetProperty = "Text" });
            Save();
        }
    }

    private void DrawEventsEditor(UiNode node)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_events"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        ImGui.TextDisabled(L10n.Tr("ui_msg_event_target_example"));
        for (int i = 0; i < node.Events.Count; i++)
        {
            var action = node.Events[i];
            ImGui.PushID($"event-{i}");
            int trigger = (int)action.Trigger;
            if (ImGui.Combo(L10n.Tr("ui_field_trigger"), ref trigger, $"{L10n.Tr("ui_event_pointer_enter")}\0{L10n.Tr("ui_event_pointer_exit")}\0{L10n.Tr("ui_event_pointer_down")}\0{L10n.Tr("ui_event_pointer_up")}\0{L10n.Tr("ui_event_click")}\0{L10n.Tr("ui_event_double_click")}\0{L10n.Tr("ui_event_drag_begin")}\0{L10n.Tr("ui_event_drag")}\0{L10n.Tr("ui_event_drag_end")}\0{L10n.Tr("ui_event_scroll")}\0{L10n.Tr("ui_event_value_changed")}\0{L10n.Tr("ui_event_submit")}\0{L10n.Tr("ui_event_cancel")}\0{L10n.Tr("ui_event_focus_changed")}\0"))
            {
                action.Trigger = (UiEventType)trigger;
                Save();
            }

            string target = action.Target;
            if (ImGui.InputText(L10n.Tr("ui_field_target"), ref target, 256)) { action.Target = target; Save(); }
            string method = action.Method;
            if (ImGui.InputText(L10n.Tr("ui_field_method"), ref method, 128)) { action.Method = method; Save(); }
            if (ImGui.SmallButton(L10n.Tr("ctx_remove"))) { node.Events.RemoveAt(i); Save(); ImGui.PopID(); break; }
            ImGui.Separator();
            ImGui.PopID();
        }

        if (ImGui.Button(L10n.Tr("ui_btn_add_event")))
        {
            node.Events.Add(new UiEventAction { Trigger = UiEventType.Click, Target = "self", Method = "OnUiEvent" });
            Save();
        }
    }

    private void DrawTextEditor(TextNode text)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_text"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string value = text.Text;
        if (ImGui.InputTextMultiline(L10n.Tr("field_Text"), ref value, 1024, new Vector2(-1, 90))) { text.Text = value; Save(); }
        float fontSize = text.FontSize;
        if (ImGui.DragFloat(L10n.Tr("ui_field_font_size"), ref fontSize, 0.5f, 8f, 96f)) { text.FontSize = fontSize; Save(); }
        bool wrap = text.WordWrap;
        if (ImGui.Checkbox(L10n.Tr("ui_field_word_wrap"), ref wrap)) { text.WordWrap = wrap; Save(); }
    }

    private void DrawDropdownEditor(Dropdown dropdown)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_dropdown"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        for (int i = 0; i < dropdown.Options.Count; i++)
        {
            ImGui.PushID(i);
            string option = dropdown.Options[i];
            if (ImGui.InputText("##option", ref option, 128)) { dropdown.Options[i] = option; Save(); }
            ImGui.SameLine();
            if (ImGui.SmallButton(L10n.Tr("ctx_remove"))) { dropdown.Options.RemoveAt(i); Save(); ImGui.PopID(); break; }
            ImGui.PopID();
        }
        if (ImGui.Button(L10n.Tr("ui_btn_add_option"))) { dropdown.Options.Add(L10n.Tr("ui_option_n", dropdown.Options.Count + 1)); Save(); }
    }

    private void DrawInputFieldEditor(InputField inputField)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_input"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string value = inputField.Value;
        if (ImGui.InputText(L10n.Tr("field_Value"), ref value, 256)) { inputField.Value = value; Save(); }
        string placeholder = inputField.Placeholder;
        if (ImGui.InputText(L10n.Tr("ui_field_placeholder"), ref placeholder, 256)) { inputField.Placeholder = placeholder; Save(); }
    }

    private void DrawTextAreaEditor(TextArea textArea)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_text_area"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        string value = textArea.Value;
        if (ImGui.InputTextMultiline(L10n.Tr("field_Value"), ref value, 2048, new Vector2(-1, 110))) { textArea.Value = value; Save(); }
        string placeholder = textArea.Placeholder;
        if (ImGui.InputText(L10n.Tr("ui_field_placeholder"), ref placeholder, 256)) { textArea.Placeholder = placeholder; Save(); }
    }

    private void DrawSliderEditor(Slider slider)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_slider"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        float min = slider.Min;
        if (ImGui.DragFloat(L10n.Tr("ui_field_min"), ref min, 0.1f)) { slider.Min = min; Save(); }
        float max = slider.Max;
        if (ImGui.DragFloat(L10n.Tr("ui_field_max"), ref max, 0.1f)) { slider.Max = max; Save(); }
        float value = slider.Value;
        if (ImGui.DragFloat(L10n.Tr("field_Value"), ref value, 0.01f, slider.Min, slider.Max)) { slider.Value = value; Save(); }
    }

    private void DrawProgressEditor(ProgressBar progressBar)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_progress"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        float min = progressBar.Min;
        if (ImGui.DragFloat(L10n.Tr("ui_field_min"), ref min, 0.1f)) { progressBar.Min = min; Save(); }
        float max = progressBar.Max;
        if (ImGui.DragFloat(L10n.Tr("ui_field_max"), ref max, 0.1f)) { progressBar.Max = max; Save(); }
        float value = progressBar.Value;
        if (ImGui.DragFloat(L10n.Tr("field_Value"), ref value, 0.01f, progressBar.Min, progressBar.Max)) { progressBar.Value = value; Save(); }
    }

    private void DrawListViewEditor(ListView listView)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("ui_header_list_view"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        int itemCount = listView.ItemCount;
        if (ImGui.DragInt(L10n.Tr("ui_field_item_count"), ref itemCount, 1f, 0, 10000)) { listView.ItemCount = itemCount; Save(); }
        bool virtualized = listView.Virtualized;
        if (ImGui.Checkbox(L10n.Tr("ui_field_virtualized"), ref virtualized)) { listView.Virtualized = virtualized; Save(); }
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
        string safeName = string.Join("_", _selectedNode.Name.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "UiPrefab";

        string prefabPath = Path.Combine(directory, safeName + ".uiprefab");
        int suffix = 1;
        while (File.Exists(prefabPath))
            prefabPath = Path.Combine(directory, $"{safeName}_{suffix++}.uiprefab");

        UiSerializer.SavePrefab(prefabPath, UiSerializer.CreatePrefab(_selectedNode.Name, _selectedNode));
        AssetPathUtility.EnsureMetaAndGetGuid(prefabPath);
    }

    private void Save()
    {
        if (_screen == null || string.IsNullOrWhiteSpace(_assetPath))
            return;

        UiSerializer.Save(_assetPath, _screen);
        AssetPathUtility.EnsureMetaAndGetGuid(_assetPath);
    }

    public override void RefreshTitle()
    {
        Title = L10n.Tr("window_ui_editor");
    }

    private string GetPresetLabel(int index) => L10n.Tr(_presets[index].LabelKey);

    private static void ApplyLocalizedDisplayDefaults(UiNode node)
    {
        switch (node)
        {
            case Label label when string.Equals(label.Text, "Label", StringComparison.Ordinal):
                label.Text = L10n.Tr("ui_default_label_text");
                break;
            case RichText richText when string.Equals(richText.Text, "<b>Rich Text</b>", StringComparison.Ordinal):
                richText.Text = L10n.Tr("ui_default_rich_text");
                break;
            case Button button when string.Equals(button.Text, "Button", StringComparison.Ordinal):
                button.Text = L10n.Tr("ui_default_button_text");
                break;
            case Toggle toggle when string.Equals(toggle.Text, "Toggle", StringComparison.Ordinal):
                toggle.Text = L10n.Tr("ui_default_toggle_text");
                break;
            case Dropdown dropdown:
                dropdown.Options =
                [
                    L10n.Tr("ui_option_n", 1),
                    L10n.Tr("ui_option_n", 2),
                    L10n.Tr("ui_option_n", 3)
                ];
                break;
            case InputField inputField when string.Equals(inputField.Placeholder, "Type here...", StringComparison.Ordinal):
                inputField.Placeholder = L10n.Tr("ui_default_placeholder");
                break;
            case TextArea textArea when string.Equals(textArea.Placeholder, "Type here...", StringComparison.Ordinal):
                textArea.Placeholder = L10n.Tr("ui_default_placeholder");
                break;
            case Window window when string.Equals(window.Title, "Window", StringComparison.Ordinal):
                window.Title = L10n.Tr("ui_default_window_title");
                break;
            case Tooltip tooltip when string.Equals(tooltip.Text, "Tooltip", StringComparison.Ordinal):
                tooltip.Text = L10n.Tr("ui_default_tooltip_text");
                break;
            case Tabs tabs:
                tabs.Titles =
                [
                    L10n.Tr("ui_default_tab_n", 1),
                    L10n.Tr("ui_default_tab_n", 2)
                ];
                break;
        }
    }
}
