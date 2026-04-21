using System;
using System.Collections.Generic;

namespace Verity.Filter;

/// <summary>
/// 모든 Enum 값에 대해 엔진 전체에서 고유한 비트 인덱스(0-63)를 관리합니다.
/// </summary>
public static class FilterRegistry
{
    private static readonly Dictionary<(string, string), int> _valueToBit = new();
    private static readonly Dictionary<int, (string TypeName, string Name)> _bitToValue = new();
    private static readonly object _lock = new();
    private static int _nextBitIndex = 0;
    private const int MaxBits = 64;

    public static int GetBitIndex<T>(T value) where T : struct, Enum
    {
        return GetBitIndex(typeof(T).FullName ?? typeof(T).Name, value.ToString()!);
    }

    public static int GetBitIndex(Type enumType, string valueName)
    {
        return GetBitIndex(enumType.FullName ?? enumType.Name, valueName);
    }

    public static int GetBitIndex(string typeName, string valueName)
    {
        lock (_lock)
        {
            var key = (typeName, valueName);
            if (_valueToBit.TryGetValue(key, out int index))
                return index;

            if (_nextBitIndex >= MaxBits)
            {
                throw new InvalidOperationException($"엔진 전체에서 사용할 수 있는 최대 필터 비트 수({MaxBits})를 초과했습니다.");
            }

            index = _nextBitIndex++;
            _valueToBit[key] = index;
            _bitToValue[index] = (typeName, valueName);
            return index;
        }
    }

    public static IEnumerable<T> GetValuesFromMask<T>(ulong mask) where T : struct, Enum
    {
        var targetTypeName = typeof(T).FullName ?? typeof(T).Name;
        for (int i = 0; i < MaxBits; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                if (_bitToValue.TryGetValue(i, out var info) && info.TypeName == targetTypeName)
                {
                    if (Enum.TryParse<T>(info.Name, out var result))
                        yield return result;
                }
            }
        }
    }

    public static ulong GetMask<T>(T value) where T : struct, Enum
    {
        return 1UL << GetBitIndex(value);
    }

    public static ulong GetMask(Type enumType, string valueName)
    {
        return 1UL << GetBitIndex(enumType, valueName);
    }

    public static ulong GetMask(string typeName, string valueName)
    {
        return 1UL << GetBitIndex(typeName, valueName);
    }

    public static ulong GetGroupMask(string groupName)
    {
        // "PhysicsGroup" is the conventional type name used for physics identity layers
        return GetMask("PhysicsGroup", groupName);
    }

    public static void Clear()
    {
        lock (_lock)
        {
            _valueToBit.Clear();
            _bitToValue.Clear();
            _nextBitIndex = 0;
        }
    }
}
