using System.Numerics;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Core.Physics;

public static class PhysicsManager
{
    private const int MaxSubSteps = 4;
    private const float TargetSubStepDelta = 1.0f / 120.0f;

    private static readonly SpatialHashGrid _grid = new();
    private static readonly List<Physical> _activePhysicals = [];
    private static readonly List<Physical> _staticShapes = [];
    private static readonly Dictionary<Physical, List<PhysicalShape>> _physicalShapes = [];
    private static readonly Dictionary<Entity, Physical?> _nearestPhysicalCache = [];
    private static readonly List<Contact> _subStepContacts = [];
    private static readonly HashSet<(Guid, Guid)> _checkedPairs = [];
    private static readonly HashSet<Physical> _potentialCollisions = [];
    private static readonly Dictionary<(Guid, Guid), List<Contact>> _currentContacts = [];
    private static readonly Dictionary<(Guid, Guid), List<Contact>> _previousContacts = [];
    private static readonly Dictionary<(Guid, Guid), List<Contact>> _contactsByPair = [];
    private static readonly Stack<List<Contact>> _contactListPool = [];
    private static ulong[] _collisionMatrix = Enumerable.Repeat(ulong.MaxValue, 64).ToArray();
    private static World.World? _cachedWorld;
    private static int _cachedWorldVersion = -1;

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

    private static void EnsureQueryCache()
    {
        var world = WorldManager.ActiveWorld;
        if (world != null)
            EnsureObjectCache(world);
    }

    public static bool CanCollide(ulong maskA, ulong maskB)
    {
        for (int i = 0; i < 64; i++)
        {
            if ((maskA & (1UL << i)) != 0 && (_collisionMatrix[i] & maskB) != 0)
                return true;
        }

        return false;
    }

    public static void Step(float deltaTime, World.World world, ProjectSettings settings)
    {
        int subSteps = DetermineSubSteps(deltaTime);
        float subDeltaTime = deltaTime / subSteps;

        Gravity = world.UseCustomSettings ? world.CustomGravity : settings.DefaultGravity;
        EnsureObjectCache(world);

        SwapContactMaps();

        for (int step = 0; step < subSteps; step++)
        {
            IntegrateBodies(subDeltaTime, world, settings);
            RebuildGrid();

            FindContacts(_subStepContacts);
            ResolveContacts(_subStepContacts, world, settings);
            RecordContacts(_subStepContacts, _currentContacts);
        }

        float sleepThreshold = world.UseCustomSettings ? world.CustomPhysicsThreshold : settings.DefaultPhysicsThreshold;
        foreach (var physical in _activePhysicals)
            physical.UpdateSleepStatus(deltaTime, sleepThreshold);

        DispatchEvents();
    }

    private static int DetermineSubSteps(float deltaTime)
    {
        if (deltaTime <= 0.0f)
            return 1;

        return Math.Clamp((int)MathF.Ceiling(deltaTime / TargetSubStepDelta), 1, MaxSubSteps);
    }

    private static void EnsureObjectCache(World.World world)
    {
        if (!ReferenceEquals(_cachedWorld, world) || _cachedWorldVersion != world.StateVersion)
            RebuildObjectCache(world);
    }

    private static void RebuildObjectCache(World.World world)
    {
        _cachedWorld = world;
        _cachedWorldVersion = world.StateVersion;
        _activePhysicals.Clear();
        _staticShapes.Clear();
        _physicalShapes.Clear();
        _nearestPhysicalCache.Clear();

        foreach (var entity in world.GetAllEntities())
        {
            Physical? nearestPhysical = null;
            var physical = entity.GetComponent<Physical>();
            if (entity.Active && physical != null && physical.Enabled)
            {
                nearestPhysical = physical;
                if (physical.IsStatic)
                    _staticShapes.Add(physical);
                else
                    _activePhysicals.Add(physical);

                _physicalShapes[physical] = [];
            }
            else if (entity.Transform.Parent != null)
            {
                _nearestPhysicalCache.TryGetValue(entity.Transform.Parent.Owner, out nearestPhysical);
            }

            _nearestPhysicalCache[entity] = nearestPhysical;

            if (!entity.Active)
                continue;

            foreach (var shape in entity.GetComponents<PhysicalShape>())
            {
                if (!shape.Enabled)
                    continue;

                if (nearestPhysical != null)
                {
                    _physicalShapes[nearestPhysical].Add(shape);
                    continue;
                }

                var virtualPhysical = new Physical
                {
                    Owner = entity,
                    IsStatic = true,
                    GroupName = shape.GroupName
                };
                _staticShapes.Add(virtualPhysical);
                _physicalShapes[virtualPhysical] = [shape];
            }
        }
    }

