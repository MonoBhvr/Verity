namespace Verity.Core.ECS;

public class Entity
{
    public Guid Id { get; internal set; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool Active { get; set; } = true;
    public Transform Transform { get; }

    internal Verity.Core.World.World? World { get; set; }

    private readonly List<Component> _components = [];

    public Entity(string name)
    {
        Name = name;
        Transform = new Transform { Owner = this };
        _components.Add(Transform);
    }

    public T AddComponent<T>() where T : Component, new()
    {
        if (typeof(T) == typeof(Transform))
            throw new InvalidOperationException("Cannot add a second Transform component.");

        var component = new T { Owner = this };
        _components.Add(component);

        if (component is Script script)
            script.Awake();

        return component;
    }

    public Component AddComponent(Type componentType)
    {
        if (componentType == typeof(Transform))
            throw new InvalidOperationException("Cannot add a second Transform component.");

        if (!typeof(Component).IsAssignableFrom(componentType))
            throw new ArgumentException($"Type {componentType.Name} is not a Component.");

        var component = (Component)Activator.CreateInstance(componentType)!;
        component.Owner = this;
        _components.Add(component);

        if (component is Script script)
            script.Awake();

        return component;
    }

    public T? GetComponent<T>() where T : Component
    {
        foreach (var component in _components)
        {
            if (component is T typed)
                return typed;
        }
        return null;
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

    public IEnumerable<T> GetComponents<T>() where T : Component
    {
        foreach (var component in _components)
        {
            if (component is T typed)
                yield return typed;
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

