namespace Verity.Core;

public static class EventBus
{
    private static readonly Dictionary<Type, Delegate[]> _handlers = [];

    public static void Subscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(T);
        var typedHandler = (Delegate)handler;

        if (_handlers.TryGetValue(eventType, out var existing))
        {
            var newArray = new Delegate[existing.Length + 1];
            Array.Copy(existing, newArray, existing.Length);
            newArray[^1] = typedHandler;
            _handlers[eventType] = newArray;
        }
        else
        {
            _handlers[eventType] = [typedHandler];
        }
    }

    public static void Unsubscribe<T>(Action<T> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventType = typeof(T);

        if (!_handlers.TryGetValue(eventType, out var existing))
            return;

        var index = Array.IndexOf(existing, handler);
        if (index < 0)
            return;

        if (existing.Length == 1)
        {
            _handlers.Remove(eventType);
            return;
        }

        var newArray = new Delegate[existing.Length - 1];
        if (index > 0)
            Array.Copy(existing, 0, newArray, 0, index);
        if (index < existing.Length - 1)
            Array.Copy(existing, index + 1, newArray, index, existing.Length - index - 1);
        _handlers[eventType] = newArray;
    }

    public static void Publish<T>(T eventData)
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers))
            return;

        foreach (var handler in handlers)
            ((Action<T>)handler)(eventData);
    }

    public static void Clear()
    {
        _handlers.Clear();
    }
}