    private static void IntegrateBodies(float subDeltaTime, World.World world, ProjectSettings settings)
    {
        float defaultLinearDamping = world.UseCustomSettings ? world.CustomLinearDamping : settings.DefaultLinearDamping;
        float defaultAngularDamping = world.UseCustomSettings ? world.CustomAngularDamping : settings.DefaultAngularDamping;

        foreach (var physical in _activePhysicals)
        {
            if (physical.IsSleeping)
                continue;

            if (physical.ForceAccumulator.LengthSquared() < 0.001f)
                physical.ForceAccumulator = Vector2.Zero;
            if (MathF.Abs(physical.TorqueAccumulator) < 0.001f)
                physical.TorqueAccumulator = 0.0f;

            float mass = MathF.Max(0.0001f, physical.Mass);
            Vector2 acceleration = (physical.ForceAccumulator / mass) + (Gravity * physical.GravityScale);
            physical.Velocity += acceleration * subDeltaTime;

            float linearDamping = physical.LinearDamping ?? defaultLinearDamping;
            physical.Velocity *= MathF.Exp(-linearDamping * subDeltaTime);

            var transform = physical.Owner.Transform;
            transform.WorldPosition += physical.Velocity * subDeltaTime;

            if (!physical.IsRotationLocked)
            {
                float inertia = MathF.Max(0.0001f, physical.Inertia);
                float angularAcceleration = physical.TorqueAccumulator / inertia;
                physical.AngularVelocity += angularAcceleration * subDeltaTime;
                float angularDamping = physical.AngularDamping ?? defaultAngularDamping;
                physical.AngularVelocity *= MathF.Exp(-angularDamping * subDeltaTime);
                transform.WorldRotation += physical.AngularVelocity * (180.0f / MathF.PI) * subDeltaTime;
            }

            physical.ForceAccumulator = Vector2.Zero;
            physical.TorqueAccumulator = 0.0f;
        }
    }

    private static void RebuildGrid()
    {
        _grid.Clear();

        foreach (var physical in _activePhysicals)
        {
            if (_physicalShapes.TryGetValue(physical, out var shapes) && shapes.Count > 0)
                _grid.Add(physical, shapes);
        }

        foreach (var physical in _staticShapes)
        {
            if (_physicalShapes.TryGetValue(physical, out var shapes) && shapes.Count > 0)
                _grid.Add(physical, shapes);
        }
    }

    private static void FindContacts(List<Contact> contacts)
    {
        contacts.Clear();
        _checkedPairs.Clear();

        foreach (var bodyA in _activePhysicals)
        {
            if (!_physicalShapes.TryGetValue(bodyA, out var shapesA) || shapesA.Count == 0)
                continue;

            _grid.GetPotentialCollisions(bodyA, shapesA, _potentialCollisions);
            foreach (var bodyB in _potentialCollisions)
            {
                var pair = GetOrderedPair(bodyA.Owner.Id, bodyB.Owner.Id);
                if (!_checkedPairs.Add(pair))
                    continue;

                if (!CanCollide(bodyA.GroupMask, bodyB.GroupMask))
                    continue;

                if (!_physicalShapes.TryGetValue(bodyB, out var shapesB) || shapesB.Count == 0)
                    continue;

                foreach (var shapeA in shapesA)
                {
                    foreach (var shapeB in shapesB)
                        TestShapePair(shapeA, shapeB, bodyA, bodyB, contacts);
                }
            }
        }
    }

