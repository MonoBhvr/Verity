using System;
using System.Collections.Generic;

namespace Verity.Input;

/// <summary>
/// 모든 Enum 값에 대해 엔진 전체에서 고유한 비트 인덱스(0-63)를 관리합니다.
/// </summary>
public static class FilterRegistry
{
    private static readonly Dictionary<(Type, string), int> _valueToBit = new();
    private static readonly Dictionary<int, (Type Type, string Name)> _bitToValue = new();
    private static readonly object _lock = new();
    private static int _nextBitIndex = 0;
    private const int MaxBits = 64;

    /// <summary>
    /// 특정 Enum 값에 할당된 비트 인덱스를 가져옵니다. 없으면 새로 할당합니다.
    /// </summary>
    public static int GetBitIndex<T>(T value) where T : struct, Enum
    {
        return GetBitIndex(typeof(T), value.ToString()!);
    }

    public static int GetBitIndex(Type enumType, string valueName)
    {
        lock (_lock)
        {
            var key = (enumType, valueName);
            if (_valueToBit.TryGetValue(key, out int index))
                return index;

            if (_nextBitIndex >= MaxBits)
            {
                throw new InvalidOperationException($"엔진 전체에서 사용할 수 있는 최대 필터 비트 수({MaxBits})를 초과했습니다.");
            }

            index = _nextBitIndex++;
            _valueToBit[key] = index;
            _bitToValue[index] = (enumType, valueName);
            return index;
        }
    }

    /// <summary>
    /// 마스크 내에서 특정 Enum 타입에 해당하는 모든 값들을 반환합니다.
    /// </summary>
    public static IEnumerable<T> GetValuesFromMask<T>(ulong mask) where T : struct, Enum
    {
        var targetType = typeof(T);
        for (int i = 0; i < MaxBits; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                if (_bitToValue.TryGetValue(i, out var info) && info.Type == targetType)
                {
                    if (Enum.TryParse<T>(info.Name, out var result))
                        yield return result;
                }
            }
        }
    }

    /// <summary>
    /// 특정 Enum 값에 해당하는 비트 마스크(1 << index)를 가져옵니다.
    /// </summary>
    public static ulong GetMask<T>(T value) where T : struct, Enum
    {
        return 1UL << GetBitIndex(value);
    }

    public static ulong GetMask(Type enumType, string valueName)
    {
        return 1UL << GetBitIndex(enumType, valueName);
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
