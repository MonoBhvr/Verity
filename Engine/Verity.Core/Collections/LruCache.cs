using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace Verity.Core.Collections;

/// <summary>
/// Represents a bounded least-recently-used cache.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public class LruCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable where TKey : notnull
{
    private readonly int _capacity;
    private readonly Action<TKey, TValue>? _onEvict;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _entries;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _usageList = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LruCache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of entries the cache can hold.</param>
    /// <param name="onEvict">An optional callback invoked when an entry is evicted due to capacity.</param>
    public LruCache(int capacity, Action<TKey, TValue>? onEvict = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be greater than zero.");

        _capacity = capacity;
        _onEvict = onEvict;
        _entries = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
    }

    /// <summary>
    /// Gets the number of entries in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            return _entries.Count;
        }
    }

    /// <summary>
    /// Gets the keys from most-recently-used to least-recently-used.
    /// </summary>
    public IEnumerable<TKey> Keys
    {
        get
        {
            ThrowIfDisposed();

            foreach (var pair in _usageList)
                yield return pair.Key;
        }
    }

    /// <summary>
    /// Gets the values from most-recently-used to least-recently-used.
    /// </summary>
    public IEnumerable<TValue> Values
    {
        get
        {
            ThrowIfDisposed();

            foreach (var pair in _usageList)
                yield return pair.Value;
        }
    }

    /// <summary>
    /// Gets a value by key and marks it as most recently used when found.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value when found; otherwise the default value.</returns>
    public TValue? Get(TKey key)
    {
        return TryGetValue(key, out var value) ? value : default;
    }

    /// <summary>
    /// Gets a value by key and marks it as most recently used when found.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The resolved value.</param>
    /// <returns><see langword="true"/> when the key is present; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        ThrowIfDisposed();

        if (!_entries.TryGetValue(key, out var node))
        {
            value = default!;
            return false;
        }

        MoveToFront(node);
        value = node.Value.Value;
        return true;
    }

    /// <summary>
    /// Adds or updates a cache entry.
    /// </summary>
    /// <param name="key">The key to add or update.</param>
    /// <param name="value">The value to store.</param>
    public void Set(TKey key, TValue value)
    {
        ThrowIfDisposed();

        if (_entries.TryGetValue(key, out var existingNode))
        {
            existingNode.Value = new KeyValuePair<TKey, TValue>(key, value);
            MoveToFront(existingNode);
            return;
        }

        if (_entries.Count >= _capacity)
            EvictLeastRecentlyUsed();

        var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new KeyValuePair<TKey, TValue>(key, value));
        _usageList.AddFirst(node);
        _entries[key] = node;
    }

    /// <summary>
    /// Removes an entry by key.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns><see langword="true"/> when the key was found; otherwise <see langword="false"/>.</returns>
    public bool Remove(TKey key)
    {
        ThrowIfDisposed();

        if (!_entries.TryGetValue(key, out var node))
            return false;

        _entries.Remove(key);
        _usageList.Remove(node);
        return true;
    }

    /// <summary>
    /// Determines whether the cache contains the specified key without affecting usage order.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> when the key is present; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(TKey key)
    {
        ThrowIfDisposed();
        return _entries.ContainsKey(key);
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();
        ClearCore();
    }

    /// <summary>
    /// Releases the cache and disposes any disposable values still stored.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        ClearCore();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Returns an enumerator that iterates from most-recently-used to least-recently-used.
    /// </summary>
    /// <returns>An enumerator over the cache contents.</returns>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        ThrowIfDisposed();

        foreach (var pair in _usageList)
            yield return pair;
    }

    /// <summary>
    /// Returns a non-generic enumerator that iterates from most-recently-used to least-recently-used.
    /// </summary>
    /// <returns>A non-generic enumerator over the cache contents.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void MoveToFront(LinkedListNode<KeyValuePair<TKey, TValue>> node)
    {
        if (ReferenceEquals(_usageList.First, node))
            return;

        _usageList.Remove(node);
        _usageList.AddFirst(node);
    }

    private void EvictLeastRecentlyUsed()
    {
        var lruNode = _usageList.Last;
        if (lruNode == null)
            return;

        _usageList.RemoveLast();
        _entries.Remove(lruNode.Value.Key);
        InvokeEviction(lruNode.Value.Key, lruNode.Value.Value);
    }

    private void InvokeEviction(TKey key, TValue value)
    {
        try
        {
            _onEvict?.Invoke(key, value);
        }
        finally
        {
            DisposeValue(value);
        }
    }

    private void ClearCore()
    {
        foreach (var pair in _usageList)
            DisposeValue(pair.Value);

        _entries.Clear();
        _usageList.Clear();
    }

    private static void DisposeValue(TValue value)
    {
        if (value is IDisposable disposable)
            disposable.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);
    }
}

