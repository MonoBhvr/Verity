using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core.Engine;

namespace Verity.Core.Physics;

public static class PhysicsManager
{
    private static SpatialHashGrid _grid = new();
    private static List<Physical> _activePhysicals = new();
    private static List<Physical> _staticColliders = new();
    
    private static Dictionary<(Guid, Guid), List<Contact>> _currentContacts = new();
    private static Dictionary<(Guid, Guid), List<Contact>> _previousContacts = new();
    private static ulong[] _collisionMatrix = Enumerable.Repeat(ulong.MaxValue, 64).ToArray();

    public static ProjectSettings Settings { get; set; } = ProjectSettings.Default;

    public struct Contact
    {
        public Physical A;
        public Physical B;
        public Vector2 Normal;
        public float Depth;
        public Vector2 Point;
    }

    public static Vector2 Gravity { get; set; } = new Vector2(0, -9.81f);
    public static ulong[] CollisionMatrix { get => _collisionMatrix; set => _collisionMatrix = value; }

    public static bool CanCollide(ulong maskA, ulong maskB)
    {
        for (int i = 0; i < 64; i++)
        {
            if ((maskA & (1UL << i)) != 0)
            {
                if ((_collisionMatrix[i] & maskB) != 0) return true;
            }
        }
        return false;
    }

    public static void Step(float deltaTime, World.World world)
    {
        const int subSteps = 4;
        float subDeltaTime = deltaTime / subSteps;

        Gravity = world.UseCustomSettings ? world.CustomGravity : Settings.DefaultGravity;
        CollectObjects(world);

        _previousContacts = _currentContacts;
        _currentContacts = new Dictionary<(Guid, Guid), List<Contact>>();

        for (int step = 0; step < subSteps; step++)
        {
            foreach (var p in _activePhysicals)
            {
                if (p.IsSleeping) continue;

                Vector2 acceleration = (p.ForceAccumulator / p.Mass) + (Gravity * p.GravityScale);
                p.Velocity += acceleration * subDeltaTime;
                
                float linearDamping = p.LinearDamping ?? (world.UseCustomSettings ? world.CustomLinearDamping : Settings.DefaultLinearDamping);
                p.Velocity *= MathF.Exp(-linearDamping * subDeltaTime);

                var transform = p.Owner.Transform;
                if (transform != null)
                {
                    transform.Position += p.Velocity * subDeltaTime;

                    if (!p.IsRotationLocked)
                    {
                        float angularAcceleration = p.TorqueAccumulator / p.Inertia;
                        p.AngularVelocity += angularAcceleration * subDeltaTime;
                        
                        float angularDamping = p.AngularDamping ?? (world.UseCustomSettings ? world.CustomAngularDamping : Settings.DefaultAngularDamping);
                        p.AngularVelocity *= MathF.Exp(-angularDamping * subDeltaTime);
                        transform.Rotation += p.AngularVelocity * (180.0f / MathF.PI) * subDeltaTime;
                    }
                }

                p.ForceAccumulator = Vector2.Zero;
                p.TorqueAccumulator = 0.0f;
            }

            _grid.Clear();
            foreach (var p in _activePhysicals) _grid.Add(p);
            foreach (var p in _staticColliders) _grid.Add(p);

            var foundContacts = FindContacts();
            ResolveContacts(foundContacts, world);
        }

        foreach (var p in _activePhysicals) 
            p.UpdateSleepStatus(deltaTime, world.UseCustomSettings ? world.CustomPhysicsThreshold : Settings.DefaultPhysicsThreshold);

        DispatchEvents();
    }

