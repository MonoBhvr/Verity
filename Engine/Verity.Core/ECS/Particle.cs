using System.Numerics;

namespace Verity.Core;

public struct Particle
{
    public Vector2 Position { get; set; }

    public Vector2 Velocity { get; set; }

    public float Lifetime { get; set; }

    public float Age { get; set; }

    public Color Color { get; set; }

    public float Size { get; set; }
}
