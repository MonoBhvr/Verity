namespace Verity.Core.ECS;

public abstract class Component
{
    public Entity Owner { get; internal set; } = null!;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (_enabled)
                OnEnable();
            else
                OnDisable();
        }
    }

    private bool _enabled = true;

    protected virtual void OnEnable() { }

    protected virtual void OnDisable() { }

    public virtual void OnDestroy() { }
}
