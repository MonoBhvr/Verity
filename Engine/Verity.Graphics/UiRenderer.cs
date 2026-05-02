using System.Numerics;
using System.Text.RegularExpressions;
using Irodori.Framebuffer;
using Irodori.Texture;
using Verity.Core.UI;

namespace Verity.Graphics;

public static class UiRenderer
{
    public static string DefaultFontPath { get; set; } = string.Empty;
    public static string DefaultFontFamily { get; set; } = string.Empty;

    public static void Render(RenderPipeline pipeline, UIScreenAsset screen, int viewportWidth, int viewportHeight, RenderTarget? targetFbo = null)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        UiLayoutEngine.Layout(screen, viewportWidth, viewportHeight);

        UiRect canvasRect = UiSystem.GetCanvasViewportRect(screen, viewportWidth, viewportHeight);
        float scale = Math.Max(0.0001f, canvasRect.Width / Math.Max(1f, screen.ReferenceResolution.X));
        var projection = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
        var view =
            Matrix4x4.CreateScale(scale, scale, 1f) *
            Matrix4x4.CreateTranslation(canvasRect.X, canvasRect.Y, 0f);
        foreach (var node in screen.Root.DescendantsAndSelf().Where(n => n.Active && n.Visible))
            RenderNode(pipeline, node, projection, view, targetFbo);

