using System.Numerics;

namespace Verity.Core;

public struct Color
{
    private float _r;
    private float _g;
    private float _b;
    private float _a;
    private bool _initialized;

    public float R { get => _initialized ? _r : 1f; set { _r = value; _initialized = true; } }
    public float G { get => _initialized ? _g : 1f; set { _g = value; _initialized = true; } }
    public float B { get => _initialized ? _b : 1f; set { _b = value; _initialized = true; } }
    public float A { get => _initialized ? _a : 1f; set { _a = value; _initialized = true; } }

    public Color(float r, float g, float b, float a = 1.0f)
    {
        _r = r; _g = g; _b = b; _a = a;
        _initialized = true;
    }

    public static Color FromRgba(int r, int g, int b, int a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public static Color White => new(1, 1, 1, 1);
    public static Color Black => new(0, 0, 0, 1);
    public static Color Red => new(1, 0, 0, 1);
    public static Color Green => new(0, 1, 0, 1);
    public static Color Blue => new(0, 0, 1, 1);
    public static Color Yellow => new(1, 0.92f, 0.016f, 1);
    public static Color Cyan => new(0, 1, 1, 1);
    public static Color Magenta => new(1, 0, 1, 1);
    public static Color Gray => new(0.5f, 0.5f, 0.5f, 1);
    public static Color Clear => new(0, 0, 0, 0);
    public static Color CornflowerBlue => new(0.392f, 0.584f, 0.929f, 1);

    public static implicit operator Vector4(Color c) => new(c.R, c.G, c.B, c.A);
    public static implicit operator Color(Vector4 v) => new(v.X, v.Y, v.Z, v.W);
    public static implicit operator Color(System.Drawing.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
    public static implicit operator System.Drawing.Color(Color c) => System.Drawing.Color.FromArgb((int)Math.Clamp(c.A * 255, 0, 255), (int)Math.Clamp(c.R * 255, 0, 255), (int)Math.Clamp(c.G * 255, 0, 255), (int)Math.Clamp(c.B * 255, 0, 255));

    public override string ToString() => $"Color({R:F2}, {G:F2}, {B:F2}, {A:F2})";
}