    private static void CollectObjects(World.World world)
    {
        _activePhysicals.Clear();
        _staticColliders.Clear();

        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            var physical = entity.GetComponent<Physical>();
            var shape = entity.GetComponent<PhysicalShape>();

            if (physical != null && physical.Enabled) _activePhysicals.Add(physical);
            else if (shape != null && shape.Enabled)
            {
                var virtualPhysical = new Physical { 
                    Owner = entity, 
                    IsStatic = true, 
                    GroupName = shape.GroupName 
                };
                _staticColliders.Add(virtualPhysical);
            }
        }
    }

    private static List<Contact> FindContacts()
    {
        var allContacts = new List<Contact>();
        var checkedPairs = new HashSet<(Guid, Guid)>();

        foreach (var a in _activePhysicals)
        {
            var potentials = _grid.GetPotentialCollisions(a);
            foreach (var b in potentials)
            {
                Guid idA = a.Owner.Id;
                Guid idB = b.Owner.Id;
                var pair = idA.CompareTo(idB) < 0 ? (idA, idB) : (idB, idA);
                if (checkedPairs.Contains(pair)) continue;
                checkedPairs.Add(pair);

                if (!CanCollide(a.GroupMask, b.GroupMask)) continue;

                var shapeA = a.Owner.GetComponent<PhysicalShape>();
                var shapeB = b.Owner.GetComponent<PhysicalShape>();
                if (shapeA == null || shapeB == null) continue;

                var result = PhysicsMath.TestSAT(shapeA, shapeB);
                if (result.IsColliding)
                {
                    var pairContacts = new List<Contact>();
                    foreach (var p in result.Contacts)
                    {
                        var contact = new Contact { A = a, B = b, Normal = result.Normal, Depth = result.Depth, Point = p };
                        pairContacts.Add(contact);
                        allContacts.Add(contact);
                    }
                    _currentContacts[pair] = pairContacts;
                }
            }
        }
        return allContacts;
    }

    private static void ResolveContacts(List<Contact> contacts, World.World world)
    {
        if (contacts.Count == 0) return;

        // 1. Positional Correction (Separation)
        // Reduced percent and increased slop to minimize jitter
        var checkedPairs = new HashSet<(Guid, Guid)>();
        foreach (var contact in contacts)
        {
            var a = contact.A;
            var b = contact.B;
            var pair = a.Owner.Id.CompareTo(b.Owner.Id) < 0 ? (a.Owner.Id, b.Owner.Id) : (b.Owner.Id, a.Owner.Id);
            if (checkedPairs.Contains(pair)) continue;
            checkedPairs.Add(pair);

            float invMassA = a.IsStatic ? 0 : 1.0f / a.Mass;
            float invMassB = b.IsStatic ? 0 : 1.0f / b.Mass;
            float totalInvMass = invMassA + invMassB;
            if (totalInvMass == 0) continue;

            float percent = 0.2f; // Low responsiveness to prevent oscillation
            float slop = 0.01f;   // Higher tolerance for overlap
            Vector2 mtv = contact.Normal * (MathF.Max(contact.Depth - slop, 0.0f) / totalInvMass * percent);
            if (!a.IsStatic) a.Owner.Transform.Position -= mtv * invMassA;
            if (!b.IsStatic) b.Owner.Transform.Position += mtv * invMassB;
        }

        // 2. Impulse & Friction resolution
        var pairs = contacts.GroupBy(c => c.A.Owner.Id.CompareTo(c.B.Owner.Id) < 0 ? (c.A.Owner.Id, c.B.Owner.Id) : (c.B.Owner.Id, c.A.Owner.Id));

        foreach (var group in pairs)
        {
            var contactList = group.ToList();
            int contactCount = contactList.Count;

            foreach (var contact in contactList)
            {
                var a = contact.A;
                var b = contact.B;
                
                float invMassA = a.IsStatic ? 0 : 1.0f / a.Mass;
                float invMassB = b.IsStatic ? 0 : 1.0f / b.Mass;
                float invInertiaA = (a.IsStatic || a.IsRotationLocked) ? 0 : 1.0f / a.Inertia;
                float invInertiaB = (b.IsStatic || b.IsRotationLocked) ? 0 : 1.0f / b.Inertia;
                float totalInvMass = invMassA + invMassB;

                Vector2 rA = contact.Point - a.Owner.Transform.Position;
                Vector2 rB = contact.Point - b.Owner.Transform.Position;

                Vector2 vA = a.Velocity + new Vector2(-a.AngularVelocity * rA.Y, a.AngularVelocity * rA.X);
                Vector2 vB = b.Velocity + new Vector2(-b.AngularVelocity * rB.Y, b.AngularVelocity * rB.X);
                Vector2 relativeVelocity = vB - vA;

                float velocityAlongNormal = Vector2.Dot(relativeVelocity, contact.Normal);
                if (velocityAlongNormal > 0) continue;

                float rAN = Cross(rA, contact.Normal);
                float rBN = Cross(rB, contact.Normal);

                float bouncinessA = a.Bounciness ?? (world.UseCustomSettings ? world.CustomBounciness : Settings.DefaultBounciness);
                float bouncinessB = b.Bounciness ?? (world.UseCustomSettings ? world.CustomBounciness : Settings.DefaultBounciness);
                float e = Math.Min(bouncinessA, bouncinessB);
                
                // Restitution threshold: increased to 0.5f to kill bounce at low speeds
                if (MathF.Abs(velocityAlongNormal) < 0.5f) e = 0;

                float j = -(1 + e) * velocityAlongNormal;
                j /= (totalInvMass + (rAN * rAN * invInertiaA) + (rBN * rBN * invInertiaB));
                j /= (float)contactCount; 

                Vector2 impulse = j * contact.Normal;
                ApplyImpulse(a, -impulse, rA);
                ApplyImpulse(b, impulse, rB);

                // Friction
                vA = a.Velocity + new Vector2(-a.AngularVelocity * rA.Y, a.AngularVelocity * rA.X);
                vB = b.Velocity + new Vector2(-b.AngularVelocity * rB.Y, b.AngularVelocity * rB.X);
                relativeVelocity = vB - vA;

                Vector2 tangent = relativeVelocity - (contact.Normal * Vector2.Dot(relativeVelocity, contact.Normal));
                if (tangent.LengthSquared() > 0.0001f)
                {
                    tangent = Vector2.Normalize(tangent);
                    float rAT = Cross(rA, tangent);
                    float rBT = Cross(rB, tangent);
                    
                    float jt = -Vector2.Dot(relativeVelocity, tangent);
                    jt /= (totalInvMass + (rAT * rAT * invInertiaA) + (rBT * rBT * invInertiaB));
                    jt /= (float)contactCount;
                    
                    float frictionA = a.Friction ?? (world.UseCustomSettings ? world.CustomFriction : Settings.DefaultFriction);
                    float frictionB = b.Friction ?? (world.UseCustomSettings ? world.CustomFriction : Settings.DefaultFriction);
                    float friction = MathF.Sqrt(frictionA * frictionB);
                    
                    jt = Math.Clamp(jt, -j * friction, j * friction);
                    
                    Vector2 frictionImpulse = jt * tangent;
                    ApplyImpulse(a, -frictionImpulse, rA);
                    ApplyImpulse(b, frictionImpulse, rB);
                }
            }
        }
    }

    private static void ApplyImpulse(Physical p, Vector2 impulse, Vector2 r)
    {
        if (p.IsStatic) return;
        p.Velocity += impulse / p.Mass;
        if (!p.IsRotationLocked) p.AngularVelocity += Cross(r, impulse) / p.Inertia;
        
        // Micro-velocity snapping to prevent jitter
        if (p.Velocity.LengthSquared() < 0.0001f) p.Velocity = Vector2.Zero;
        if (MathF.Abs(p.AngularVelocity) < 0.001f) p.AngularVelocity = 0f;

        p.WakeUp();
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static void DispatchEvents()
    {
        foreach (var pair in _currentContacts)
        {
            var contact = pair.Value[0];
            bool isFirstTouch = !_previousContacts.ContainsKey(pair.Key);
            InvokePhysicsEvent(contact.A.Owner, contact.B.Owner, contact.B, isFirstTouch);
            InvokePhysicsEvent(contact.B.Owner, contact.A.Owner, contact.A, isFirstTouch);
        }
        foreach (var pair in _previousContacts)
        {
            if (!_currentContacts.ContainsKey(pair.Key))
            {
                var contact = pair.Value[0];
                InvokeTouchEnd(contact.A.Owner, contact.B.Owner);
                InvokeTouchEnd(contact.B.Owner, contact.A.Owner);
            }
        }
    }

    private static void InvokePhysicsEvent(Entity entity, Entity otherEntity, Physical otherPhysical, bool isFirst)
    {
        var shape = entity.GetComponent<PhysicalShape>();
        bool isSensor = shape?.IsSensor ?? false;
        var scripts = entity.GetComponents<Script>();
        foreach (var script in scripts)
        {
            if (isSensor) { if (isFirst) script._onDetectedDelegate?.Invoke(otherEntity); else script._onDetectingDelegate?.Invoke(otherEntity); }
            else { if (isFirst) script._onTouchedDelegate?.Invoke(otherPhysical); else script._onTouchingDelegate?.Invoke(otherPhysical); }
        }
    }

    private static void InvokeTouchEnd(Entity entity, Entity otherEntity)
    {
        var shape = entity.GetComponent<PhysicalShape>();
        bool isSensor = shape?.IsSensor ?? false;
        var scripts = entity.GetComponents<Script>();
        foreach (var script in scripts)
        {
            if (isSensor) script._onDetectEndDelegate?.Invoke(otherEntity);
            else script._onTouchEndDelegate?.Invoke(otherEntity);
        }
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, ulong mask)
    {
        PhysicsMath.RaycastHit closestHit = new() { IsHit = false, Distance = float.MaxValue };
        foreach (var physical in _activePhysicals.Concat(_staticColliders))
        {
            if ((physical.GroupMask & mask) == 0) continue;
            var shape = physical.Owner.GetComponent<PhysicalShape>();
            if (shape == null) continue;
            var hit = PhysicsMath.TestRay(origin, direction, distance, shape);
            if (hit.IsHit && hit.Distance < closestHit.Distance) closestHit = hit;
        }
        return closestHit.IsHit ? closestHit : new PhysicsMath.RaycastHit { IsHit = false };
    }

    public static IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, ulong mask)
    {
        var result = new List<Entity>();
        var circleAABB = new AABB(center - new Vector2(radius), center + new Vector2(radius));
        foreach (var physical in _activePhysicals.Concat(_staticColliders))
        {
            if ((physical.GroupMask & mask) == 0) continue;
            var shape = physical.Owner.GetComponent<PhysicalShape>();
            if (shape == null) continue;
            if (shape.GetAABB().Overlaps(circleAABB)) result.Add(physical.Owner);
        }
        return result;
    }

    public static IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, ulong mask)
    {
        var result = new List<Entity>();
        var halfSize = size / 2.0f;
        var boxAABB = new AABB(center - halfSize, center + halfSize);
        foreach (var physical in _activePhysicals.Concat(_staticColliders))
        {
            if ((physical.GroupMask & mask) == 0) continue;
            var shape = physical.Owner.GetComponent<PhysicalShape>();
            if (shape == null) continue;
            if (shape.GetAABB().Overlaps(boxAABB)) result.Add(physical.Owner);
        }
        return result;
    }

    public static void DrawGizmos(World.World world)
    {
        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            var shape = entity.GetComponent<PhysicalShape>();
            if (shape == null || !shape.Enabled) continue;
            var physical = entity.GetComponent<Physical>();
            var color = shape.IsSensor ? Color.Blue : (physical != null && IsTouchingAnything(physical) ? Color.Red : Color.Green);
            if (shape is CircleShape circle) DrawCircleGizmo(circle, color);
            else {
                var vertices = shape.GetVertices();
                if (vertices.Length < 2) continue;
                for (int i = 0; i < vertices.Length; i++) 
                    Verity.Core.Debug.DrawLine(vertices[i], vertices[(i + 1) % vertices.Length], color, 0.02f);
            }
        }
    }

    private static void DrawCircleGizmo(CircleShape circle, Color color)
    {
        var transform = circle.Owner.Transform;
        Vector2 worldScale = transform.Scale;
        float scaledRadius = circle.Radius * Math.Max(worldScale.X, worldScale.Y);
        float rotationRad = transform.Rotation * MathF.PI / 180.0f;
        float cos = MathF.Cos(rotationRad); float sin = MathF.Sin(rotationRad);
        Vector2 rotatedOffset = new Vector2(circle.Offset.X * worldScale.X * cos - circle.Offset.Y * worldScale.Y * sin, circle.Offset.X * worldScale.X * sin + circle.Offset.Y * worldScale.Y * cos);
        Vector2 center = transform.Position + rotatedOffset;
        const int segments = 16;
        for (int i = 0; i < segments; i++) {
            float a1 = (float)i / segments * MathF.PI * 2; float a2 = (float)(i + 1) / segments * MathF.PI * 2;
            Vector2 p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * scaledRadius;
            Vector2 p2 = center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * scaledRadius;
            Verity.Core.Debug.DrawLine(p1, p2, color, 0.02f);
        }
    }

    public static bool IsTouchingAnything(Physical p) { Guid id = p.Owner.Id; return _currentContacts.Keys.Any(k => k.Item1 == id || k.Item2 == id); }
    public static IEnumerable<Entity> GetTouchingEntities(Physical p) { Guid id = p.Owner.Id; foreach (var pair in _currentContacts) { if (pair.Key.Item1 == id) yield return pair.Value[0].B.Owner; else if (pair.Key.Item2 == id) yield return pair.Value[0].A.Owner; } }
    public static bool IsTouching(Physical p, string groupName) { var groupMask = Verity.Input.Filter.Get(groupName)?.Mask ?? 0; return GetTouchingEntities(p).Any(e => (e.GetComponent<Physical>()?.GroupMask & groupMask) != 0); }
    public static bool IsTouching(Physical p, Entity target) => GetTouchingEntities(p).Any(e => e == target);
}
