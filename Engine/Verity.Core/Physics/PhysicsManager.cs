using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core.Engine;

namespace Verity.Core.Physics;

public static class PhysicsManager
{
    private static SpatialHashGrid _grid = new();
    private static List<Physical> _activePhysicals = new();
    private static List<Physical> _staticShapes = new();
    private static Dictionary<Physical, List<PhysicalShape>> _physicalShapes = new();
    
    private static Dictionary<(Guid, Guid), List<Contact>> _currentContacts = new();
    private static Dictionary<(Guid, Guid), List<Contact>> _previousContacts = new();
    private static ulong[] _collisionMatrix = Enumerable.Repeat(ulong.MaxValue, 64).ToArray();

    public struct Contact
    {
        public Physical A;
        public Physical B;
        public Vector2 Normal;
        public float Depth;
        public Vector2 Point;
    }

    public static Vector2 Gravity { get; set; } = Vector2.Zero;
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

    public static void Step(float deltaTime, World.World world, ProjectSettings settings)
    {
        const int subSteps = 8;
        float subDeltaTime = deltaTime / subSteps;

        Gravity = world.UseCustomSettings ? world.CustomGravity : settings.DefaultGravity;
        CollectObjects(world);

        _previousContacts = _currentContacts;
        _currentContacts = new Dictionary<(Guid, Guid), List<Contact>>();

        for (int step = 0; step < subSteps; step++)
        {
            foreach (var p in _activePhysicals)
            {
                if (p.IsSleeping) continue;
                if (p.ForceAccumulator.LengthSquared() < 0.001f) p.ForceAccumulator = Vector2.Zero;
                if (MathF.Abs(p.TorqueAccumulator) < 0.001f) p.TorqueAccumulator = 0f;

                float mass = MathF.Max(0.0001f, p.Mass);
                Vector2 acceleration = (p.ForceAccumulator / mass) + (Gravity * p.GravityScale);
                p.Velocity += acceleration * subDeltaTime;
                
                float linearDamping = p.LinearDamping ?? (world.UseCustomSettings ? world.CustomLinearDamping : settings.DefaultLinearDamping);
                p.Velocity *= MathF.Exp(-linearDamping * subDeltaTime);

                var transform = p.Owner.Transform;
                if (transform != null)
                {
                    transform.WorldPosition += p.Velocity * subDeltaTime;
                    if (!p.IsRotationLocked)
                    {
                        float inertia = MathF.Max(0.0001f, p.Inertia);
                        float angularAcceleration = p.TorqueAccumulator / inertia;
                        p.AngularVelocity += angularAcceleration * subDeltaTime;
                        float angularDamping = p.AngularDamping ?? (world.UseCustomSettings ? world.CustomAngularDamping : settings.DefaultAngularDamping);
                        p.AngularVelocity *= MathF.Exp(-angularDamping * subDeltaTime);
                        transform.WorldRotation += p.AngularVelocity * (180.0f / MathF.PI) * subDeltaTime;
                    }
                }
                p.ForceAccumulator = Vector2.Zero;
                p.TorqueAccumulator = 0.0f;
            }

            _grid.Clear();
            foreach (var p in _activePhysicals) _grid.Add(p, _physicalShapes[p]);
            foreach (var p in _staticShapes) _grid.Add(p, _physicalShapes[p]);

            var foundContacts = FindContacts();
            ResolveContacts(foundContacts, world, settings);

            foreach (var contact in foundContacts)
            {
                var pair = contact.A.Owner.Id.CompareTo(contact.B.Owner.Id) < 0 ? (contact.A.Owner.Id, contact.B.Owner.Id) : (contact.B.Owner.Id, contact.A.Owner.Id);
                if (!_currentContacts.TryGetValue(pair, out var pairContacts)) { pairContacts = new List<Contact>(); _currentContacts[pair] = pairContacts; }
                pairContacts.Add(contact);
            }
        }

        foreach (var p in _activePhysicals) p.UpdateSleepStatus(deltaTime, world.UseCustomSettings ? world.CustomPhysicsThreshold : settings.DefaultPhysicsThreshold);
        DispatchEvents();
    }

