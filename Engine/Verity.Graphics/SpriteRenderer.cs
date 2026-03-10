using System.Numerics;
using Irodori.Texture;
using Verity.Core.ECS;
using Verity.Core;

namespace Verity.Graphics;

public class SpriteRenderer : Component
{
    [SerializeField]
    public Sprite Sprite { get; set; }

    [HideInInspector]
    public TextureObjectUploaded? Texture { get; set; }

    [SerializeField]
    public Verity.Core.Color Color { get; set; } = Verity.Core.Color.White;

    [SerializeField]
    public string SortingLayerName { get; set; } = "Default";

    [SerializeField]
    public int OrderInLayer { get; set; } = 0;

    [SerializeField]
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    [SerializeField]
    public bool FlipX { get; set; } = false;

    [SerializeField]
    public bool FlipY { get; set; } = false;

    [Button("Apply Native Aspect Ratio")]
    public void ApplyNativeAspectRatio()
    {
        if (Texture == null || Owner == null) return;

        float texW = Texture.Width;
        float texH = Texture.Height;
        if (texW <= 0 || texH <= 0) return;

        float aspect = texW / texH;
        Vector2 currentScale = Owner.Transform.Scale;
        float maxScale = MathF.Max(MathF.Abs(currentScale.X), MathF.Abs(currentScale.Y));

        if (aspect >= 1.0f) // 가로가 더 길거나 같음
        {
            Owner.Transform.Scale = new Vector2(maxScale, maxScale / aspect);
        }
        else // 세로가 더 김
        {
            Owner.Transform.Scale = new Vector2(maxScale * aspect, maxScale);
        }
    }

    internal int ResolvedLayerIndex => SortingLayer.GetLayerIndex(SortingLayerName);
}
