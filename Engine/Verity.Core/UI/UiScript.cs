using Verity.Core.World;

namespace Verity.Core.UI;

public abstract class UiScript
{
    private readonly Dictionary<string, object?> _state = new(StringComparer.OrdinalIgnoreCase);

    public Canvas Canvas { get; internal set; } = null!;
    public World.World? World => Canvas.World;

    public virtual void OnOpen() { }
    public virtual void OnClose() { }
    public virtual void OnUpdate(float deltaTime) { }
    public virtual void OnLayout() { }
    public virtual void OnVariableChanged(string name, object? value) { }
    public virtual void OnCommand(string command, object? payload) { }

    protected T? Query<T>(string nameOrId) where T : UiNode => Canvas.Query<T>(nameOrId);
    protected UiNode? Query(string nameOrId) => Canvas.Query(nameOrId);

    protected void SetState(string name, object? value)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        _state[name] = value;
    }

    protected bool TryGetState<T>(string name, out T? value)
    {
        if (_state.TryGetValue(name, out var raw) && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    internal bool TryResolveState(string name, out object? value) => _state.TryGetValue(name, out value);
    internal IReadOnlyDictionary<string, object?> GetStateValues() => _state;
}