    private static void TestShapePair(PhysicalShape shapeA, PhysicalShape shapeB, Physical bodyA, Physical bodyB, List<Contact> contacts)
    {
        if (shapeA is TilemapShape tilemapA)
        {
            foreach (var polygon in tilemapA.GetWorldPolygons())
            {
                if (shapeB is CircleShape circleB)
                {
                    var result = PhysicsMath.TestSAT(circleB, polygon);
                    if (result.IsColliding)
                    {
                        result.Normal = -result.Normal;
                        AddSubStepContacts(contacts, bodyA, bodyB, result);
                    }
                }
                else
                {
                    var result = PhysicsMath.TestSAT(polygon, shapeB.GetVertices());
                    if (result.IsColliding)
                        AddSubStepContacts(contacts, bodyA, bodyB, result);
                }
            }

            return;
        }

        if (shapeB is TilemapShape tilemapB)
        {
            foreach (var polygon in tilemapB.GetWorldPolygons())
            {
                if (shapeA is CircleShape circleA)
                {
                    var result = PhysicsMath.TestSAT(circleA, polygon);
                    if (result.IsColliding)
                        AddSubStepContacts(contacts, bodyA, bodyB, result);
                }
                else
                {
                    var result = PhysicsMath.TestSAT(shapeA.GetVertices(), polygon);
                    if (result.IsColliding)
                        AddSubStepContacts(contacts, bodyA, bodyB, result);
                }
            }

            return;
        }

        if (shapeA is CircleShape && shapeB is CircleShape)
        {
            var result = PhysicsMath.TestSAT(shapeA, shapeB);
            if (result.IsColliding)
                AddSubStepContacts(contacts, bodyA, bodyB, result);
            return;
        }

        var subShapesA = shapeA is PolygonShape polygonA ? polygonA.GetConvexSubShapes() : [shapeA.GetVertices()];
        var subShapesB = shapeB is PolygonShape polygonB ? polygonB.GetConvexSubShapes() : [shapeB.GetVertices()];

        if (shapeA is CircleShape shapeCircleA)
        {
            foreach (var verticesB in subShapesB)
            {
                if (verticesB.Length == 0)
                    continue;

                var result = PhysicsMath.TestSAT(shapeCircleA, verticesB);
                if (result.IsColliding)
                    AddSubStepContacts(contacts, bodyA, bodyB, result);
            }

            return;
        }

        if (shapeB is CircleShape shapeCircleB)
        {
            foreach (var verticesA in subShapesA)
            {
                if (verticesA.Length == 0)
                    continue;

                var result = PhysicsMath.TestSAT(shapeCircleB, verticesA);
                if (result.IsColliding)
                {
                    result.Normal = -result.Normal;
                    AddSubStepContacts(contacts, bodyA, bodyB, result);
                }
            }

            return;
        }

        foreach (var verticesA in subShapesA)
        {
            if (verticesA.Length == 0)
                continue;

            foreach (var verticesB in subShapesB)
            {
                if (verticesB.Length == 0)
                    continue;

                var result = PhysicsMath.TestSAT(verticesA, verticesB);
                if (result.IsColliding)
                    AddSubStepContacts(contacts, bodyA, bodyB, result);
            }
        }
    }

    private static void ResolveContacts(List<Contact> contacts, World.World world, ProjectSettings settings)
    {
        if (contacts.Count == 0)
            return;

        foreach (var contact in contacts)
        {
            var pair = GetOrderedPair(contact.A.Owner.Id, contact.B.Owner.Id);
            if (!_contactsByPair.TryGetValue(pair, out var contactList))
            {
                contactList = RentContactList();
                _contactsByPair[pair] = contactList;
            }

            contactList.Add(contact);
        }

        float defaultBounciness = world.UseCustomSettings ? world.CustomBounciness : settings.DefaultBounciness;
        float defaultFriction = world.UseCustomSettings ? world.CustomFriction : settings.DefaultFriction;

        foreach (var pairContacts in _contactsByPair.Values)
        {
            ResolveContactGroup(pairContacts, defaultBounciness, defaultFriction);
            ReturnContactList(pairContacts);
        }

        _contactsByPair.Clear();
    }

