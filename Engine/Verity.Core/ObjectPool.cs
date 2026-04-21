namespace Verity.Core;

public class ObjectPool<T> where T : class, new()
{
    private readonly Stack<T> _items;
    private readonly Func<T> _factory;
    private readonly Action<T>? _onGet;
    private readonly Action<T>? _onReturn;

    public ObjectPool(int initialCapacity = 0)
        : this(factory: null, onGet: null, onReturn: null, initialCapacity)
    {
    }

    public ObjectPool(Func<T>? factory, Action<T>? onGet = null, Action<T>? onReturn = null, int initialCapacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(initialCapacity);

        _items = new Stack<T>(initialCapacity);
        _factory = factory ?? (() => new T());
        _onGet = onGet;
        _onReturn = onReturn;

        for (int i = 0; i < initialCapacity; i++)
            _items.Push(_factory());
    }

    public int Count => _items.Count;

    public T Get()
    {
        var item = _items.Count > 0 ? _items.Pop() : _factory();
        _onGet?.Invoke(item);
        return item;
    }

    public void Return(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        _onReturn?.Invoke(item);
        _items.Push(item);
    }

    public void Clear()
    {
        _items.Clear();
    }
}
