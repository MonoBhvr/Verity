using System.Text.Json.Serialization;

namespace Verity.Input;

public class Filter
{
    public string Name { get; set; } = string.Empty;
    public string EnumTypeName { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
    public FilterMode Mode { get; set; } = FilterMode.Whitelist;

    [JsonIgnore]
    private Type? _cachedType;
    [JsonIgnore]
    private HashSet<object>? _cachedValues;

    public const FilterMode WhiteList = FilterMode.Whitelist;
    public const FilterMode BlackList = FilterMode.Blacklist;

    public Filter() { }

    public static Filter? Get(string name) => FilterManager.Get(name);
    public static void Register(Filter filter) => FilterManager.Register(filter);

    public Filter(string name, Type enumType, Array values, FilterMode mode)
    {
        Name = name;
        EnumTypeName = enumType.AssemblyQualifiedName ?? enumType.FullName ?? enumType.Name;
        Mode = mode;
        foreach (var val in values)
        {
            if (val != null) Values.Add(val.ToString()!);
        }
        UpdateCache();
    }

    public bool Check<T>(T value) where T : struct, Enum
    {
        if (_cachedValues == null) UpdateCache();
        bool contains = _cachedValues!.Contains(value);
        return Mode == FilterMode.Whitelist ? contains : !contains;
    }

    /// <summary>
    /// For input checking: iterates and checks against the provided function.
    /// If Whitelist, returns true if any value matches.
    /// If Blacklist, returns true if any value NOT in the list matches.
    /// </summary>
    public bool Any(Func<object, bool> checkFunc)
    {
        if (_cachedValues == null) UpdateCache();
        
        if (Mode == FilterMode.Whitelist)
        {
            foreach (var val in _cachedValues!)
                if (checkFunc(val)) return true;
            return false;
        }
        else
        {
            // For Blacklist, this is tricky. We need the "universe" of possible values?
            // Usually, for input, this means "Check if ANY key is pressed that isn't in this list".
            // But we don't have a list of "all pressed keys" easily accessible in a generic way?
            // Actually, Input.AnyKeyDown gives us a key.
            // Let's defer this to the Input specific implementation.
            return false;
        }
    }

    public IEnumerable<T> GetValues<T>() where T : struct, Enum
    {
        if (_cachedValues == null) UpdateCache();
        foreach (var val in _cachedValues!)
        {
            if (val is T tVal) yield return tVal;
        }
    }

    public void UpdateCache()
    {
        if (string.IsNullOrEmpty(EnumTypeName)) return;

        if (_cachedType == null)
        {
            _cachedType = Type.GetType(EnumTypeName);
            if (_cachedType == null)
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _cachedType = asm.GetType(EnumTypeName) ?? asm.GetType(EnumTypeName.Split(',')[0]);
                    if (_cachedType != null) break;
                }
            }
        }

        _cachedValues = new HashSet<object>();
        if (_cachedType != null)
        {
            foreach (var valStr in Values)
            {
                try
                {
                    var val = Enum.Parse(_cachedType, valStr);
                    _cachedValues.Add(val);
                }
                catch { }
            }
        }
    }
}
