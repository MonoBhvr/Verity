using Verity.Core;

namespace Verity.Graphics;

public class BloomSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

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
    public float Intensity { get; set; } = 0.15f;
}

public class DistortionSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public float Intensity { get; set; } = 0.015f;

    [SerializeField]
    public float Speed { get; set; } = 1.5f;

    [SerializeField]
    public float Frequency { get; set; } = 12.0f;

    [SerializeField]
    public Vector2 Center { get; set; } = new(0.5f, 0.5f);
}

public class CustomPostProcessSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public StyleAsset Style { get; set; } = default;
}

public class PostProcessSettings
{
    [SerializeField]
    public bool Enabled { get; set; } = false;

    [SerializeField]
    public BloomSettings Bloom { get; set; } = new();

    [SerializeField]
    public VignetteSettings Vignette { get; set; } = new();

    [SerializeField]
    public ColorAdjustmentsSettings ColorAdjustments { get; set; } = new();

    [SerializeField]
    public MotionBlurSettings MotionBlur { get; set; } = new();

    [SerializeField]
    public DistortionSettings Distortion { get; set; } = new();

    [SerializeField]
    public CustomPostProcessSettings Custom { get; set; } = new();
}
