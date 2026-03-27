using System.Numerics;
using Irodori.Framebuffer;
using Irodori.Texture;
using Verity.Core.UI;

namespace Verity.Graphics;

public static class UiRenderer
{
    public static void Render(RenderPipeline pipeline, UIScreenAsset screen, int viewportWidth, int viewportHeight, FramebufferObject.Uploaded? targetFbo = null)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
            return;

        UiLayoutEngine.Layout(screen, viewportWidth, viewportHeight);

        var projection = Matrix4x4.CreateOrthographicOffCenter(0f, screen.ReferenceResolution.X, screen.ReferenceResolution.Y, 0f, -1f, 1f);
        var view = Matrix4x4.Identity;
        foreach (var node in screen.Root.DescendantsAndSelf().Where(n => n.Active && n.Visible))
            RenderNode(pipeline, node, projection, view, targetFbo);
    }

    public static void Render(RenderPipeline pipeline, Canvas canvas, int viewportWidth, int viewportHeight, FramebufferObject.Uploaded? targetFbo = null)
    {
        if (!canvas.Visible)
            return;

        Render(pipeline, canvas.Screen, viewportWidth, viewportHeight, targetFbo);
    }

    private static void RenderNode(RenderPipeline pipeline, UiNode node, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? targetFbo)
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
    }

    private static TextureObjectUploaded? ResolveTexture(RenderPipeline pipeline, UiNode node)
    {
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

    private static void RenderProgressFill(RenderPipeline pipeline, ProgressBar progress, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? targetFbo)
    {
        var rect = progress.LayoutRect;
        float t = progress.Max <= progress.Min ? 0f : Math.Clamp((progress.Value - progress.Min) / (progress.Max - progress.Min), 0f, 1f);
        if (t <= 0f || DefaultSprites.Square == null)
            return;

        var fillRect = new UiRect(rect.X + 4f, rect.Y + 4f, Math.Max(0f, (rect.Width - 8f) * t), Math.Max(0f, rect.Height - 8f));
        var fillModel = Matrix4x4.CreateScale(fillRect.Width, fillRect.Height, 1f) * Matrix4x4.CreateTranslation(fillRect.X, fillRect.Y, 0f);
        pipeline.DrawTile(DefaultSprites.Square, fillModel, progress.Visual.ForegroundColor, projection, view, targetFbo);
    }

    private static void RenderSliderHandle(RenderPipeline pipeline, Slider slider, Matrix4x4 projection, Matrix4x4 view, FramebufferObject.Uploaded? targetFbo)
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
}