        pipeline.FlushBrowserQuadBatch();
    }

    public static void Render(RenderPipeline pipeline, Canvas canvas, int viewportWidth, int viewportHeight, RenderTarget? targetFbo = null)
    {
        if (!canvas.Visible)
            return;

        Render(pipeline, canvas.Screen, viewportWidth, viewportHeight, targetFbo);
    }

    private static void RenderNode(RenderPipeline pipeline, UiNode node, Matrix4x4 projection, Matrix4x4 view, RenderTarget? targetFbo)
    {
        var rect = node.LayoutRect;
        if (rect.Width <= 0f || rect.Height <= 0f)
            return;

        var texture = ResolveTexture(pipeline, node) ?? DefaultSprites.Square;
        if (texture == null)
            return;

        Color color = ResolveColor(node);
        var model = Matrix4x4.CreateScale(rect.Width, rect.Height, 1f) * Matrix4x4.CreateTranslation(rect.X, rect.Y, 0f);
        pipeline.DrawTile(texture, model, color, projection, view, targetFbo);

        if (node is ProgressBar progress)
            RenderProgressFill(pipeline, progress, projection, view, targetFbo);
        else if (node is Slider slider)
            RenderSliderHandle(pipeline, slider, projection, view, targetFbo);

        RenderNodeText(pipeline, node, projection, view, targetFbo);
    }

    private static RenderTexture? ResolveTexture(RenderPipeline pipeline, UiNode node)
    {
        if (node is Image imageWithTexture &&
            pipeline.TryGetTextureAsset(imageWithTexture.TextureAsset, out var textureAsset))
        {
            return textureAsset;
        }

        if (node is Image imageWithOutput &&
            !string.IsNullOrWhiteSpace(imageWithOutput.CameraOutputName) &&
            pipeline.TryGetCameraOutputTexture(imageWithOutput.CameraOutputName, out var outputTexture))
        {
            return outputTexture;
        }

        if (node is Image image && !string.IsNullOrWhiteSpace(image.Sprite.Path))
            return pipeline.LoadTexture(image.Sprite) ?? DefaultSprites.Square;

        if (node is IconButton iconButton && !string.IsNullOrWhiteSpace(iconButton.Icon.Path))
            return pipeline.LoadTexture(iconButton.Icon) ?? DefaultSprites.Square;

        return DefaultSprites.Square;
    }

    private static Color ResolveColor(UiNode node)
    {
        var state = node.RuntimeState;
        if ((state & UiStateFlags.Disabled) != 0)
            return node.Visual.DisabledColor;
        if ((state & UiStateFlags.Pressed) != 0)
            return node.Visual.PressedColor;
        if ((state & UiStateFlags.Hover) != 0)
            return node.Visual.HoverColor;
        if ((state & UiStateFlags.Checked) != 0)
            return node.Visual.HoverColor;
        return node.Visual.BackgroundColor;
    }

    private static void RenderProgressFill(RenderPipeline pipeline, ProgressBar progress, Matrix4x4 projection, Matrix4x4 view, RenderTarget? targetFbo)
    {
        var rect = progress.LayoutRect;
        float t = progress.Max <= progress.Min ? 0f : Math.Clamp((progress.Value - progress.Min) / (progress.Max - progress.Min), 0f, 1f);
        if (t <= 0f || DefaultSprites.Square == null)
            return;

        var fillRect = new UiRect(rect.X + 4f, rect.Y + 4f, Math.Max(0f, (rect.Width - 8f) * t), Math.Max(0f, rect.Height - 8f));
        var fillModel = Matrix4x4.CreateScale(fillRect.Width, fillRect.Height, 1f) * Matrix4x4.CreateTranslation(fillRect.X, fillRect.Y, 0f);
        pipeline.DrawTile(DefaultSprites.Square, fillModel, progress.Visual.ForegroundColor, projection, view, targetFbo);
    }

    private static void RenderSliderHandle(RenderPipeline pipeline, Slider slider, Matrix4x4 projection, Matrix4x4 view, RenderTarget? targetFbo)
    {
        var rect = slider.LayoutRect;
        float t = slider.Max <= slider.Min ? 0f : Math.Clamp((slider.Value - slider.Min) / (slider.Max - slider.Min), 0f, 1f);
        float handleSize = Math.Min(rect.Height, 18f);
        float x = rect.X + ((rect.Width - handleSize) * t);
        float y = rect.Y + ((rect.Height - handleSize) * 0.5f);
        var model = Matrix4x4.CreateScale(handleSize, handleSize, 1f) * Matrix4x4.CreateTranslation(x, y, 0f);
        if (DefaultSprites.Square != null)
            pipeline.DrawTile(DefaultSprites.Square, model, slider.Visual.ForegroundColor, projection, view, targetFbo);
    }

    private static void RenderNodeText(RenderPipeline pipeline, UiNode node, Matrix4x4 projection, Matrix4x4 view, RenderTarget? targetFbo)
    {
        if (!TryResolveNodeText(node, out var text, out var color, out var wordWrap))
            return;

        if (string.IsNullOrWhiteSpace(text))
            return;

        pipeline.FlushBrowserQuadBatch();

        var rect = GetNodeTextRect(node);
        if (rect.Width <= 0f || rect.Height <= 0f)
            return;

        var fontSize = ResolveNodeFontSize(node);
        var horizontal = ResolveNodeHorizontalAlignment(node);
        var vertical = ResolveNodeVerticalAlignment(node);

        pipeline.DrawText(
            new TextRenderOptions(
                text,
                new System.Numerics.Vector2(rect.X, rect.Y),
                new System.Numerics.Vector2(rect.Width, rect.Height),
                color,
                fontSize,
                ResolveAutoFit(node),
                wordWrap,
                ResolveFontPath(node),
                ResolveFontFamily(node),
                horizontal,
                vertical),
            projection,
            view,
            targetFbo);
    }

    public static bool TryResolveNodeText(UiNode node, out string text, out Color color, out bool wordWrap)
    {
        text = string.Empty;
        color = node.Visual.ForegroundColor;
        wordWrap = false;

        switch (node)
        {
            case Label label:
                text = label.Text;
                wordWrap = label.WordWrap;
                return true;
            case RichText richText:
                text = Regex.Replace(richText.Text ?? string.Empty, "<.*?>", string.Empty);
                wordWrap = richText.WordWrap;
                return true;
            case Button button:
                text = button.Text;
                return true;
            case Toggle toggle:
                text = toggle.Text;
                return true;
            case InputField inputField:
                if (!string.IsNullOrEmpty(inputField.Value))
                {
                    text = inputField.Value;
                    return true;
                }

                text = inputField.Placeholder;
                color = WithAlpha(node.Visual.ForegroundColor, 0.55f);
                return !string.IsNullOrWhiteSpace(text);
            case TextArea textArea:
                if (!string.IsNullOrEmpty(textArea.Value))
                {
                    text = textArea.Value;
                    wordWrap = true;
                    return true;
                }

                text = textArea.Placeholder;
                color = WithAlpha(node.Visual.ForegroundColor, 0.55f);
                wordWrap = true;
                return !string.IsNullOrWhiteSpace(text);
            case Dropdown dropdown:
                if (dropdown.Options.Count == 0)
                    return false;

                int selected = Math.Clamp(dropdown.SelectedIndex, 0, dropdown.Options.Count - 1);
                text = dropdown.Options[selected];
                return true;
            case Tabs tabs:
                if (tabs.Titles.Count == 0)
                    return false;

                int selectedTab = Math.Clamp(tabs.SelectedIndex, 0, tabs.Titles.Count - 1);
                text = tabs.Titles[selectedTab];
                return !string.IsNullOrWhiteSpace(text);
            case Tooltip tooltip:
                text = tooltip.Text;
                wordWrap = true;
                return true;
            case Window window:
                text = window.Title;
                return !string.IsNullOrWhiteSpace(text);
            default:
                return false;
        }
    }

    public static UiRect GetNodeTextRect(UiNode node)
    {
        var rect = node.LayoutRect;
        var padding = node.Visual.Padding;

        return node switch
        {
            Button or Toggle or Dropdown => new UiRect(rect.X + padding.X, rect.Y, Math.Max(0f, rect.Width - padding.X - padding.Z), rect.Height),
            Tabs => new UiRect(rect.X + padding.X, rect.Y + padding.Y, Math.Max(0f, rect.Width - padding.X - padding.Z), Math.Max(0f, Math.Min(rect.Height, 30f))),
            Window => new UiRect(rect.X + padding.X, rect.Y + padding.Y, Math.Max(0f, rect.Width - padding.X - padding.Z), Math.Max(0f, Math.Min(rect.Height, 32f))),
            _ => new UiRect(
                rect.X + padding.X,
                rect.Y + padding.Y,
                Math.Max(0f, rect.Width - padding.X - padding.Z),
                Math.Max(0f, rect.Height - padding.Y - padding.W))
        };
    }

    public static float ResolveNodeFontSize(UiNode node)
    {
        return node switch
        {
            TextNode textNode when textNode.FontSize > 0f => textNode.FontSize,
            _ => node.Visual.FontSize > 0f ? node.Visual.FontSize : 16f
        };
    }

    public static TextHorizontalAlignment ResolveNodeHorizontalAlignment(UiNode node)
    {
        if (node.Visual.TextHorizontalAlignment != UiTextHorizontalAlignment.Default)
        {
            return node.Visual.TextHorizontalAlignment switch
            {
                UiTextHorizontalAlignment.Center => TextHorizontalAlignment.Center,
                UiTextHorizontalAlignment.Right => TextHorizontalAlignment.Right,
                _ => TextHorizontalAlignment.Left
            };
        }

        return node switch
        {
            Button or Toggle or Dropdown => TextHorizontalAlignment.Center,
            Tabs => TextHorizontalAlignment.Left,
            Window => TextHorizontalAlignment.Left,
            _ => TextHorizontalAlignment.Left
        };
    }

    public static TextVerticalAlignment ResolveNodeVerticalAlignment(UiNode node)
    {
        if (node.Visual.TextVerticalAlignment != UiTextVerticalAlignment.Default)
        {
            return node.Visual.TextVerticalAlignment switch
            {
                UiTextVerticalAlignment.Middle => TextVerticalAlignment.Middle,
                UiTextVerticalAlignment.Bottom => TextVerticalAlignment.Bottom,
                _ => TextVerticalAlignment.Top
            };
        }

        return node switch
        {
            Button or Toggle or Dropdown or Tooltip or Tabs => TextVerticalAlignment.Middle,
            InputField => TextVerticalAlignment.Middle,
            _ => TextVerticalAlignment.Top
        };
    }

    public static bool ResolveAutoFit(UiNode node) => node.Visual.AutoFitText;

    private static Color WithAlpha(Color color, float alphaScale)
    {
        return new Color(color.R, color.G, color.B, color.A * alphaScale);
    }

    private static string ResolveFontPath(UiNode node)
    {
        if (node is TextNode textNode && !string.IsNullOrWhiteSpace(textNode.FontPath))
            return textNode.FontPath;

        return !string.IsNullOrWhiteSpace(node.Visual.FontPath)
            ? node.Visual.FontPath
            : DefaultFontPath;
    }

    private static string ResolveFontFamily(UiNode node)
    {
        if (node is TextNode textNode && !string.IsNullOrWhiteSpace(textNode.FontFamily))
            return textNode.FontFamily;

        return !string.IsNullOrWhiteSpace(node.Visual.FontFamily)
            ? node.Visual.FontFamily
            : DefaultFontFamily;
    }
}
