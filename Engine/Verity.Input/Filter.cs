using System.Text.Json.Serialization;

namespace Verity.Input;

public class FilterValue
{
    public string TypeName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public FilterValue() { }
    public FilterValue(Type type, string value)
    {
        TypeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        Value = value;
    }
}

public class Filter
{
    public string Name { get; set; } = string.Empty;
    
    // For single-type filters (legacy/simple support)
    public string EnumTypeName { get; set; } = string.Empty;
    public List<string> Values { get; set; } = new();
    
    // For mixed-type filters
    public List<FilterValue> MixedValues { get; set; } = new();
    
    public FilterMode Mode { get; set; } = FilterMode.Whitelist;

    [JsonIgnore]
    protected Dictionary<Type, HashSet<object>> _cachedValues = new();

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

    public virtual bool Check<T>(T value) where T : struct, Enum
    {
        if (_cachedValues.Count == 0) UpdateCache();
        
        if (_cachedValues.TryGetValue(typeof(T), out var set))
        {
            bool contains = set.Contains(value);
            return Mode == FilterMode.Whitelist ? contains : !contains;
        }
        
        // If the type isn't in our filter at all:
        // Whitelist -> definitely false
        // Blacklist -> true (it's NOT in the blacklist)
        return Mode == FilterMode.Blacklist;
    }

    public virtual IEnumerable<T> GetValues<T>() where T : struct, Enum
    {
        if (_cachedValues.Count == 0) UpdateCache();
        if (_cachedValues.TryGetValue(typeof(T), out var set))
        {
            foreach (var val in set)
                if (val is T tVal) yield return tVal;
        }
    }

    public void UpdateCache()
    {
        _cachedValues.Clear();

        // 1. Process single-type values
        if (!string.IsNullOrEmpty(EnumTypeName))
        {
            var type = ResolveType(EnumTypeName);
            if (type != null && type.IsEnum)
            {
                var set = GetOrCreateSet(type);
                foreach (var v in Values)
                {
                    try { set.Add(Enum.Parse(type, v)); } catch { }
                }
            }
        }

        // 2. Process mixed-type values
        foreach (var mv in MixedValues)
        {
            var type = ResolveType(mv.TypeName);
            if (type != null && type.IsEnum)
            {
                var set = GetOrCreateSet(type);
                try { set.Add(Enum.Parse(type, mv.Value)); } catch { }
            }
        }
    }

    protected HashSet<object> GetOrCreateSet(Type t)
    {
        if (!_cachedValues.TryGetValue(t, out var set))
        {
            set = new HashSet<object>();
            _cachedValues[t] = set;
        }
        return set;
    }

    protected Type? ResolveType(string typeName)
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

/// <summary>
/// A specialized Filter that is explicitly designed to hold multiple Enum types.
/// Inherits from Filter to maintain same usage pattern.
/// </summary>
public class MixedFilter : Filter
{
    public MixedFilter() : base() { }
    public MixedFilter(string name, FilterMode mode) : base()
    {
        Name = name;
        Mode = mode;
    }

    public void AddValue<T>(T value) where T : struct, Enum
    {
        MixedValues.Add(new FilterValue(typeof(T), value.ToString()!));
        UpdateCache();
    }
}
