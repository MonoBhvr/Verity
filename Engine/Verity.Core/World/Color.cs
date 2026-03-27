using System.Numerics;

namespace Verity.Core;

public struct Color
{
    private float _r;
    private float _g;
    private float _b;
    private float _a;

    public float R { get => _r; set => _r = value; }
    public float G { get => _g; set => _g = value; }
    public float B { get => _b; set => _b = value; }
    public float A { get => _a; set => _a = value; }

    public Color(float r, float g, float b, float a = 1.0f)
    {
        _r = r;
        _g = g;
        _b = b;
        _a = a;
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
    public static implicit operator System.Drawing.Color(Color c) => System.Drawing.Color.FromArgb((int)(c.A * 255), (int)(c.R * 255), (int)(c.G * 255), (int)(c.B * 255));
    
    public override string ToString() => $"Color({R:F2}, {G:F2}, {B:F2}, {A:F2})";
}