/// <summary>
/// Represents a thread-safe bounded least-recently-used cache.
/// </summary>
/// <typeparam name="TKey">The key type.</typeparam>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class ConcurrentLruCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable where TKey : notnull
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly LruCache<TKey, TValue> _cache;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentLruCache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of entries the cache can hold.</param>
    /// <param name="onEvict">An optional callback invoked when an entry is evicted due to capacity.</param>
    public ConcurrentLruCache(int capacity, Action<TKey, TValue>? onEvict = null)
    {
        _cache = new LruCache<TKey, TValue>(capacity, onEvict);
    }

    /// <summary>
    /// Gets the number of entries in the cache.
    /// </summary>
    public int Count
    {
        get
        {
            ThrowIfDisposed();
            _lock.EnterReadLock();
            try
            {
                return _cache.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets the keys from most-recently-used to least-recently-used.
    /// </summary>
    public IEnumerable<TKey> Keys
    {
        get
        {
            ThrowIfDisposed();
            _lock.EnterReadLock();
            try
            {
                var keys = new List<TKey>(_cache.Count);
                foreach (var pair in _cache)
                    keys.Add(pair.Key);
                return keys;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets the values from most-recently-used to least-recently-used.
    /// </summary>
    public IEnumerable<TValue> Values
    {
        get
        {
            ThrowIfDisposed();
            _lock.EnterReadLock();
            try
            {
                var values = new List<TValue>(_cache.Count);
                foreach (var pair in _cache)
                    values.Add(pair.Value);
                return values;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Gets a value by key and marks it as most recently used when found.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>The value when found; otherwise the default value.</returns>
    public TValue? Get(TKey key)
    {
        return TryGetValue(key, out var value) ? value : default;
    }

    /// <summary>
    /// Gets a value by key and marks it as most recently used when found.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The resolved value.</param>
    /// <returns><see langword="true"/> when the key is present; otherwise <see langword="false"/>.</returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        ThrowIfDisposed();

        _lock.EnterUpgradeableReadLock();
        try
        {
            if (!_cache.ContainsKey(key))
            {
                value = default!;
                return false;
            }

            _lock.EnterWriteLock();
            try
            {
                return _cache.TryGetValue(key, out value);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
        finally
        {
            _lock.ExitUpgradeableReadLock();
        }
    }

    /// <summary>
    /// Adds or updates a cache entry.
    /// </summary>
    /// <param name="key">The key to add or update.</param>
    /// <param name="value">The value to store.</param>
    public void Set(TKey key, TValue value)
    {
        ThrowIfDisposed();

        _lock.EnterWriteLock();
        try
        {
            _cache.Set(key, value);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Removes an entry by key.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns><see langword="true"/> when the key was found; otherwise <see langword="false"/>.</returns>
    public bool Remove(TKey key)
    {
        ThrowIfDisposed();

        _lock.EnterWriteLock();
        try
        {
            return _cache.Remove(key);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Determines whether the cache contains the specified key without affecting usage order.
    /// </summary>
    /// <param name="key">The key to test.</param>
    /// <returns><see langword="true"/> when the key is present; otherwise <see langword="false"/>.</returns>
    public bool ContainsKey(TKey key)
    {
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            return _cache.ContainsKey(key);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Removes all entries from the cache.
    /// </summary>
    public void Clear()
    {
        ThrowIfDisposed();

        _lock.EnterWriteLock();
        try
        {
            _cache.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Releases the cache, disposes any disposable values still stored, and disposes the internal lock.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _lock.EnterWriteLock();
        try
        {
            if (_disposed)
                return;

            _cache.Dispose();
            _disposed = true;
        }
        finally
        {
            if (_lock.IsWriteLockHeld)
                _lock.ExitWriteLock();
        }

        _lock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Returns an enumerator that iterates from most-recently-used to least-recently-used.
    /// </summary>
    /// <returns>An enumerator over the cache contents.</returns>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        ThrowIfDisposed();
        return SnapshotEntries().GetEnumerator();
    }

    /// <summary>
    /// Returns a non-generic enumerator that iterates from most-recently-used to least-recently-used.
    /// </summary>
    /// <returns>A non-generic enumerator over the cache contents.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private List<KeyValuePair<TKey, TValue>> SnapshotEntries()
    {
        _lock.EnterReadLock();
        try
        {
            var items = new List<KeyValuePair<TKey, TValue>>(_cache.Count);
            foreach (var pair in _cache)
                items.Add(pair);
            return items;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().FullName);
    }
}
