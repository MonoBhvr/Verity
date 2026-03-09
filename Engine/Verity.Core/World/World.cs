using Verity.Core.ECS;

namespace Verity.Core.World;

public class World
{
    public string Name { get; }

    private readonly List<Entity> _entities = [];
    private readonly List<Entity> _pendingDestroy = [];

    public World(string name)
    {
        Name = name;
    }

    public IReadOnlyList<Entity> RootEntities => _entities;

    public Entity CreateEntity(string name)
    {
        var entity = new Entity(name) { World = this };
        _entities.Add(entity);
        return entity;
    }

    public void AddToRoot(Entity entity)
    {
        if (!_entities.Contains(entity))
        {
            entity.World = this;
            _entities.Add(entity);
        }
    }

    public void RemoveFromRoot(Entity entity)
    {
        _entities.Remove(entity);
    }

    public void DestroyEntity(Entity entity)
    {
        if (!_pendingDestroy.Contains(entity))
            _pendingDestroy.Add(entity);
    }

    public void ProcessPendingDestroys()
    {
        foreach (var entity in _pendingDestroy)
        {
            DestroyEntityRecursive(entity);
            _entities.Remove(entity);
            if (entity.Transform.Parent != null)
                entity.Transform.Parent = null;
        }
        _pendingDestroy.Clear();
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
