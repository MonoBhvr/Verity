using System.Numerics;
using Verity.Core.ECS;
using Verity.Core;
using Verity.Input;

namespace Verity.Core.Physics;

public class Physical : Component
{
    private float _mass = 1.0f;
    private float? _inertia = null;

    public float Mass 
    { 
        get => _mass; 
        set { _mass = value; } 
    }

    public float Inertia 
    { 
        get {
            if (_inertia.HasValue) return _inertia.Value;
            var shape = Owner.GetComponent<PhysicalShape>();
            if (shape != null) return _mass * shape.CalculateInertiaCoefficient();
            return _mass; // Fallback
        }
        set { _inertia = value; }
    }

    public Vector2 Velocity { get; set; } = Vector2.Zero;
    public float AngularVelocity { get; set; } = 0.0f;

    [HideInInspector]
    public float TorqueAccumulator { get; set; } = 0.0f;

    public float? LinearDamping { get; set; } = null;
    public float? AngularDamping { get; set; } = null;
    public float? Friction { get; set; } = null;
    public float? Bounciness { get; set; } = null;

    [SerializeField, PhysicsGroupSelector]
    public string GroupName { get; set; } = "Default";
    public ulong GroupMask => Filter.Get(GroupName)?.Mask ?? FilterRegistry.GetGroupMask(GroupName);

    public float GravityScale { get; set; } = 1.0f;
    public float SleepThreshold { get; set; } = 0.01f;
    public bool IsStatic { get; set; } = false;
    public bool IsRotationLocked { get; set; } = false;

    internal Vector2 ForceAccumulator { get; set; } = Vector2.Zero;
    internal bool IsSleeping { get; set; } = false;
    private float _sleepTimer = 0.0f;

    public void Push(Vector2 force)
    {
        if (IsStatic) return;
        if (force.LengthSquared() < 0.005f) return;
        ForceAccumulator += force;
        WakeUp();
    }

    public void PushTorque(float torque)
    {
        if (IsStatic || IsRotationLocked)
        {
            AngularVelocity = 0.0f; // 즉시 회전 멈춤
            return;
        }
        if (MathF.Abs(torque) < 0.005f) return;
        TorqueAccumulator += torque;
        WakeUp();
    }

    public void WakeUp()
    {
        IsSleeping = false;
        _sleepTimer = 0.0f;
    }

    public bool IsTouchingAnything() => PhysicsManager.IsTouchingAnything(this);
    public bool IsTouching(string groupName) => PhysicsManager.IsTouching(this, groupName);
    public bool IsTouchingGroup(string groupName) => PhysicsManager.IsTouching(this, groupName);
    public bool IsTouching(Entity entity) => PhysicsManager.IsTouching(this, entity);
    public bool IsGrounded(string groupName) => PhysicsManager.IsGrounded(this, groupName);
    public IEnumerable<Entity> GetTouchingEntities() => PhysicsManager.GetTouchingEntities(this);

    public bool IsTouchingDirection(Vector2 direction, string? groupName = null) => PhysicsManager.IsTouchingDirection(this, direction, groupName);
    public bool IsTouchingLocalDirection(Vector2 direction, string? groupName = null) => PhysicsManager.IsTouchingLocalDirection(this, direction, groupName);
    public int GetTouchingCountDirection(Vector2 direction, string? groupName = null) => PhysicsManager.GetTouchingCountDirection(this, direction, groupName);
    public int GetTouchingCountLocalDirection(Vector2 direction, string? groupName = null) => PhysicsManager.GetTouchingCountLocalDirection(this, direction, groupName);
    public IEnumerable<Entity> GetTouchingEntitiesDirection(Vector2 direction, string? groupName = null) => PhysicsManager.GetTouchingEntitiesDirection(this, direction, groupName);
    public IEnumerable<Entity> GetTouchingEntitiesLocalDirection(Vector2 direction, string? groupName = null) => PhysicsManager.GetTouchingEntitiesLocalDirection(this, direction, groupName);

    internal void UpdateSleepStatus(float deltaTime, float physicsThreshold)
    {
        if (IsStatic) return;

        // More strict threshold: using a fraction of the threshold for instantaneous zeroing
        // to prevent premature stopping, but using the full threshold for sleep timer.
        float sqrThreshold = physicsThreshold * physicsThreshold;
        float energy = Velocity.LengthSquared() + (AngularVelocity * AngularVelocity);

        if (energy <= sqrThreshold)
        {
            _sleepTimer += deltaTime;
            
            // Required 1.0 second of stillness (up from 0.5s)
            if (_sleepTimer > 1.0f)
            {
                IsSleeping = true;
                Velocity = Vector2.Zero;
                AngularVelocity = 0.0f;
            }
        }
        else
        {
            WakeUp();
        }
    }
}
