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

    internal int ResolvedLayerIndex => SortingLayer.GetLayerIndex(SortingLayerName);
}
