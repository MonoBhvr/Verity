using System.Reflection;

namespace Verity.Core.ECS;

public abstract class Script : Component
{
    internal bool HasStarted;

    // Lifecycle delegates
    internal Action? _awakeDelegate;
    internal Action? _startDelegate;
    internal Action? _updateDelegate;
    internal Action? _fixedUpdateDelegate;
    internal Action? _lateUpdateDelegate;

    protected Script()
    {
        InitializeLifecycle();
    }

    private void InitializeLifecycle()
    {
        var type = GetType();
        
        _awakeDelegate = CreateLifecycleDelegate(type, "Awake");
        _startDelegate = CreateLifecycleDelegate(type, "Start");
        _updateDelegate = CreateLifecycleDelegate(type, "Update");
        _fixedUpdateDelegate = CreateLifecycleDelegate(type, "FixedUpdate");
        _lateUpdateDelegate = CreateLifecycleDelegate(type, "LateUpdate");
    }

    private Action? CreateLifecycleDelegate(Type type, string methodName)
    {
        // Search for the method in the current type (including private/protected/public)
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        
        if (method == null) return null;

        // Skip if the method is declared in the base Script class itself (it's just an empty virtual)
        if (method.DeclaringType == typeof(Script)) return null;

        // Ensure it has no parameters and returns void
        if (method.GetParameters().Length == 0 && method.ReturnType == typeof(void))
        {
            return (Action)Delegate.CreateDelegate(typeof(Action), this, method);
        }

        return null;
    }

    // Keep these as empty methods so existing 'override' code doesn't break immediately,
    // but they are no longer the primary way the engine calls these methods.
    public virtual void Awake() { }
    public virtual void Start() { }
    public virtual void Update() { }
    public virtual void FixedUpdate() { }
    public virtual void LateUpdate() { }

    public override void OnDestroy() { }
}
