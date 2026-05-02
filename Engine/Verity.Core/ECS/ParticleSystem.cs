using System.Numerics;
using Verity.Core.ECS;

namespace Verity.Core;

public readonly record struct ParticleEmittedEvent(ParticleEmitter Emitter, Particle Particle);

public readonly record struct ParticleExpiredEvent(ParticleEmitter Emitter, Particle Particle);

public static class ParticleSystem
{
    private static readonly Dictionary<ParticleEmitter, EmitterState> States = [];

    public static void Update(ParticleEmitter emitter, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentOutOfRangeException.ThrowIfNegative(deltaTime);

        EmitterState state = GetOrCreateState(emitter);

        UpdateParticles(emitter, state, deltaTime);

        if (emitter.Enabled && emitter.Rate > 0f)
        {
            state.EmissionRemainder += emitter.Rate * deltaTime;
            int emitCount = (int)state.EmissionRemainder;
            state.EmissionRemainder -= emitCount;
            EmitInternal(emitter, state, emitCount);
        }
    }

    public static void Emit(ParticleEmitter emitter, int count)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        EmitInternal(emitter, GetOrCreateState(emitter), count);
    }

    public static IReadOnlyList<Particle> GetParticles(ParticleEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);

        if (!States.TryGetValue(emitter, out EmitterState? state))
            return [];

        Particle[] particles = new Particle[state.ActiveParticles.Count];
        for (int i = 0; i < state.ActiveParticles.Count; i++)
            particles[i] = state.ActiveParticles[i].Particle;

        return particles;
    }

    public static int GetActiveCount(ParticleEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        return States.TryGetValue(emitter, out EmitterState? state) ? state.ActiveParticles.Count : 0;
    }

    public static int GetPoolCount(ParticleEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        return States.TryGetValue(emitter, out EmitterState? state) ? state.Pool.Count : 0;
    }

    public static int GetCreatedSlotCount(ParticleEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        return States.TryGetValue(emitter, out EmitterState? state) ? state.CreatedSlotCount : 0;
    }

    public static void Clear()
    {
        States.Clear();
    }

    public static void RemoveEmitter(ParticleEmitter emitter)
    {
        if (emitter == null)
            return;

        States.Remove(emitter);
    }

    public static void UpdateAll(Verity.Core.World.World world, float deltaTime)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfNegative(deltaTime);

        var entities = world.GetAllEntities();
        for (int i = 0; i < entities.Count; i++)
        {
            Entity entity = entities[i];
            if (!entity.Active)
                continue;

            if (entity.GetComponent<ParticleEmitter>() is ParticleEmitter emitter && emitter.Enabled)
                Update(emitter, deltaTime);
        }
    }

    private static EmitterState GetOrCreateState(ParticleEmitter emitter)
    {
        if (States.TryGetValue(emitter, out EmitterState? state))
            return state;

        state = new EmitterState(emitter.RandomSeed);
        States[emitter] = state;
        return state;
    }

    private static void EmitInternal(ParticleEmitter emitter, EmitterState state, int requestedCount)
    {
        if (requestedCount <= 0)
            return;

        int maxParticles = Math.Max(0, emitter.MaxParticles);
        int availableSlots = maxParticles - state.ActiveParticles.Count;
        int emitCount = Math.Min(requestedCount, availableSlots);

        for (int i = 0; i < emitCount; i++)
        {
            ParticleSlot slot = state.Pool.Get();
            slot.InitialSize = emitter.ParticleSize;
            slot.InitialColor = emitter.ParticleColor;
            slot.Particle = new Particle
            {
                Position = SamplePosition(emitter, state.Random),
                Velocity = emitter.InitialVelocity,
                Lifetime = Math.Max(0.0001f, emitter.ParticleLifetime),
                Age = 0f,
                Color = emitter.ParticleColor,
                Size = emitter.ParticleSize
            };

            state.ActiveParticles.Add(slot);
            EventBus.Publish(new ParticleEmittedEvent(emitter, slot.Particle));
        }
    }

    private static void UpdateParticles(ParticleEmitter emitter, EmitterState state, float deltaTime)
    {
        for (int i = state.ActiveParticles.Count - 1; i >= 0; i--)
        {
            ParticleSlot slot = state.ActiveParticles[i];
            Particle particle = slot.Particle;

            particle.Age += deltaTime;

            if (particle.Age >= particle.Lifetime)
            {
                particle.Age = particle.Lifetime;
                slot.Particle = particle;
                state.ActiveParticles.RemoveAt(i);
                EventBus.Publish(new ParticleExpiredEvent(emitter, particle));
                state.Pool.Return(slot);
                continue;
            }

            particle.Velocity += emitter.Gravity * deltaTime;
            particle.Position += particle.Velocity * deltaTime;

            float decay = 1f - (particle.Age / particle.Lifetime);
            particle.Size = slot.InitialSize * decay;

            Color color = slot.InitialColor;
            color.A *= decay;
            particle.Color = color;

            slot.Particle = particle;
        }
    }

    private static Vector2 SamplePosition(ParticleEmitter emitter, Random random)
    {
        Vector2 center = emitter.Owner.Transform.WorldPosition;

        return emitter.EmissionShape switch
        {
            ParticleEmissionShape.Point => center,
            ParticleEmissionShape.Circle => center + SampleCircleOffset(random, MathF.Max(0f, emitter.EmissionRadius)),
            ParticleEmissionShape.Box => center + SampleBoxOffset(random, emitter.EmissionBoxSize),
            _ => center
        };
    }

    private static Vector2 SampleCircleOffset(Random random, float radius)
    {
        if (radius <= 0f)
            return Vector2.Zero;

        double angle = random.NextDouble() * (Math.PI * 2d);
        float distance = radius * MathF.Sqrt((float)random.NextDouble());
        return new Vector2(MathF.Cos((float)angle), MathF.Sin((float)angle)) * distance;
    }

    private static Vector2 SampleBoxOffset(Random random, Vector2 boxSize)
    {
        Vector2 halfSize = new(MathF.Abs(boxSize.X) * 0.5f, MathF.Abs(boxSize.Y) * 0.5f);
        return new Vector2(
            ((float)random.NextDouble() * 2f - 1f) * halfSize.X,
            ((float)random.NextDouble() * 2f - 1f) * halfSize.Y);
    }

    private sealed class EmitterState
    {
        private int _nextSlotId = 1;

        public EmitterState(int randomSeed)
        {
            Random = new Random(randomSeed);
            Pool = new ObjectPool<ParticleSlot>(CreateSlot, onReturn: ResetSlot);
        }

        public List<ParticleSlot> ActiveParticles { get; } = [];

        public ObjectPool<ParticleSlot> Pool { get; }

        public Random Random { get; }

        public float EmissionRemainder { get; set; }

        public int CreatedSlotCount { get; private set; }

        private ParticleSlot CreateSlot()
        {
            CreatedSlotCount++;
            return new ParticleSlot { SlotId = _nextSlotId++ };
        }

        private static void ResetSlot(ParticleSlot slot)
        {
            slot.Particle = default;
            slot.InitialSize = 0f;
            slot.InitialColor = default;
        }
    }

    private sealed class ParticleSlot
    {
        public int SlotId { get; init; }

        public Particle Particle { get; set; }

        public float InitialSize { get; set; }

        public Color InitialColor { get; set; }
    }
}
