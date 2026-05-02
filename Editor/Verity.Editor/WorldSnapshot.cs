using System.Reflection;
using Irodori.Texture;
using Verity.Core.ECS;
using Verity.Core.World;
using Verity.Core;
using Verity.Graphics;
using Verity.Core.Serialization;

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
                entity.Id,
                entity.Name,
                entity.Active,
                entity.BlueprintAssetPath,
                entity.BlueprintAssetGuid,
                entity.BlueprintSourceEntityId,
                entity.BlueprintInstanceRootId,
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

        // Store FullName to resolve it later against potentially new assembly
        return new ComponentSnapshot(type.FullName ?? type.Name, component.Enabled, data);
    }

    public void Restore(World world, Assembly? userAssembly = null)
    {
        world.ClearAllEntities();

        var created = new List<Entity>(_entities.Count);
        
        // 1. Create all entities first
        foreach (var snapshot in _entities)
        {
            var entity = world.CreateEntity(snapshot.Name);
            entity.Id = snapshot.Id;
            entity.Active = snapshot.Active;
            entity.BlueprintAssetPath = snapshot.BlueprintAssetPath;
            entity.BlueprintAssetGuid = snapshot.BlueprintAssetGuid;
            entity.BlueprintSourceEntityId = snapshot.BlueprintSourceEntityId;
            entity.BlueprintInstanceRootId = snapshot.BlueprintInstanceRootId;
            created.Add(entity);
        }

        // 2. Restore hierarchy FIRST before setting transforms
        for (int i = 0; i < _entities.Count; i++)
        {
            var parentIndex = _entities[i].ParentIndex;
            if (parentIndex >= 0)
            {
                created[i].Transform.SetParent(created[parentIndex].Transform, false);
            }
        }

        // 3. Now set local transforms (they are now correctly relative to parents)
        for (int i = 0; i < _entities.Count; i++)
        {
            var snapshot = _entities[i];
            var entity = created[i];
            entity.Transform.Position = snapshot.Position;
            entity.Transform.Rotation = snapshot.Rotation;
            entity.Transform.Scale = snapshot.Scale;
        }

        // 4. Restore components
        for (int i = 0; i < _entities.Count; i++)
        {
            var snapshot = _entities[i];
            var entity = created[i];

            foreach (var componentSnapshot in snapshot.Components)
            {
                // RESOLVE TYPE using latest assembly!
                Type? type = ResolveType(componentSnapshot.TypeName, userAssembly);
                if (type == null)
                {
                    Verity.Core.Debug.LogError($"[WorldSnapshot] Failed to resolve type '{componentSnapshot.TypeName}' during restore.");
                    continue;
                }

                var component = entity.GetComponent(type);
                if (component == null)
                {
                    if (!entity.CanAddComponent(type, out var reason))
                    {
                        Verity.Core.Debug.LogWarning($"[WorldSnapshot] Skipping component '{type.Name}' on '{entity.Name}': {reason}");
                        continue;
                    }

                    component = entity.AddComponent(type);
                }

                component.Enabled = componentSnapshot.Enabled;

                foreach (var kvp in componentSnapshot.Data)
                {
                    var field = type.GetField(kvp.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        try { field.SetValue(component, kvp.Value); } catch { }
                        continue;
                    }

                    var prop = type.GetProperty(kvp.Key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (prop != null && prop.CanWrite)
                    {
                        try { prop.SetValue(component, kvp.Value); } catch { }
                    }
                }
            }
        }

        foreach (var entity in created)
        {
            foreach (var script in entity.GetScripts())
            {
                script.HasStarted = false;

                if (script is LuaScriptComponent)
                    script.InitializeAfterDeserialization();
            }
        }
    }

    private static Type? ResolveType(string name, Assembly? userAsm)
    {
        // Reuse logic from SceneSerializer or similar
        string[] engineNamespaces = { "Verity.Core", "Verity.Graphics", "Verity.Input" };
        bool looksLikeUserScript = !engineNamespaces.Any(ns => name.StartsWith(ns));
        string shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

        if (looksLikeUserScript && userAsm != null)
        {
            var t = userAsm.GetType(name);
            if (t != null) return t;
            foreach (var type in userAsm.GetTypes())
            {
                if (type.Name == shortName || type.FullName == name) return type;
            }
        }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try {
                var t = asm.GetType(name);
                if (t != null) return t;
            } catch { }
        }

        return null;
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
        Guid Id,
        string Name,
        bool Active,
        string BlueprintAssetPath,
        string BlueprintAssetGuid,
        Guid? BlueprintSourceEntityId,
        Guid? BlueprintInstanceRootId,
        System.Numerics.Vector2 Position,
        float Rotation,
        System.Numerics.Vector2 Scale,
        int ParentIndex,
        List<ComponentSnapshot> Components);

    private sealed class ComponentSnapshot
    {
        public string TypeName { get; }
        public bool Enabled { get; }
        public Dictionary<string, object?> Data { get; }

        public ComponentSnapshot(string typeName, bool enabled, Dictionary<string, object?> data)
        {
            TypeName = typeName;
            Enabled = enabled;
            Data = data;
        }
    }
}
