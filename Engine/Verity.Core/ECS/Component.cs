namespace Verity.Core.ECS;

public abstract class Component
{
    public Entity Owner { get; internal set; } = null!;

    public Transform Transform => Owner.Transform;

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

    #region Convenience Methods
    public T? GetComponent<T>() where T : class => Owner.GetComponent<T>();
    public Component? GetComponent(Type type) => Owner.GetComponent(type);
    public IEnumerable<T> GetComponents<T>() where T : class => Owner.GetComponents<T>();
    public T? GetComponentInChildren<T>(bool includeInactive = false) where T : class => Owner.GetComponentInChildren<T>(includeInactive);
    public IEnumerable<T> GetComponentsInChildren<T>(bool includeInactive = false) where T : class => Owner.GetComponentsInChildren<T>(includeInactive);
    public T? GetComponentInParent<T>(bool includeInactive = false) where T : class => Owner.GetComponentInParent<T>(includeInactive);
    public IEnumerable<T> GetComponentsInParent<T>(bool includeInactive = false) where T : class => Owner.GetComponentsInParent<T>(includeInactive);
    #endregion
}
