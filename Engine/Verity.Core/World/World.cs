using Verity.Core.ECS;

namespace Verity.Core.World;

public class World
{
    public string Name { get; }
    public bool UseCustomSettings { get; set; } = false;
    public int CustomTPS { get; set; } = 60;
    public int CustomPTPS { get; set; } = 50;

    // Custom Physics Settings
    public System.Numerics.Vector2 CustomGravity { get; set; } = new(0, -9.81f);
    public float CustomFriction { get; set; } = 0.5f;
    public float CustomBounciness { get; set; } = 0.0f;
    public float CustomLinearDamping { get; set; } = 0.1f;
    public float CustomAngularDamping { get; set; } = 0.1f;
    public float CustomPhysicsThreshold { get; set; } = 0.05f;

    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _pendingDestroy = [];

    // Script cache: avoids full tree traversal every logic tick
    private readonly List<Script> _cachedScripts = [];
    private bool _scriptCacheDirty = true;

    public World(string name)
    {
        Name = name;
    }

    public IReadOnlyList<Entity> RootEntities => _entities;

    internal void InvalidateScriptCache()
    {
        _scriptCacheDirty = true;
    }

    internal IReadOnlyList<Script> GetActiveScripts()
    {
        if (!_scriptCacheDirty)
            return _cachedScripts;

        _cachedScripts.Clear();
        foreach (var entity in _entities)
        {
            if (!entity.Active) continue;
            CollectScriptsRecursiveInto(entity, _cachedScripts);
        }
        _scriptCacheDirty = false;
        return _cachedScripts;
    }

    private static void CollectScriptsRecursiveInto(Entity entity, List<Script> result)
    {
        foreach (var script in entity.GetScripts())
        {
            if (script.Enabled)
                result.Add(script);
        }

        foreach (var child in entity.Transform.Children)
        {
            if (!child.Owner.Active) continue;
            CollectScriptsRecursiveInto(child.Owner, result);
        }
    }

    public Entity CreateEntity(string name)
    {
        var entity = new Entity(name) { World = this };
        _entities.Add(entity);
        _scriptCacheDirty = true;
        return entity;
    }

    public void AddToRoot(Entity entity)
    {
        if (!_entities.Contains(entity))
        {
            entity.World = this;
            _entities.Add(entity);
            _scriptCacheDirty = true;
        }
    }

    public void AddToRoot(Entity entity, int index)
    {
        if (_entities.Contains(entity))
        {
            SetRootIndex(entity, index);
            return;
        }

        entity.World = this;
        int insertIndex = Math.Clamp(index, 0, _entities.Count);
        _entities.Insert(insertIndex, entity);
        _scriptCacheDirty = true;
    }

    public void RemoveFromRoot(Entity entity)
    {
        _entities.Remove(entity);
        _scriptCacheDirty = true;
    }

    public int IndexOfRoot(Entity entity)
    {
        return _entities.IndexOf(entity);
    }

    public void SetRootIndex(Entity entity, int index)
    {
        int currentIndex = _entities.IndexOf(entity);
        if (currentIndex < 0)
            return;

        int clampedIndex = Math.Clamp(index, 0, _entities.Count - 1);
        if (currentIndex == clampedIndex)
            return;

        _entities.RemoveAt(currentIndex);
        _entities.Insert(clampedIndex, entity);
        _scriptCacheDirty = true;
    }

    public void DestroyEntity(Entity entity)
    {
        if (!_pendingDestroy.Contains(entity))
        {
            _pendingDestroy.Add(entity);
            _scriptCacheDirty = true;
        }
    }

    public void ProcessPendingDestroys()
    {
        if (_pendingDestroy.Count == 0) return;

        foreach (var entity in _pendingDestroy)
        {
            DestroyEntityRecursive(entity);
            _entities.Remove(entity);
            if (entity.Transform.Parent != null)
                entity.Transform.Parent = null;
        }
        _pendingDestroy.Clear();
        _scriptCacheDirty = true;
    }

    private static void DestroyEntityRecursive(Entity entity)
    {
        foreach (var child in entity.Transform.Children.ToArray())
            DestroyEntityRecursive(child.Owner);

        foreach (var script in entity.GetScripts())
            script.OnDestroy();

        entity.World = null;
    }

    internal void ClearAllEntities()
    {
        _entities.Clear();
        _pendingDestroy.Clear();
        _cachedScripts.Clear();
        _scriptCacheDirty = true;
    }

    public IEnumerable<Entity> GetAllEntities()
    {
        foreach (var entity in _entities)
        {
            foreach (var e in GetAllEntitiesRecursive(entity))
                yield return e;
        }
    }

    private static IEnumerable<Entity> GetAllEntitiesRecursive(Entity entity)
    {
        yield return entity;
        foreach (var child in entity.Transform.Children)
        {
            foreach (var e in GetAllEntitiesRecursive(child.Owner))
                yield return e;
        }
    }

    public IEnumerable<T> GetAllComponents<T>() where T : class
    {
        foreach (var entity in GetAllEntities())
        {
            foreach (var component in entity.GetComponents<T>())
            {
                yield return component;
            }
        }
    }

    internal IEnumerable<Script> GetAllScripts()
    {
        foreach (var entity in _entities)
        {
            if (!entity.Active) continue;
            foreach (var script in CollectScriptsRecursive(entity))
                yield return script;
        }
    }

    private static IEnumerable<Script> CollectScriptsRecursive(Entity entity)
    {
        foreach (var script in entity.GetScripts())
        {
            if (script.Enabled)
                yield return script;
        }

        foreach (var child in entity.Transform.Children)
        {
            if (!child.Owner.Active) continue;
            foreach (var script in CollectScriptsRecursive(child.Owner))
                yield return script;
        }
    }
}
