using System.Text.Json;

namespace Verity.Input;

public static class FilterManager
{
    private static readonly Dictionary<string, Filter> _filters = new();
    private static string _savePath = "Assets/Filters.json";

    private static bool _loaded = false;

    public static string SavePath
    {
        get => _savePath;
        set => _savePath = value;
    }

    public static void Register(Filter filter)
    {
        if (!_loaded) Load();
        filter.UpdateCache(); // Ensure mask is updated
        _filters[filter.Name] = filter;
        Save();
    }

    public static Filter? Get(string name)
    {
        if (!_loaded) Load();
        if (_filters.TryGetValue(name, out var filter))
            return filter;
        return null;
    }

    public static IEnumerable<Filter> GetAllFilters()
    {
        if (!_loaded) Load();
        return _filters.Values;
    }

    public static void Remove(string name)
    {
        if (!_loaded) Load();
        if (_filters.Remove(name))
            Save();
    }

    public static void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_filters.Values, options);
            
            // Ensure directory exists
            var dir = Path.GetDirectoryName(_savePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_savePath, json);
            _loaded = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FilterManager] Error saving filters: {ex.Message}");
        }
    }

    public static void Load()
    {
        _loaded = true;
        if (!File.Exists(_savePath))
        {
            Save(); // Create empty file if it doesn't exist
            return;
        }

        try
        {
            var json = File.ReadAllText(_savePath);
            var filters = JsonSerializer.Deserialize<List<Filter>>(json);
            if (filters != null)
            {
                _filters.Clear();
                foreach (var f in filters)
                {
                    f.UpdateCache();
                    _filters[f.Name] = f;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FilterManager] Error loading filters: {ex.Message}");
        }
    }

    public static Type? ResolveTypeInternal(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type != null) return type;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = asm.GetType(typeName) ?? asm.GetType(typeName.Split(',')[0]);
            if (type != null) return type;
        }
        return null;
    }
}