    private static void ResolveContactGroup(List<Contact> contacts, float defaultBounciness, float defaultFriction)
    {
        int contactCount = contacts.Count;
        if (contactCount == 0)
            return;

        var maxDepthContact = contacts[0];
        for (int i = 1; i < contactCount; i++)
        {
            if (contacts[i].Depth > maxDepthContact.Depth)
                maxDepthContact = contacts[i];
        }

        float inverseMassA = maxDepthContact.A.IsStatic ? 0.0f : 1.0f / maxDepthContact.A.Mass;
        float inverseMassB = maxDepthContact.B.IsStatic ? 0.0f : 1.0f / maxDepthContact.B.Mass;
        float totalInverseMass = inverseMassA + inverseMassB;
        if (totalInverseMass > 0.0f)
        {
            const float percent = 0.4f;
            const float slop = 0.01f;
            Vector2 correction = maxDepthContact.Normal * (MathF.Max(maxDepthContact.Depth - slop, 0.0f) / totalInverseMass * percent);
            if (!maxDepthContact.A.IsStatic)
                maxDepthContact.A.Owner.Transform.WorldPosition -= correction * inverseMassA;
            if (!maxDepthContact.B.IsStatic)
                maxDepthContact.B.Owner.Transform.WorldPosition += correction * inverseMassB;
        }

        foreach (var contact in contacts)
        {
            var bodyA = contact.A;
            var bodyB = contact.B;

            float inverseMassBodyA = bodyA.IsStatic ? 0.0f : 1.0f / bodyA.Mass;
            float inverseMassBodyB = bodyB.IsStatic ? 0.0f : 1.0f / bodyB.Mass;
            float inverseInertiaA = (bodyA.IsStatic || bodyA.IsRotationLocked) ? 0.0f : 1.0f / bodyA.Inertia;
            float inverseInertiaB = (bodyB.IsStatic || bodyB.IsRotationLocked) ? 0.0f : 1.0f / bodyB.Inertia;
            float totalMass = inverseMassBodyA + inverseMassBodyB;

            Vector2 radiusA = contact.Point - bodyA.Owner.Transform.WorldPosition;
            Vector2 radiusB = contact.Point - bodyB.Owner.Transform.WorldPosition;
            Vector2 velocityA = bodyA.Velocity + new Vector2(-bodyA.AngularVelocity * radiusA.Y, bodyA.AngularVelocity * radiusA.X);
            Vector2 velocityB = bodyB.Velocity + new Vector2(-bodyB.AngularVelocity * radiusB.Y, bodyB.AngularVelocity * radiusB.X);
            Vector2 relativeVelocity = velocityB - velocityA;
            float velocityAlongNormal = Vector2.Dot(relativeVelocity, contact.Normal);
            if (velocityAlongNormal > 0.0f)
                continue;

            float crossA = Cross(radiusA, contact.Normal);
            float crossB = Cross(radiusB, contact.Normal);
            float restitution = Math.Max(bodyA.Bounciness ?? defaultBounciness, bodyB.Bounciness ?? defaultBounciness);
            if (MathF.Abs(velocityAlongNormal) < 0.1f)
                restitution = 0.0f;

            float impulseMagnitude = (-(1.0f + restitution) * velocityAlongNormal) /
                                     (totalMass + (crossA * crossA * inverseInertiaA) + (crossB * crossB * inverseInertiaB));
            impulseMagnitude /= contactCount;

            Vector2 impulse = impulseMagnitude * contact.Normal;
            ApplyImpulse(bodyA, -impulse, radiusA);
            ApplyImpulse(bodyB, impulse, radiusB);

            velocityA = bodyA.Velocity + new Vector2(-bodyA.AngularVelocity * radiusA.Y, bodyA.AngularVelocity * radiusA.X);
            velocityB = bodyB.Velocity + new Vector2(-bodyB.AngularVelocity * radiusB.Y, bodyB.AngularVelocity * radiusB.X);
            relativeVelocity = velocityB - velocityA;

            Vector2 tangent = relativeVelocity - (contact.Normal * Vector2.Dot(relativeVelocity, contact.Normal));
            if (tangent.LengthSquared() <= 0.0001f)
                continue;

            tangent = Vector2.Normalize(tangent);
            float tangentCrossA = Cross(radiusA, tangent);
            float tangentCrossB = Cross(radiusB, tangent);
            float frictionImpulseMagnitude = (-Vector2.Dot(relativeVelocity, tangent)) /
                                             (totalMass + (tangentCrossA * tangentCrossA * inverseInertiaA) + (tangentCrossB * tangentCrossB * inverseInertiaB));
            frictionImpulseMagnitude /= contactCount;

            float frictionA = bodyA.Friction ?? defaultFriction;
            float frictionB = bodyB.Friction ?? defaultFriction;
            float friction = MathF.Sqrt(frictionA * frictionB);
            frictionImpulseMagnitude = Math.Clamp(frictionImpulseMagnitude, -impulseMagnitude * friction, impulseMagnitude * friction);

            Vector2 frictionImpulse = frictionImpulseMagnitude * tangent;
            ApplyImpulse(bodyA, -frictionImpulse, radiusA);
            ApplyImpulse(bodyB, frictionImpulse, radiusB);
        }
    }

