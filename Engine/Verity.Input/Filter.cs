using System;
using System.Collections.Generic;
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
    protected ulong _mask = 0;

    [JsonIgnore]
    public ulong Mask => _mask;

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

    /// <summary>
    /// 비트마스크를 사용하여 초고속으로 필터링을 수행합니다.
    /// </summary>
    public virtual bool Check<T>(T value) where T : struct, Enum
    {
        ulong valueMask = FilterRegistry.GetMask(value);
        bool hasBit = (_mask & valueMask) != 0;
        
        return Mode == FilterMode.Whitelist ? hasBit : !hasBit;
    }

    public virtual IEnumerable<T> GetValues<T>() where T : struct, Enum
    {
        // Whitelist인 경우 마스크된 값만 반환
        // Blacklist인 경우 모든 값에서 마스크된 값을 제외하고 반환해야 하지만, 
        // 일반적으로 GetValues는 등록된 화이트리스트 목록을 확인하는 용도로 쓰이므로 마스크된 값 위주로 반환합니다.
        return FilterRegistry.GetValuesFromMask<T>(_mask);
    }

    public void UpdateCache()
    {
        _mask = 0;

        // 1. Process single-type values
        if (!string.IsNullOrEmpty(EnumTypeName))
        {
            var type = FilterManager.ResolveTypeInternal(EnumTypeName);
            if (type != null)
            {
                foreach (var v in Values)
                {
                    _mask |= FilterRegistry.GetMask(type, v);
                }
            }
        }

        // 2. Process mixed-type values
        foreach (var mv in MixedValues)
        {
            var type = FilterManager.ResolveTypeInternal(mv.TypeName);
            if (type != null)
            {
                _mask |= FilterRegistry.GetMask(type, mv.Value);
            }
        }
    }
}

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
