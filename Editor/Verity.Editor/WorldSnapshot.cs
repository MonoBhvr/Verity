using System.Reflection;
using Irodori.Texture;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core;
using Verity.Graphics;

namespace Verity.Editor;

internal sealed class WorldSnapshot
{
    private readonly List<EntitySnapshot> _entities;

    private WorldSnapshot(List<EntitySnapshot> entities)
    {
        _entities = entities;
    }

    public static WorldSnapshot Capture(World world)
    {
        var ordered = BuildOrderedEntityList(world);
        var indexByObject = new Dictionary<Entity, int>(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
            indexByObject[ordered[i]] = i;

        var snapshots = new List<EntitySnapshot>(ordered.Count);
        foreach (var entity in ordered)
        {
            int parentIndex = -1;
            var parent = entity.Transform.Parent?.Owner;
            if (parent != null && indexByObject.TryGetValue(parent, out var found))
                parentIndex = found;

            var componentSnapshots = new List<ComponentSnapshot>();
            foreach (var component in entity.GetAllComponents())
            {
                if (component is Transform)
                    continue;

                componentSnapshots.Add(CaptureComponent(component));
            }

            snapshots.Add(new EntitySnapshot(
                entity.Name,
                entity.Active,
                entity.Transform.Position,
                entity.Transform.Rotation,
                entity.Transform.Scale,
                parentIndex,
                componentSnapshots));
        }

        return new WorldSnapshot(snapshots);
    }

    private static ComponentSnapshot CaptureComponent(Component component)
    {
        var type = component.GetType();
        var data = new Dictionary<string, object?>();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.GetCustomAttribute<SerializeFieldAttribute>() != null || field.IsPublic)
            {
                if (field.GetCustomAttribute<HideInInspectorAttribute>() == null)
                {
                    data[field.Name] = field.GetValue(component);
                }
            }
        }

        var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        foreach (var prop in props)
        {
            if (prop.DeclaringType == typeof(Component)) continue;
            if ((prop.GetCustomAttribute<SerializeFieldAttribute>() != null || (prop.GetGetMethod()?.IsPublic ?? false)) && prop.CanRead && prop.CanWrite)
            {
                if (prop.GetCustomAttribute<HideInInspectorAttribute>() == null)
                {
                    data[prop.Name] = prop.GetValue(component);
                }
            }
        }

        return new ComponentSnapshot(type, component.Enabled, data);
    }

    public void Restore(World world)
    {
        world.ClearAllEntities();

        var created = new List<Entity>(_entities.Count);
        foreach (var snapshot in _entities)
        {
            var entity = world.CreateEntity(snapshot.Name);
            entity.Active = snapshot.Active;
            entity.Transform.Position = snapshot.Position;
            entity.Transform.Rotation = snapshot.Rotation;
            entity.Transform.Scale = snapshot.Scale;

            foreach (var componentSnapshot in snapshot.Components)
            {
                var component = AddComponentByType(entity, componentSnapshot.Type);
                component.Enabled = componentSnapshot.Enabled;

                foreach (var kvp in componentSnapshot.Data)
                {
                    var field = componentSnapshot.Type.GetField(kvp.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(component, kvp.Value);
                        continue;
                    }

                    var prop = componentSnapshot.Type.GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        prop.SetValue(component, kvp.Value);
                    }
                }
            }

            created.Add(entity);
        }

        for (int i = 0; i < _entities.Count; i++)
        {
            var parentIndex = _entities[i].ParentIndex;
            created[i].Transform.Parent = parentIndex >= 0 ? created[parentIndex].Transform : null;
        }

        foreach (var entity in created)
        {
            foreach (var script in entity.GetScripts())
                script.HasStarted = false;
        }
    }

    private static List<Entity> BuildOrderedEntityList(World world)
    {
        var ordered = new List<Entity>(world.RootEntities.Count);
        var visited = new HashSet<Entity>();

        foreach (var entity in world.RootEntities)
            Traverse(entity, ordered, visited);

        return ordered;
    }

    private static void Traverse(Entity entity, List<Entity> ordered, HashSet<Entity> visited)
    {
        if (!visited.Add(entity))
            return;

        ordered.Add(entity);
        foreach (var child in entity.Transform.Children)
            Traverse(child.Owner, ordered, visited);
    }

    private sealed record EntitySnapshot(
        string Name,
        bool Active,
        System.Numerics.Vector2 Position,
        float Rotation,
        System.Numerics.Vector2 Scale,
        int ParentIndex,
        List<ComponentSnapshot> Components);

    private sealed class ComponentSnapshot
    {
        public Type Type { get; }
        public bool Enabled { get; }
        public Dictionary<string, object?> Data { get; }

        public ComponentSnapshot(Type type, bool enabled, Dictionary<string, object?> data)
        {
            Type = type;
            Enabled = enabled;
            Data = data;
        }
    }

    private static Component AddComponentByType(Entity entity, Type componentType)
    {
        return entity.AddComponent(componentType);
    }
}