    private static void RecordContacts(List<Contact> contacts, Dictionary<(Guid, Guid), List<Contact>> target)
    {
        foreach (var contact in contacts)
        {
            var pair = GetOrderedPair(contact.A.Owner.Id, contact.B.Owner.Id);
            if (!target.TryGetValue(pair, out var contactList))
            {
                contactList = RentContactList();
                target[pair] = contactList;
            }

            contactList.Add(contact);
        }
    }

    private static void SwapContactMaps()
    {
        ClearContactMap(_previousContacts);
        foreach (var pair in _currentContacts)
            _previousContacts[pair.Key] = pair.Value;

        _currentContacts.Clear();
    }

    private static void ClearContactMap(Dictionary<(Guid, Guid), List<Contact>> map)
    {
        foreach (var contacts in map.Values)
            ReturnContactList(contacts);

        map.Clear();
    }

    private static List<Contact> RentContactList()
        => _contactListPool.Count > 0 ? _contactListPool.Pop() : new List<Contact>(4);

    private static void ReturnContactList(List<Contact> contacts)
    {
        contacts.Clear();
        _contactListPool.Push(contacts);
    }

    private static void ApplyImpulse(Physical physical, Vector2 impulse, Vector2 offset)
    {
        if (physical.IsStatic)
            return;

        physical.Velocity += impulse / physical.Mass;
        if (!physical.IsRotationLocked)
            physical.AngularVelocity += Cross(offset, impulse) / physical.Inertia;
        physical.WakeUp();
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    private static (Guid, Guid) GetOrderedPair(Guid a, Guid b)
        => a.CompareTo(b) < 0 ? (a, b) : (b, a);

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
            if (_currentContacts.ContainsKey(pair.Key))
                continue;

            var contact = pair.Value[0];
            InvokeTouchEnd(contact.A.Owner, contact.B.Owner);
            InvokeTouchEnd(contact.B.Owner, contact.A.Owner);
        }
    }

    private static void InvokePhysicsEvent(Entity entity, Entity otherEntity, Physical otherPhysical, bool isFirst)
    {
        var shape = GetFirstEnabledShape(entity);
        bool isSensor = shape?.IsSensor ?? false;
        foreach (var script in entity.GetComponents<Script>())
        {
            if (!script.Enabled)
                continue;

            if (isSensor)
            {
                if (isFirst)
                    script._onDetectedDelegate?.Invoke(otherEntity);
                else
                    script._onDetectingDelegate?.Invoke(otherEntity);
            }
            else
            {
                if (isFirst)
                    script._onTouchedDelegate?.Invoke(otherPhysical);
                else
                    script._onTouchingDelegate?.Invoke(otherPhysical);
            }
        }
    }

    private static void InvokeTouchEnd(Entity entity, Entity otherEntity)
    {
        var shape = GetFirstEnabledShape(entity);
        bool isSensor = shape?.IsSensor ?? false;
        foreach (var script in entity.GetComponents<Script>())
        {
            if (!script.Enabled)
                continue;

            if (isSensor)
                script._onDetectEndDelegate?.Invoke(otherEntity);
            else
                script._onTouchEndDelegate?.Invoke(otherEntity);
        }
    }

    private static PhysicalShape? GetFirstEnabledShape(Entity entity)
    {
        foreach (var shape in entity.GetComponents<PhysicalShape>())
        {
            if (shape.Enabled)
                return shape;
        }

        return null;
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, ulong mask = ulong.MaxValue, Entity? ignoreEntity = null)
    {
        EnsureQueryCache();

        PhysicsMath.RaycastHit closestHit = new() { IsHit = false, Distance = float.MaxValue };
        FindClosestRaycastHit(_activePhysicals, origin, direction, distance, mask, ignoreEntity, ref closestHit);
        FindClosestRaycastHit(_staticShapes, origin, direction, distance, mask, ignoreEntity, ref closestHit);
        return closestHit.IsHit ? closestHit : new PhysicsMath.RaycastHit { IsHit = false };
    }

    private static void FindClosestRaycastHit(List<Physical> physicals, Vector2 origin, Vector2 direction, float distance, ulong mask, Entity? ignoreEntity, ref PhysicsMath.RaycastHit closestHit)
    {
        foreach (var physical in physicals)
        {
            if (ignoreEntity != null && physical.Owner == ignoreEntity)
                continue;
            if ((physical.GroupMask & mask) == 0 || !_physicalShapes.TryGetValue(physical, out var shapes))
                continue;

            foreach (var shape in shapes)
            {
                var hit = PhysicsMath.TestRay(origin, direction, distance, shape);
                if (hit.IsHit && hit.Distance < closestHit.Distance)
                    closestHit = hit;
            }
        }
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, Entity? ignoreEntity, params string[] layerOrGroupNames)
    {
        ulong mask = 0;
        foreach (var name in layerOrGroupNames)
        {
            var filter = Verity.Input.Filter.Get(name);
            mask |= filter != null ? filter.Mask : Verity.Input.FilterRegistry.GetGroupMask(name);
        }

        if (layerOrGroupNames.Length == 0)
            mask = ulong.MaxValue;

        return Raycast(origin, direction, distance, mask, ignoreEntity);
    }

    public static PhysicsMath.RaycastHit Raycast(Vector2 origin, Vector2 direction, float distance, params string[] layerOrGroupNames)
        => Raycast(origin, direction, distance, (Entity?)null, layerOrGroupNames);

    public static IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, ulong mask = ulong.MaxValue)
    {
        EnsureQueryCache();
        var result = new List<Entity>();
        var circleAabb = new AABB(center - new Vector2(radius), center + new Vector2(radius));
        CollectOverlaps(_activePhysicals, circleAabb, mask, result);
        CollectOverlaps(_staticShapes, circleAabb, mask, result);
        return result;
    }

    public static IEnumerable<Entity> OverlapCircle(Vector2 center, float radius, params string[] layerNames)
    {
        ulong mask = 0;
        foreach (var name in layerNames)
            mask |= Verity.Input.Filter.Get(name)?.Mask ?? 0;

        if (layerNames.Length == 0)
            mask = ulong.MaxValue;

        return OverlapCircle(center, radius, mask);
    }

    public static IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, ulong mask = ulong.MaxValue)
    {
        EnsureQueryCache();
        var result = new List<Entity>();
        Vector2 halfSize = size / 2.0f;
        var boxAabb = new AABB(center - halfSize, center + halfSize);
        CollectOverlaps(_activePhysicals, boxAabb, mask, result);
        CollectOverlaps(_staticShapes, boxAabb, mask, result);
        return result;
    }

    public static IEnumerable<Entity> OverlapBox(Vector2 center, Vector2 size, params string[] layerNames)
    {
        ulong mask = 0;
        foreach (var name in layerNames)
            mask |= Verity.Input.Filter.Get(name)?.Mask ?? 0;

        if (layerNames.Length == 0)
            mask = ulong.MaxValue;

        return OverlapBox(center, size, mask);
    }

    private static void CollectOverlaps(List<Physical> physicals, AABB queryAabb, ulong mask, List<Entity> result)
    {
        foreach (var physical in physicals)
        {
            if ((physical.GroupMask & mask) == 0 || !_physicalShapes.TryGetValue(physical, out var shapes))
                continue;

            foreach (var shape in shapes)
            {
                if (!shape.GetAABB().Overlaps(queryAabb))
                    continue;

                result.Add(physical.Owner);
                break;
            }
        }
    }

    public static void DrawGizmos(World.World world)
    {
        EnsureObjectCache(world);

        foreach (var entity in world.GetAllEntities())
        {
            if (!entity.Active)
                continue;

            foreach (var shape in entity.GetComponents<PhysicalShape>())
            {
                if (!shape.Enabled)
                    continue;

                var physical = FindNearestPhysicalAncestor(entity);
                var color = shape.IsSensor ? Color.Blue : (physical != null && IsTouchingAnything(physical) ? Color.Red : Color.Green);
                if (shape is CircleShape circleShape)
                    DrawCircleGizmo(circleShape, color);
                else if (shape is TilemapShape tilemapShape)
                    tilemapShape.DrawGizmos(color);
                else
                {
                    var vertices = shape.GetVertices();
                    if (vertices.Length < 2)
                        continue;

                    for (int i = 0; i < vertices.Length; i++)
                        Verity.Core.Debug.DrawLine(vertices[i], vertices[(i + 1) % vertices.Length], color, 0.02f);
                }
            }
        }
    }

    private static Physical? FindNearestPhysicalAncestor(Entity entity)
    {
        if (_nearestPhysicalCache.TryGetValue(entity, out var cached))
            return cached;

        var current = entity.Transform.Parent?.Owner;
        while (current != null)
        {
            var physical = current.GetComponent<Physical>();
            if (physical != null && physical.Enabled)
                return physical;

            current = current.Transform.Parent?.Owner;
        }

        return null;
    }

    private static void DrawCircleGizmo(CircleShape circle, Color color)
    {
        Vector2 center = circle.GetWorldCenter();
        Vector2 worldScale = circle.GetBaseScale();
        float scaledRadius = circle.Radius * Math.Max(MathF.Abs(worldScale.X), MathF.Abs(worldScale.Y));
        const int segments = 16;
        for (int i = 0; i < segments; i++)
        {
            float angleA = (float)i / segments * MathF.PI * 2.0f;
            float angleB = (float)(i + 1) / segments * MathF.PI * 2.0f;
            Vector2 pointA = center + new Vector2(MathF.Cos(angleA), MathF.Sin(angleA)) * scaledRadius;
            Vector2 pointB = center + new Vector2(MathF.Cos(angleB), MathF.Sin(angleB)) * scaledRadius;
            Verity.Core.Debug.DrawLine(pointA, pointB, color, 0.02f);
        }
    }

    public static bool IsTouchingAnything(Physical physical)
    {
        Guid id = physical.Owner.Id;
        foreach (var pair in _currentContacts.Keys)
        {
            if (pair.Item1 == id || pair.Item2 == id)
                return true;
        }

        return false;
    }

    public static IEnumerable<Entity> GetTouchingEntities(Physical physical)
    {
        Guid id = physical.Owner.Id;
        foreach (var pair in _currentContacts)
        {
            if (pair.Key.Item1 == id)
                yield return pair.Value[0].B.Owner;
            else if (pair.Key.Item2 == id)
                yield return pair.Value[0].A.Owner;
        }
    }

    public static bool IsTouching(Physical physical, string groupName)
    {
        ulong groupMask = Verity.Input.Filter.Get(groupName)?.Mask ?? Verity.Input.FilterRegistry.GetGroupMask(groupName);
        foreach (var entity in GetTouchingEntities(physical))
        {
            var otherPhysical = entity.GetComponent<Physical>();
            if (otherPhysical != null && (otherPhysical.GroupMask & groupMask) != 0)
                return true;
        }

        return false;
    }

    public static bool IsTouching(Physical physical, Entity target)
    {
        foreach (var entity in GetTouchingEntities(physical))
        {
            if (entity == target)
                return true;
        }

        return false;
    }

    public static bool IsTouchingDirection(Physical physical, Vector2 direction, string? groupName = null)
        => GetTouchingEntitiesDirection(physical, direction, groupName).Any();

    public static bool IsTouchingLocalDirection(Physical physical, Vector2 localDirection, string? groupName = null)
        => GetTouchingEntitiesLocalDirection(physical, localDirection, groupName).Any();

    public static int GetTouchingCountDirection(Physical physical, Vector2 direction, string? groupName = null)
        => GetTouchingEntitiesDirection(physical, direction, groupName).Count();

    public static int GetTouchingCountLocalDirection(Physical physical, Vector2 localDirection, string? groupName = null)
        => GetTouchingEntitiesLocalDirection(physical, localDirection, groupName).Count();

    public static IEnumerable<Entity> GetTouchingEntitiesDirection(Physical physical, Vector2 direction, string? groupName = null)
    {
        if (direction == Vector2.Zero)
            yield break;

        Vector2 cardinalDirection = SnapToCardinal(direction);
        Guid id = physical.Owner.Id;
        ulong groupMask = groupName != null
            ? (Verity.Input.Filter.Get(groupName)?.Mask ?? Verity.Input.FilterRegistry.GetGroupMask(groupName))
            : ulong.MaxValue;

        foreach (var pair in _currentContacts)
        {
            if (pair.Key.Item1 != id && pair.Key.Item2 != id)
                continue;

            bool isA = pair.Key.Item1 == id;
            var other = isA ? pair.Value[0].B : pair.Value[0].A;
            if (groupName != null && (other.GroupMask & groupMask) == 0)
                continue;

            foreach (var contact in pair.Value)
            {
                Vector2 normal = isA ? contact.Normal : -contact.Normal;
                if (Vector2.Dot(normal, cardinalDirection) > 0.7f)
                {
                    yield return other.Owner;
                    break;
                }
            }
        }
    }

    public static IEnumerable<Entity> GetTouchingEntitiesLocalDirection(Physical physical, Vector2 localDirection, string? groupName = null)
    {
        if (localDirection == Vector2.Zero)
            yield break;

        Vector2 worldDirection = localDirection;
        var transform = physical.Owner.GetComponent<Transform>();
        if (transform != null)
        {
            float radians = transform.WorldRotation * MathF.PI / 180.0f;
            worldDirection = RotateVector(localDirection, radians);
        }

        Guid id = physical.Owner.Id;
        ulong groupMask = groupName != null
            ? (Verity.Input.Filter.Get(groupName)?.Mask ?? Verity.Input.FilterRegistry.GetGroupMask(groupName))
            : ulong.MaxValue;
        Vector2 normalizedDirection = Vector2.Normalize(worldDirection);

        foreach (var pair in _currentContacts)
        {
            if (pair.Key.Item1 != id && pair.Key.Item2 != id)
                continue;

            bool isA = pair.Key.Item1 == id;
            var other = isA ? pair.Value[0].B : pair.Value[0].A;
            if (groupName != null && (other.GroupMask & groupMask) == 0)
                continue;

            foreach (var contact in pair.Value)
            {
                Vector2 normal = isA ? contact.Normal : -contact.Normal;
                if (Vector2.Dot(normal, normalizedDirection) > 0.7f)
                {
                    yield return other.Owner;
                    break;
                }
            }
        }
    }

    private static Vector2 RotateVector(Vector2 vector, float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Vector2(vector.X * cos - vector.Y * sin, vector.X * sin + vector.Y * cos);
    }

    private static Vector2 SnapToCardinal(Vector2 direction)
    {
        if (direction == Vector2.Zero)
            return Vector2.Zero;

        return MathF.Abs(direction.X) > MathF.Abs(direction.Y)
            ? new Vector2(MathF.Sign(direction.X), 0.0f)
            : new Vector2(0.0f, MathF.Sign(direction.Y));
    }

    private static void AddSubStepContacts(List<Contact> contacts, Physical bodyA, Physical bodyB, PhysicsMath.CollisionResult result)
    {
        foreach (var point in result.Contacts)
        {
            contacts.Add(new Contact
            {
                A = bodyA,
                B = bodyB,
                Normal = result.Normal,
                Depth = result.Depth,
                Point = point
            });
        }
    }

    public static bool IsGrounded(Physical physical, string groupName)
    {
        ulong groupMask = Verity.Input.Filter.Get(groupName)?.Mask ?? Verity.Input.FilterRegistry.GetGroupMask(groupName);
        Guid id = physical.Owner.Id;

        foreach (var pair in _currentContacts)
        {
            if (pair.Key.Item1 != id && pair.Key.Item2 != id)
                continue;

            foreach (var contact in pair.Value)
            {
                var other = contact.A.Owner.Id == id ? contact.B : contact.A;
                if ((other.GroupMask & groupMask) == 0)
                    continue;

                if (contact.A.Owner.Id == id)
                {
                    if (contact.Normal.Y < -0.7f)
                        return true;
                }
                else if (contact.Normal.Y > 0.7f)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