    private static void CollectObjects(World.World world)
    {
        _activePhysicals.Clear(); _staticShapes.Clear(); _physicalShapes.Clear();
        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            var physical = entity.GetComponent<Physical>();
            if (physical != null && physical.Enabled)
            {
                if (physical.IsStatic) _staticShapes.Add(physical); else _activePhysicals.Add(physical);
                _physicalShapes[physical] = new List<PhysicalShape>();
            }
        }
        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            var shape = entity.GetComponent<PhysicalShape>();
            if (shape == null || !shape.Enabled) continue;
            Physical? nearestPhysical = FindNearestPhysicalAncestor(entity);
            if (nearestPhysical != null) { if (_physicalShapes.ContainsKey(nearestPhysical)) _physicalShapes[nearestPhysical].Add(shape); }
            else
            {
                var virtualPhysical = new Physical { Owner = entity, IsStatic = true, GroupName = shape.GroupName };
                _staticShapes.Add(virtualPhysical); _physicalShapes[virtualPhysical] = new List<PhysicalShape> { shape };
            }
        }
    }

    private static Physical? FindNearestPhysicalAncestor(Entity entity)
    {
        var current = entity;
        while (current != null)
        {
            var p = current.GetComponent<Physical>();
            if (p != null && p.Enabled) return p;
            current = current.Transform.Parent?.Owner;
        }
        return null;
    }

    private static List<Contact> FindContacts()
    {
        var subStepContacts = new List<Contact>();
        var checkedPairs = new HashSet<(Guid, Guid)>();
        foreach (var a in _activePhysicals)
        {
            var potentials = _grid.GetPotentialCollisions(a, _physicalShapes[a]);
            foreach (var b in potentials)
            {
                var pair = a.Owner.Id.CompareTo(b.Owner.Id) < 0 ? (a.Owner.Id, b.Owner.Id) : (b.Owner.Id, a.Owner.Id);
                if (checkedPairs.Contains(pair)) continue;
                checkedPairs.Add(pair);
                if (!CanCollide(a.GroupMask, b.GroupMask)) continue;
                if (!_physicalShapes.TryGetValue(a, out var shapesA) || !_physicalShapes.TryGetValue(b, out var shapesB)) continue;
                foreach (var sA in shapesA)
                {
                    foreach (var sB in shapesB)
                    {
                        if (sA is CircleShape && sB is CircleShape)
                        {
                            var result = PhysicsMath.TestSAT(sA, sB);
                            if (result.IsColliding) AddSubStepContacts(subStepContacts, a, b, result);
                            continue;
                        }
                        var subA = (sA is PolygonShape psA) ? psA.GetConvexSubShapes() : new List<Vector2[]> { sA.GetVertices() };
                        var subB = (sB is PolygonShape psB) ? psB.GetConvexSubShapes() : new List<Vector2[]> { sB.GetVertices() };
                        if (sA is CircleShape cA)
                        {
                            foreach (var vB in subB)
                            {
                                if (vB.Length == 0) continue;
                                var result = PhysicsMath.TestSAT(cA, vB);
                                if (result.IsColliding) AddSubStepContacts(subStepContacts, a, b, result);
                            }
                        }
                        else if (sB is CircleShape cB)
                        {
                            foreach (var vA in subA)
                            {
                                if (vA.Length == 0) continue;
                                var result = PhysicsMath.TestSAT(cB, vA);
                                if (result.IsColliding) { result.Normal = -result.Normal; AddSubStepContacts(subStepContacts, a, b, result); }
                            }
                        }
                        else
                        {
                            foreach (var vA in subA)
                            {
                                foreach (var vB in subB)
                                {
                                    if (vA.Length == 0 || vB.Length == 0) continue;
                                    var result = PhysicsMath.TestSAT(vA, vB);
                                    if (result.IsColliding) AddSubStepContacts(subStepContacts, a, b, result);
                                }
                            }
                        }
                    }
                }
            }
        }
        return subStepContacts;
    }

    private static void ResolveContacts(List<Contact> contacts, World.World world, ProjectSettings settings)
    {
        if (contacts.Count == 0) return;
        var pairs = contacts.GroupBy(c => c.A.Owner.Id.CompareTo(c.B.Owner.Id) < 0 ? (c.A.Owner.Id, c.B.Owner.Id) : (c.B.Owner.Id, c.A.Owner.Id));
        foreach (var group in pairs)
        {
            var contactList = group.ToList();
            int contactCount = contactList.Count;
            // 1. Positional Correction (using max depth to avoid over-correction)
            var maxDepthContact = contactList.OrderByDescending(c => c.Depth).First();
            float invMassA = maxDepthContact.A.IsStatic ? 0 : 1.0f / maxDepthContact.A.Mass;
            float invMassB = maxDepthContact.B.IsStatic ? 0 : 1.0f / maxDepthContact.B.Mass;
            float totalInvMass = invMassA + invMassB;
            if (totalInvMass > 0)
            {
                float percent = 0.4f, slop = 0.01f;
                Vector2 mtv = maxDepthContact.Normal * (MathF.Max(maxDepthContact.Depth - slop, 0.0f) / totalInvMass * percent);
                if (!maxDepthContact.A.IsStatic) maxDepthContact.A.Owner.Transform.WorldPosition -= mtv * invMassA;
                if (!maxDepthContact.B.IsStatic) maxDepthContact.B.Owner.Transform.WorldPosition += mtv * invMassB;
            }
            // 2. Impulse & Friction resolution
            foreach (var contact in contactList)
            {
                var a = contact.A; var b = contact.B;
                float iMA = a.IsStatic ? 0 : 1.0f / a.Mass, iMB = b.IsStatic ? 0 : 1.0f / b.Mass;
                float iIA = (a.IsStatic || a.IsRotationLocked) ? 0 : 1.0f / a.Inertia, iIB = (b.IsStatic || b.IsRotationLocked) ? 0 : 1.0f / b.Inertia;
                float tIM = iMA + iMB;
                Vector2 rA = contact.Point - a.Owner.Transform.WorldPosition, rB = contact.Point - b.Owner.Transform.WorldPosition;
                Vector2 vA = a.Velocity + new Vector2(-a.AngularVelocity * rA.Y, a.AngularVelocity * rA.X);
                Vector2 vB = b.Velocity + new Vector2(-b.AngularVelocity * rB.Y, b.AngularVelocity * rB.X);
                Vector2 relV = vB - vA;
                float velN = Vector2.Dot(relV, contact.Normal);
                if (velN > 0) continue;
                float rAN = Cross(rA, contact.Normal), rBN = Cross(rB, contact.Normal);
                float e = Math.Max(a.Bounciness ?? (world.UseCustomSettings ? world.CustomBounciness : settings.DefaultBounciness), b.Bounciness ?? (world.UseCustomSettings ? world.CustomBounciness : settings.DefaultBounciness));
                if (MathF.Abs(velN) < 0.1f) e = 0;
                float j = (-(1 + e) * velN) / (tIM + (rAN * rAN * iIA) + (rBN * rBN * iIB));
                j /= (float)contactCount;
                Vector2 impulse = j * contact.Normal;
                ApplyImpulse(a, -impulse, rA); ApplyImpulse(b, impulse, rB);
                vA = a.Velocity + new Vector2(-a.AngularVelocity * rA.Y, a.AngularVelocity * rA.X);
                vB = b.Velocity + new Vector2(-b.AngularVelocity * rB.Y, b.AngularVelocity * rB.X);
                relV = vB - vA;
                Vector2 tangent = relV - (contact.Normal * Vector2.Dot(relV, contact.Normal));
                if (tangent.LengthSquared() > 0.0001f)
                {
                    tangent = Vector2.Normalize(tangent);
                    float rAT = Cross(rA, tangent), rBT = Cross(rB, tangent);
                    float jt = (-Vector2.Dot(relV, tangent)) / (tIM + (rAT * rAT * iIA) + (rBT * rBT * iIB));
                    jt /= (float)contactCount;
                    float fA = a.Friction ?? (world.UseCustomSettings ? world.CustomFriction : settings.DefaultFriction), fB = b.Friction ?? (world.UseCustomSettings ? world.CustomFriction : settings.DefaultFriction);
                    float f = MathF.Sqrt(fA * fB);
                    jt = Math.Clamp(jt, -j * f, j * f);
                    Vector2 fImpulse = jt * tangent;
                    ApplyImpulse(a, -fImpulse, rA); ApplyImpulse(b, fImpulse, rB);
                }
            }
        }
    }

    private static void ApplyImpulse(Physical p, Vector2 impulse, Vector2 r)
    {
        if (p.IsStatic) return;
        p.Velocity += impulse / p.Mass;
        if (!p.IsRotationLocked) p.AngularVelocity += Cross(r, impulse) / p.Inertia;
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
        foreach (var pair in _previousContacts) if (!_currentContacts.ContainsKey(pair.Key)) { var contact = pair.Value[0]; InvokeTouchEnd(contact.A.Owner, contact.B.Owner); InvokeTouchEnd(contact.B.Owner, contact.A.Owner); }
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
        foreach (var script in scripts) if (isSensor) script._onDetectEndDelegate?.Invoke(otherEntity); else script._onTouchEndDelegate?.Invoke(otherEntity);
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, ulong mask = ulong.MaxValue, Entity? ignoreEntity = null)
    {
        PhysicsMath.RaycastHit closestHit = new() { IsHit = false, Distance = float.MaxValue };
        foreach (var physical in _activePhysicals.Concat(_staticShapes))
        {
            if (ignoreEntity != null && physical.Owner == ignoreEntity) continue;
            if ((physical.GroupMask & mask) == 0) continue;
            if (!_physicalShapes.TryGetValue(physical, out var shapes)) continue;
            foreach (var shape in shapes)
            {
                var hit = PhysicsMath.TestRay(origin, direction, distance, shape);
                if (hit.IsHit && hit.Distance < closestHit.Distance) closestHit = hit;
            }
        }
        return closestHit.IsHit ? closestHit : new PhysicsMath.RaycastHit { IsHit = false };
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, Entity? ignoreEntity, params string[] layerOrGroupNames)
    {
        ulong mask = 0;
        foreach (var name in layerOrGroupNames) { var filter = Verity.Input.Filter.Get(name); if (filter != null) mask |= filter.Mask; else mask |= Verity.Input.FilterRegistry.GetGroupMask(name); }
        if (layerOrGroupNames.Length == 0) mask = ulong.MaxValue;
        return Raycast(origin, direction, distance, mask, ignoreEntity);
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, params string[] layerOrGroupNames) => Raycast(origin, direction, distance, (Entity?)null, layerOrGroupNames);

    public static IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, ulong mask = ulong.MaxValue)
    {
        var result = new List<Entity>();
        var circleAABB = new AABB(center - new Vector2(radius), center + new Vector2(radius));
        foreach (var physical in _activePhysicals.Concat(_staticShapes))
        {
            if ((physical.GroupMask & mask) == 0) continue;
            if (!_physicalShapes.TryGetValue(physical, out var shapes)) continue;
            foreach (var shape in shapes) if (shape.GetAABB().Overlaps(circleAABB)) { result.Add(physical.Owner); break; }
        }
        return result;
    }

    public static IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, params string[] layerNames)
    {
        ulong mask = 0;
        foreach (var name in layerNames) mask |= Verity.Input.Filter.Get(name)?.Mask ?? 0;
        if (layerNames.Length == 0) mask = ulong.MaxValue;
        return OverlapCircle(center, radius, mask);
    }

    public static IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, ulong mask = ulong.MaxValue)
    {
        var result = new List<Entity>();
        var halfSize = size / 2.0f; var boxAABB = new AABB(center - halfSize, center + halfSize);
        foreach (var physical in _activePhysicals.Concat(_staticShapes))
        {
            if ((physical.GroupMask & mask) == 0) continue;
            if (!_physicalShapes.TryGetValue(physical, out var shapes)) continue;
            foreach (var shape in shapes) if (shape.GetAABB().Overlaps(boxAABB)) { result.Add(physical.Owner); break; }
        }
        return result;
    }

    public static IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, params string[] layerNames)
    {
        ulong mask = 0;
        foreach (var name in layerNames) mask |= Verity.Input.Filter.Get(name)?.Mask ?? 0;
        if (layerNames.Length == 0) mask = ulong.MaxValue;
        return OverlapBox(center, size, mask);
    }

    public static void DrawGizmos(World.World world)
    {
        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active) continue;
            var shape = entity.GetComponent<PhysicalShape>();
            if (shape == null || !shape.Enabled) continue;
            var physical = FindNearestPhysicalAncestor(entity);
            var color = shape.IsSensor ? Color.Blue : (physical != null && IsTouchingAnything(physical) ? Color.Red : Color.Green);
            if (shape is CircleShape circle) DrawCircleGizmo(circle, color);
            else { var vertices = shape.GetVertices(); if (vertices.Length < 2) continue; for (int i = 0; i < vertices.Length; i++) Verity.Core.Debug.DrawLine(vertices[i], vertices[(i + 1) % vertices.Length], color, 0.02f); }
        }
    }

    private static void DrawCircleGizmo(CircleShape circle, Color color)
    {
        Vector2 center = circle.GetWorldCenter(); Vector2 worldScale = circle.GetBaseScale();
        float scaledRadius = circle.Radius * Math.Max(MathF.Abs(worldScale.X), MathF.Abs(worldScale.Y));
        const int segments = 16;
        for (int i = 0; i < segments; i++) {
            float a1 = (float)i / segments * MathF.PI * 2, a2 = (float)(i + 1) / segments * MathF.PI * 2;
            Vector2 p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * scaledRadius, p2 = center + new Vector2(MathF.Cos(a2), MathF.Sin(a2)) * scaledRadius;
            Verity.Core.Debug.DrawLine(p1, p2, color, 0.02f);
        }
    }

    public static bool IsTouchingAnything(Physical p) { Guid id = p.Owner.Id; return _currentContacts.Keys.Any(k => k.Item1 == id || k.Item2 == id); }
    public static IEnumerable<Entity> GetTouchingEntities(Physical p) { Guid id = p.Owner.Id; foreach (var pair in _currentContacts) { if (pair.Key.Item1 == id) yield return pair.Value[0].B.Owner; else if (pair.Key.Item2 == id) yield return pair.Value[0].A.Owner; } }
    public static bool IsTouching(Physical p, string groupName) { var groupMask = Verity.Input.Filter.Get(groupName)?.Mask ?? Verity.Input.FilterRegistry.GetGroupMask(groupName); return GetTouchingEntities(p).Any(e => { var otherPhys = e.GetComponent<Physical>(); return otherPhys != null && (otherPhys.GroupMask & groupMask) != 0; }); }
    public static bool IsTouching(Physical p, Entity target) => GetTouchingEntities(p).Any(e => e == target);
    private static void AddSubStepContacts(List<Contact> subStepContacts, Physical a, Physical b, PhysicsMath.CollisionResult result) { foreach (var p in result.Contacts) subStepContacts.Add(new Contact { A = a, B = b, Normal = result.Normal, Depth = result.Depth, Point = p }); }

    public static bool IsGrounded(Physical p, string groupName)
    {
        var groupMask = Verity.Input.Filter.Get(groupName)?.Mask ?? Verity.Input.FilterRegistry.GetGroupMask(groupName);
        Guid myId = p.Owner.Id;
        foreach (var pair in _currentContacts)
        {
            if (pair.Key.Item1 == myId || pair.Key.Item2 == myId)
            {
                foreach (var contact in pair.Value)
                {
                    var other = (contact.A.Owner.Id == myId) ? contact.B : contact.A;
                    if ((other.GroupMask & groupMask) != 0)
                    {
                        if (contact.A.Owner.Id == myId) { if (contact.Normal.Y < -0.7f) return true; }
                        else { if (contact.Normal.Y > 0.7f) return true; }
                    }
                }
            }
        }
        return false;
    }
}
