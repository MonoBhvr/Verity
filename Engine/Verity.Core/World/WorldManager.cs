namespace Verity.Core.World;

public static class WorldManager
{
    private static readonly List<World> _loadedWorlds = [];

    public static World? ActiveWorld { get; private set; }

    public static IReadOnlyList<World> LoadedWorlds => _loadedWorlds;

    public static World? GetWorld(string name)
    {
        return _loadedWorlds.FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public static World CreateWorld(string name)
    {
        var world = new World(name);
        _loadedWorlds.Add(world);

        ActiveWorld ??= world;

        return world;
    }

    public static World CreateOrReplaceWorld(string name)
    {
        var existing = GetWorld(name);
        if (existing != null)
            UnloadWorld(existing);

        return CreateWorld(name);
    }

    public static void SetActiveWorld(World world)
    {
        if (!_loadedWorlds.Contains(world))
            throw new InvalidOperationException($"World '{world.Name}' is not loaded.");

        ActiveWorld = world;
    }

    public static void UnloadWorld(World world)
    {
        if (ActiveWorld == world)
            ActiveWorld = null;

        foreach (var entity in world.RootEntities.ToArray())
            world.DestroyEntity(entity);

        world.ProcessPendingDestroys();
        world.ClearAllEntities();
        _loadedWorlds.Remove(world);
    }

    internal static void Reset()
    {
        while (_loadedWorlds.Count > 0)
            UnloadWorld(_loadedWorlds[^1]);

        ActiveWorld = null;
    }
}
