using System.Numerics;
using Irodori.Texture;
using Verity.Core.ECS;
using Verity.Core;

namespace Verity.Graphics;

public class SpriteRenderer : Component, IHasSize
{
    private Sprite _sprite;

    [SerializeField]
    public Sprite Sprite
    {
        get => _sprite;
        set
        {
            if (_sprite.Path == value.Path &&
                _sprite.Guid == value.Guid &&
                _sprite.SpriteId == value.SpriteId)
                return;

            _sprite = value;
            Texture = null;
        }
    }

    [SerializeField]
    public StyleAsset Style { get; set; }

    [HideInInspector]
    public TextureObjectUploaded? Texture { get; set; }

    [HideInInspector]
    public StyleRuntime? StyleRuntime { get; set; }

    [SerializeField]
    public Verity.Core.Color Color { get; set; } = Verity.Core.Color.White;

    [SerializeField, SortingLayerSelector]
    public string SortingLayerName { get; set; } = "Default";

    [SerializeField]
    public int OrderInLayer { get; set; } = 0;

    [SerializeField]
    public Vector2 Pivot { get; set; } = new(0.5f, 0.5f);

    [SerializeField]
    public bool UseSpritePivot { get; set; } = true;

    [SerializeField]
    public Vector2 Size { get; set; } = Vector2.One;

    [SerializeField]
    public bool FlipX { get; set; } = false;

    [SerializeField]
    public bool FlipY { get; set; } = false;

    [SerializeField]
    public bool CastShadows { get; set; } = true;

    [SerializeField]
    public ShadowCasterSourceMode ShadowSourceMode { get; set; } = ShadowCasterSourceMode.PreferRenderer;

    [SerializeField]
    public ShadowSelfMode ShadowSelfMode { get; set; } = ShadowSelfMode.ExcludeSelf;

    [SerializeField]
    public float ShadowAlphaThreshold { get; set; } = 0.5f;

    [Button("Apply Native Aspect Ratio")]
    public void ApplyNativeAspectRatio()
    {
        if (Texture == null || Owner == null) return;

        float texW = Texture.Width;
        float texH = Texture.Height;
        if (texW <= 0.0001f || texH <= 0.0001f) return;

        float aspect = texW / texH;
        Vector2 currentSize = Size;
        float maxSize = MathF.Max(MathF.Abs(currentSize.X), MathF.Abs(currentSize.Y));

        if (aspect >= 1.0f) // 가로가 더 길거나 같음
        {
            Size = new Vector2(maxSize, maxSize / aspect);
        }
        else // 세로가 더 김
        {
            Size = new Vector2(maxSize * aspect, maxSize);
        }
    }

    internal int ResolvedLayerIndex => SortingLayer.GetLayerIndex(SortingLayerName);
}
