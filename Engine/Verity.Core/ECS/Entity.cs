namespace Verity.Core.ECS;

public class Entity
{
    public Guid Id { get; internal set; } = Guid.NewGuid();
    public string Name { get; set; }
    public string Tag { get; set; } = "Untagged";
    public bool Active { get; set; } = true;
    public Transform Transform { get; }

    [HideInInspector]
    public string BlueprintAssetPath { get; set; } = string.Empty;

    [HideInInspector]
    public string BlueprintAssetGuid { get; set; } = string.Empty;

    [HideInInspector]
    public Guid? BlueprintSourceEntityId { get; set; }

    [HideInInspector]
    public Guid? BlueprintInstanceRootId { get; set; }

    internal Verity.Core.World.World? World { get; set; }

    private readonly List<Component> _components = [];

    public Entity(string name)
    {
        Name = name;
        Transform = new Transform { Owner = this };
        _components.Add(Transform);
    }

    [HideInInspector]
    public bool IsBlueprintInstance => BlueprintSourceEntityId.HasValue && !string.IsNullOrWhiteSpace(BlueprintAssetPath);

    [HideInInspector]
    public bool IsBlueprintInstanceRoot => IsBlueprintInstance && BlueprintInstanceRootId == Id;

    #region Find Methods
    public static Entity? Find(string name)
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) return null;
        return world.GetAllEntities().FirstOrDefault(e => e.Name == name);
    }

    public static Entity? FindWithTag(string tag)
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) return null;
        return world.GetAllEntities().FirstOrDefault(e => e.Tag == tag);
    }

    public static Entity[] FindEntitiesWithTag(string tag)
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) return Array.Empty<Entity>();
        return world.GetAllEntities().Where(e => e.Tag == tag).ToArray();
    }

    public static T? FindObjectOfType<T>(bool includeInactive = false) where T : class
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) return null;
        foreach (var entity in world.GetAllEntities())
        {
            if (!includeInactive && !entity.Active) continue;
            var comp = entity.GetComponent<T>();
            if (!includeInactive && comp is Component component && !component.Enabled) continue;
            if (comp != null) return comp;
        }
        return null;
    }

    public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : class
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) return Array.Empty<T>();
        var results = new List<T>();
        foreach (var entity in world.GetAllEntities())
        {
            if (!includeInactive && !entity.Active) continue;
            foreach (var component in entity.GetComponents<T>())
            {
                if (!includeInactive && component is Component typedComponent && !typedComponent.Enabled) continue;
                results.Add(component);
            }
        }
        return results.ToArray();
    }

    public static void Destroy(Entity entity)
    {
        entity.World?.DestroyEntity(entity);
    }

    public static void Destroy(Component component)
    {
        component.Owner.RemoveComponent(component);
    }

    public static Entity Instantiate(string name = "New Entity")
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) throw new InvalidOperationException("No active world to instantiate entity.");
        return world.CreateEntity(name);
    }

    public static Entity? Instantiate(Entity original)
    {
        var world = Verity.Core.World.WorldManager.ActiveWorld;
        if (world == null) return null;
        
        string json = Verity.Core.Serialization.SceneSerializer.SerializeEntity(original);
        var clone = Verity.Core.Serialization.SceneSerializer.DeserializeEntity(world, json);
        if (clone != null)
        {
            clone.Name = original.Name + " (Clone)";
        }
        return clone;
    }

    public static T? Instantiate<T>(T original) where T : Component
    {
        var cloneEntity = Instantiate(original.Owner);
        return cloneEntity?.GetComponent<T>();
    }
    #endregion

    public T AddComponent<T>() where T : Component, new()
    {
        if (typeof(T) == typeof(Transform))
            throw new InvalidOperationException("Cannot add a second Transform component.");

        // Prevent duplicate components (except for specific cases if ever needed, but standard is one per entity)
        var existing = GetComponent<T>();
        if (existing != null) return (T)(object)existing;

        if (!CanAddComponent(typeof(T), out var reason))
            throw new InvalidOperationException(reason);

        var component = new T { Owner = this };
        _components.Add(component);

        CheckRequiredComponents(typeof(T));

        // [SYNC] PolygonShape가 추가될 때 PolygonRenderer가 이미 있으면 동기화
        if (component is Verity.Core.Physics.PolygonShape poly)
        {
            poly.SyncWithRenderer();
        }

        return component;
    }

    public Component AddComponent(Type componentType)
    {
        if (componentType == typeof(Transform))
            throw new InvalidOperationException("Cannot add a second Transform component.");

        if (!typeof(Component).IsAssignableFrom(componentType))
            throw new ArgumentException($"Type {componentType.Name} is not a Component.");

        var existing = GetComponent(componentType);
        if (existing != null) return existing;

        if (!CanAddComponent(componentType, out var reason))
            throw new InvalidOperationException(reason);

        var component = (Component)Activator.CreateInstance(componentType)!;
        component.Owner = this;
        _components.Add(component);

        CheckRequiredComponents(componentType);

        // [SYNC] PolygonShape가 추가될 때 PolygonRenderer가 이미 있으면 동기화
        if (component is Verity.Core.Physics.PolygonShape poly)
        {
            poly.SyncWithRenderer();
        }

        return component;
    }

    public bool CanAddComponent(Type componentType, out string? reason)
    {
        reason = null;

        if (componentType == typeof(Transform))
        {
            reason = "Cannot add a second Transform component.";
            return false;
        }

        if (!typeof(Component).IsAssignableFrom(componentType))
        {
            reason = $"Type {componentType.Name} is not a Component.";
            return false;
        }

        if (GetComponent(componentType) != null)
        {
            reason = $"{componentType.Name} already exists on this entity.";
            return false;
        }

        if (componentType.GetCustomAttributes(typeof(Verity.Core.SingleInstancePerWorldAttribute), true).Length > 0 && World != null)
        {
            foreach (var entity in World.GetAllEntities())
            {
                if (entity == this) continue;

                var conflict = entity.GetComponent(componentType);
                if (conflict != null)
                {
                    reason = $"{componentType.Name} can only exist once per world.";
                    return false;
                }
            }
        }

        return true;
    }

    private void CheckRequiredComponents(Type type)
    {
        var attrs = type.GetCustomAttributes(typeof(Verity.Core.RequireComponentAttribute), true);
        foreach (Verity.Core.RequireComponentAttribute attr in attrs)
        {
            if (GetComponent(attr.RequiredType) == null)
            {
                AddComponent(attr.RequiredType);
            }
        }
    }

    public T? GetComponent<T>() where T : class
    {
        foreach (var component in _components)
        {
            if (component is T typed)
                return typed;
        }
        return default;
    }

    public Component? GetComponent(Type type)
    {
        foreach (var component in _components)
        {
            if (type.IsAssignableFrom(component.GetType()))
                return component;
        }
        return null;
    }

    public IEnumerable<T> GetComponents<T>() where T : class
    {
        foreach (var component in _components)
        {
            if (component is T typed)
                yield return typed;
        }
    }

    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : class
    {
        if (!includeInactive && !Active) return default;

        var comp = GetComponent<T>();
        if (comp != null && (includeInactive || comp is not Component component || component.Enabled)) return comp;

        foreach (var child in Transform.Children)
        {
            var found = child.Owner.GetComponentInChildren<T>(includeInactive);
            if (found != null) return found;
        }

        return default;
    }

    public IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : class
    {
        if (!includeInactive && !Active) yield break;

        foreach (var comp in GetComponents<T>())
        {
            if (includeInactive || comp is not Component component || component.Enabled)
                yield return comp;
        }

        foreach (var child in Transform.Children)
        {
            foreach (var found in child.Owner.GetComponentsInChildren<T>(includeInactive))
                yield return found;
        }
    }

    public T? GetComponentInParent<T>(bool includeInactive = false) where T : class
    {
        if (!includeInactive && !Active) return default;

        var comp = GetComponent<T>();
        if (comp != null && (includeInactive || comp is not Component component || component.Enabled)) return comp;

        return Transform.Parent?.Owner.GetComponentInParent<T>(includeInactive);
    }

    public IEnumerable<T> GetComponentsInParent<T>(bool includeInactive = false) where T : class
    {
        if (!includeInactive && !Active) yield break;

        foreach (var comp in GetComponents<T>())
        {
            if (includeInactive || comp is not Component component || component.Enabled)
                yield return comp;
        }

        if (Transform.Parent != null)
        {
            foreach (var found in Transform.Parent.Owner.GetComponentsInParent<T>(includeInactive))
                yield return found;
        }
    }

    public bool RemoveComponent<T>() where T : Component
    {
        if (typeof(T) == typeof(Transform))
            return false;

        for (var i = _components.Count - 1; i >= 0; i--)
        {
            if (_components[i] is T)
            {
                _components[i].OnDestroy();
                _components.RemoveAt(i);
                return true;
            }
        }
        return false;
    }

    public bool RemoveComponent(Component component)
    {
        if (component is Transform)
            return false;

        if (_components.Remove(component))
        {
            component.OnDestroy();
            return true;
        }
        return false;
    }

    public IReadOnlyList<Component> GetAllComponents() => _components;

    internal IReadOnlyList<Component> Components => _components;

    internal IEnumerable<Script> GetScripts()
    {
        foreach (var component in _components)
        {
            if (component is Script script)
                yield return script;
        }
    }
}

