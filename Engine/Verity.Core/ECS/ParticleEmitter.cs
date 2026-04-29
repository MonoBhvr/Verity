using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core;

public enum ParticleEmissionShape
{
    Point,
    Circle,
    Box
}

public class ParticleEmitter : Component
{
    [SerializeField]
    public float Rate { get; set; } = 10f;

    [SerializeField]
    public float ParticleLifetime { get; set; } = 1f;

    [SerializeField]
    public float ParticleSize { get; set; } = 1f;

    [SerializeField]
    public Color ParticleColor { get; set; } = Color.White;

    [SerializeField]
    public Vector2 InitialVelocity { get; set; } = Vector2.Zero;

    [SerializeField]
    public Vector2 Gravity { get; set; } = Vector2.Zero;

    [SerializeField]
    public int MaxParticles { get; set; } = 256;

    [SerializeField]
    public ParticleEmissionShape EmissionShape { get; set; } = ParticleEmissionShape.Point;

    [SerializeField]
    public float EmissionRadius { get; set; } = 1f;

    [SerializeField]
    public Vector2 EmissionBoxSize { get; set; } = Vector2.One;

    [SerializeField]
    public int RandomSeed { get; set; } = Environment.TickCount;

    public override void OnDestroy()
    {
        ParticleSystem.RemoveEmitter(this);
        base.OnDestroy();
    }
}
