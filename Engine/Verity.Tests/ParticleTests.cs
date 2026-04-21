using Verity.Core;
using Verity.Core.ECS;
using SystemNumericsVector2 = System.Numerics.Vector2;

namespace Verity.Tests;

public sealed class ParticleTests : IDisposable
{
    public ParticleTests()
    {
        EventBus.Clear();
        ParticleSystem.Clear();
    }

    public void Dispose()
    {
        EventBus.Clear();
        ParticleSystem.Clear();
    }

    [Fact]
    public void Update_EmitsParticlesFromRate_AndPublishesEvents()
    {
        ParticleEmittedEvent[] receivedEvents = new ParticleEmittedEvent[2];
        int receivedCount = 0;
        var emitter = CreateEmitter(position: new SystemNumericsVector2(4f, -2f));
        emitter.Rate = 4f;

        EventBus.Subscribe<ParticleEmittedEvent>(OnParticleEmitted);

        ParticleSystem.Update(emitter, 0.5f);

        var particles = ParticleSystem.GetParticles(emitter);

        Assert.Equal(2, particles.Count);
        Assert.All(particles, particle => Assert.Equal(new SystemNumericsVector2(4f, -2f), particle.Position));
        Assert.Equal(2, receivedCount);
        Assert.All(receivedEvents, particleEvent => Assert.Same(emitter, particleEvent.Emitter));
        return;

        void OnParticleEmitted(ParticleEmittedEvent particleEvent)
        {
            receivedEvents[receivedCount++] = particleEvent;
        }
    }

    [Fact]
    public void Update_AgesParticles_AppliesDecay_AndReturnsExpiredParticlesToPool()
    {
        ParticleExpiredEvent? expiredEvent = null;
        var emitter = CreateEmitter();
        emitter.ParticleLifetime = 1f;
        emitter.ParticleSize = 2f;
        emitter.ParticleColor = Color.White;

        EventBus.Subscribe<ParticleExpiredEvent>(particleEvent => expiredEvent = particleEvent);

        ParticleSystem.Emit(emitter, 1);
        ParticleSystem.Update(emitter, 0.25f);

        var particle = Assert.Single(ParticleSystem.GetParticles(emitter));

        Assert.Equal(0.25f, particle.Age, 3);
        Assert.Equal(1.5f, particle.Size, 3);
        Assert.Equal(0.75f, particle.Color.A, 3);

        ParticleSystem.Update(emitter, 0.75f);

        Assert.Empty(ParticleSystem.GetParticles(emitter));
        Assert.Equal(1, ParticleSystem.GetPoolCount(emitter));
        Assert.NotNull(expiredEvent);
        Assert.Same(emitter, expiredEvent.Value.Emitter);
    }

    [Fact]
    public void Emit_SupportsPointCircleAndBoxPatterns()
    {
        var pointEmitter = CreateEmitter(position: new SystemNumericsVector2(3f, 6f));

        ParticleSystem.Emit(pointEmitter, 1);

        Assert.Equal(new SystemNumericsVector2(3f, 6f), Assert.Single(ParticleSystem.GetParticles(pointEmitter)).Position);

        var circleEmitter = CreateEmitter(position: new SystemNumericsVector2(10f, 20f));
        circleEmitter.EmissionShape = ParticleEmissionShape.Circle;
        circleEmitter.EmissionRadius = 2f;
        circleEmitter.RandomSeed = 7;

        ParticleSystem.Emit(circleEmitter, 16);

        Assert.All(
            ParticleSystem.GetParticles(circleEmitter),
            particle => Assert.True(SystemNumericsVector2.Distance(circleEmitter.Owner.Transform.WorldPosition, particle.Position) <= 2f + 0.0001f));

        var boxEmitter = CreateEmitter(position: new SystemNumericsVector2(-4f, 8f));
        boxEmitter.EmissionShape = ParticleEmissionShape.Box;
        boxEmitter.EmissionBoxSize = new SystemNumericsVector2(6f, 4f);
        boxEmitter.RandomSeed = 11;

        ParticleSystem.Emit(boxEmitter, 16);

        Assert.All(
            ParticleSystem.GetParticles(boxEmitter),
            particle =>
            {
                SystemNumericsVector2 offset = particle.Position - boxEmitter.Owner.Transform.WorldPosition;
                Assert.InRange(offset.X, -3f, 3f);
                Assert.InRange(offset.Y, -2f, 2f);
            });
    }

    [Fact]
    public void Emit_ReusesReturnedParticleSlots()
    {
        var emitter = CreateEmitter();
        emitter.ParticleLifetime = 0.25f;

        ParticleSystem.Emit(emitter, 1);

        Assert.Equal(1, ParticleSystem.GetCreatedSlotCount(emitter));

        ParticleSystem.Update(emitter, 0.25f);
        ParticleSystem.Emit(emitter, 1);

        Assert.Equal(1, ParticleSystem.GetCreatedSlotCount(emitter));
        Assert.Equal(1, ParticleSystem.GetActiveCount(emitter));
        Assert.Equal(0, ParticleSystem.GetPoolCount(emitter));
    }

    [Fact]
    public void Emit_RespectsMaxParticleCount()
    {
        var emitter = CreateEmitter();
        emitter.MaxParticles = 2;

        ParticleSystem.Emit(emitter, 5);

        Assert.Equal(2, ParticleSystem.GetActiveCount(emitter));
    }

    private static ParticleEmitter CreateEmitter(SystemNumericsVector2? position = null)
    {
        var entity = new Entity("Emitter");
        entity.Transform.Position = position ?? SystemNumericsVector2.Zero;

        var emitter = entity.AddComponent<ParticleEmitter>();
        emitter.Rate = 0f;
        emitter.ParticleLifetime = 1f;
        emitter.ParticleSize = 1f;
        emitter.ParticleColor = Color.White;
        emitter.Gravity = SystemNumericsVector2.Zero;
        emitter.InitialVelocity = SystemNumericsVector2.Zero;
        emitter.MaxParticles = 64;
        emitter.RandomSeed = 1;
        return emitter;
    }
}
