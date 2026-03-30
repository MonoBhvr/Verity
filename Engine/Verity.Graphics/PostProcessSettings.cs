using Verity.Core;

namespace Verity.Graphics;

public class BloomSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 400;

    [SerializeField]
    public float Intensity { get; set; } = 1.0f;

    [SerializeField]
    public float Threshold { get; set; } = 1.0f;

    [SerializeField]
    public float Scatter { get; set; } = 1.0f;

    [SerializeField]
    public int BlurIterations { get; set; } = 4;

    [SerializeField]
    public int Downsample { get; set; } = 2;
}

public class VignetteSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 700;

    [SerializeField]
    public float Intensity { get; set; } = 0.4f;

    [SerializeField]
    public float Smoothness { get; set; } = 0.2f;

    [SerializeField]
    public float Roundness { get; set; } = 1.0f;

    [SerializeField]
    public Color Color { get; set; } = Color.Black;
}

public class ColorAdjustmentsSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 600;

    [SerializeField]
    public float Exposure { get; set; } = 0.0f;

    [SerializeField]
    public float Contrast { get; set; } = 1.0f;

    [SerializeField]
    public float Saturation { get; set; } = 1.0f;

    [SerializeField]
    public Color Tint { get; set; } = Color.White;
}

public class MotionBlurSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 500;

    [SerializeField]
    public float Intensity { get; set; } = 0.15f;
}

public class DistortionSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 100;

    [SerializeField]
    public float Intensity { get; set; } = 0.08f;

    [SerializeField]
    public Vector2 Center { get; set; } = new(0.5f, 0.5f);

    [SerializeField]
    public float Scale { get; set; } = 1.0f;
}

public class ChromaticAberrationSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 300;

    [SerializeField]
    public float Intensity { get; set; } = 0.01f;

    [SerializeField]
    public Vector2 Center { get; set; } = new(0.5f, 0.5f);
}

public class PixelateSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 200;

    [SerializeField]
    public int Width { get; set; } = 320;

    [SerializeField]
    public int Height { get; set; } = 180;
}

public class CustomPostProcessSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public int Order { get; set; } = 800;

    [SerializeField]
    public StyleAsset Style { get; set; } = default;
}

public class PostProcessSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public BloomSettings? Bloom { get; set; }

    [SerializeField]
    public VignetteSettings? Vignette { get; set; }

    [SerializeField]
    public ColorAdjustmentsSettings? ColorAdjustments { get; set; }

    [SerializeField]
    public MotionBlurSettings? MotionBlur { get; set; }

    [SerializeField]
    public DistortionSettings? Distortion { get; set; }

    [SerializeField]
    public ChromaticAberrationSettings? ChromaticAberration { get; set; }

    [SerializeField]
    public PixelateSettings? Pixelate { get; set; }

    [SerializeField]
    public CustomPostProcessSettings? Custom { get; set; }

    [SerializeField]
    public List<CustomPostProcessSettings> Customs { get; set; } = [];

    public List<CustomPostProcessSettings> GetCustomEffects()
    {
        if (Custom != null)
        {
            Customs ??= [];
            Customs.Add(Custom);
            Custom = null;
        }

        Customs ??= [];
        return Customs;
    }

    public bool HasAnyEffect()
    {
        List<CustomPostProcessSettings> customs = GetCustomEffects();
        return Bloom != null ||
               Vignette != null ||
               ColorAdjustments != null ||
               MotionBlur != null ||
               Distortion != null ||
               ChromaticAberration != null ||
               Pixelate != null ||
               customs.Count > 0;
    }

    public bool HasAnyEnabledEffect()
    {
        List<CustomPostProcessSettings> customs = GetCustomEffects();
        return (Bloom?.Enabled ?? false) ||
               (Vignette?.Enabled ?? false) ||
               (ColorAdjustments?.Enabled ?? false) ||
               (MotionBlur?.Enabled ?? false) ||
               (Distortion?.Enabled ?? false) ||
               (ChromaticAberration?.Enabled ?? false) ||
               (Pixelate?.Enabled ?? false) ||
               customs.Any(custom => custom.Enabled);
    }
}
