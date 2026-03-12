using System.Collections;
using System.Reflection;
using Verity.Core.Physics;
using Verity.Core.Engine;

namespace Verity.Core.ECS;

public class Coroutine
{
    internal IEnumerator Routine;
    internal object? Wait;
    internal float Timer;
    internal int StartTick;
    internal bool IsDone;
    
    public Coroutine(IEnumerator routine) => Routine = routine;
}

public class WaitForSeconds
{
    public float Seconds { get; }
    public WaitForSeconds(float seconds) => Seconds = seconds;
}

public class WaitForTicks
{
    public int Ticks { get; }
    public WaitForTicks(int ticks) => Ticks = ticks;
}

public class WaitForPhysicalTicks
{
    public int Ticks { get; }
    public WaitForPhysicalTicks(int ticks) => Ticks = ticks;
}

public class WaitUntil
{
    public Func<bool> Predicate { get; }
    public WaitUntil(Func<bool> predicate) => Predicate = predicate;
}

public class WaitWhile
{
    public Func<bool> Predicate { get; }
    public WaitWhile(Func<bool> predicate) => Predicate = predicate;
}

public abstract class Script : Component
{
    internal bool HasAwoken;
    internal bool HasStarted;

    // Lifecycle delegates
    internal Action? _awakeDelegate;
    internal Action? _startDelegate;
    internal Action? _updateDelegate;
    internal Action? _fixedUpdateDelegate;
    internal Action? _lateUpdateDelegate;
    internal Action? _onDrawGizmosDelegate;
    internal Action? _onDrawGizmosSelectedDelegate;

    // Physics delegates
    internal Action<Physical>? _onTouchedDelegate;
    internal Action<Physical>? _onTouchingDelegate;
    internal Action<Entity>? _onTouchEndDelegate;
    internal Action<Entity>? _onDetectedDelegate;
    internal Action<Entity>? _onDetectingDelegate;
    internal Action<Entity>? _onDetectEndDelegate;

    // Coroutine Management
    private readonly List<Coroutine> _activeCoroutines = new();
    private readonly List<Coroutine> _coroutinesToRemove = new();

    protected Script()
    {
        InitializeLifecycle();
    }

    private void InitializeLifecycle()
    {
        var type = GetType();
        
        _awakeDelegate = CreateLifecycleDelegate(type, "Awake", false); 
        _startDelegate = CreateLifecycleDelegate(type, "Start", true);
        _updateDelegate = CreateLifecycleDelegate(type, "Update", false);
        _fixedUpdateDelegate = CreateLifecycleDelegate(type, "FixedUpdate", false);
        _lateUpdateDelegate = CreateLifecycleDelegate(type, "LateUpdate", false);
        _onDrawGizmosDelegate = CreateLifecycleDelegate(type, "OnDrawGizmos", false);
        _onDrawGizmosSelectedDelegate = CreateLifecycleDelegate(type, "OnDrawGizmosSelected", false);

        // Physics delegates
        _onTouchedDelegate = CreatePhysicsDelegate<Physical>(type, "OnTouched");
        _onTouchingDelegate = CreatePhysicsDelegate<Physical>(type, "OnTouching");
        _onTouchEndDelegate = CreatePhysicsDelegate<Entity>(type, "OnTouchEnd");
        _onDetectedDelegate = CreatePhysicsDelegate<Entity>(type, "OnDetected");
        _onDetectingDelegate = CreatePhysicsDelegate<Entity>(type, "OnDetecting");
        _onDetectEndDelegate = CreatePhysicsDelegate<Entity>(type, "OnDetectEnd");
    }

    private Action? CreateLifecycleDelegate(Type type, string methodName, bool allowCoroutine)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null || method.DeclaringType == typeof(Script)) return null;
        
        if (method.GetParameters().Length == 0)
        {
            if (method.ReturnType == typeof(void))
                return (Action)Delegate.CreateDelegate(typeof(Action), this, method);
            
            if (allowCoroutine && method.ReturnType == typeof(IEnumerator))
            {
                var func = (Func<IEnumerator>)Delegate.CreateDelegate(typeof(Func<IEnumerator>), this, method);
                return () => StartCoroutine(func());
            }
        }
        return null;
    }

    private Action<T>? CreatePhysicsDelegate<T>(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null || method.DeclaringType == typeof(Script)) return null;
        
        var parameters = method.GetParameters();
        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(T))
        {
            if (method.ReturnType == typeof(void))
                return (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), this, method);
            
            if (method.ReturnType == typeof(IEnumerator))
            {
                var func = (Func<T, IEnumerator>)Delegate.CreateDelegate(typeof(Func<T, IEnumerator>), this, method);
                return (val) => StartCoroutine(func(val));
            }
        }
        return null;
    }

    public Coroutine StartCoroutine(IEnumerator routine)
    {
        var coroutine = new Coroutine(routine);
        _activeCoroutines.Add(coroutine);
        return coroutine;
    }

    public void StopCoroutine(Coroutine coroutine)
    {
        if (coroutine == null) return;
        _coroutinesToRemove.Add(coroutine);
    }

    public void StopAllCoroutines()
    {
        _activeCoroutines.Clear();
    }

    internal void UpdateCoroutines(float deltaTime)
    {
        if (_coroutinesToRemove.Count > 0)
        {
            foreach (var c in _coroutinesToRemove) _activeCoroutines.Remove(c);
            _coroutinesToRemove.Clear();
        }

        for (int i = _activeCoroutines.Count - 1; i >= 0; i--)
        {
            var coroutine = _activeCoroutines[i];
            
            if (IsWaiting(coroutine, deltaTime)) continue;

            if (!coroutine.Routine.MoveNext())
            {
                coroutine.IsDone = true;
                _activeCoroutines.RemoveAt(i);
                continue;
            }

            coroutine.Wait = coroutine.Routine.Current;
            coroutine.Timer = 0;
            if (coroutine.Wait is WaitForTicks)
                coroutine.StartTick = Time.LogicTickCount;
            else if (coroutine.Wait is WaitForPhysicalTicks)
                coroutine.StartTick = Time.PhysicsTickCount;
        }
    }

    private bool IsWaiting(Coroutine coroutine, float deltaTime)
    {
        if (coroutine.Wait == null) return false;

        if (coroutine.Wait is WaitForSeconds wfs)
        {
            coroutine.Timer += deltaTime;
            return coroutine.Timer < wfs.Seconds;
        }

        if (coroutine.Wait is WaitForTicks wft)
        {
            return (Time.LogicTickCount - coroutine.StartTick) < wft.Ticks;
        }

        if (coroutine.Wait is WaitForPhysicalTicks wfpt)
        {
            return (Time.PhysicsTickCount - coroutine.StartTick) < wfpt.Ticks;
        }

        if (coroutine.Wait is WaitUntil wu) return !wu.Predicate();
        if (coroutine.Wait is WaitWhile ww) return ww.Predicate();
        if (coroutine.Wait is Coroutine other) return !other.IsDone;
        if (coroutine.Wait is IEnumerator nested)
        {
            if (!nested.MoveNext()) { coroutine.Wait = null; return false; }
            return true;
        }

        return false;
    }

    public virtual void Awake() { }
    public virtual void Start() { }
    public virtual void Update() { }
    public virtual void FixedUpdate() { }
    public virtual void LateUpdate() { }
    public virtual void OnDrawGizmos() { }
    public virtual void OnDrawGizmosSelected() { }

    // Physics virtuals
    public virtual void OnTouched(Physical other) { }
    public virtual void OnTouching(Physical other) { }
    public virtual void OnTouchEnd(Entity other) { }
    public virtual void OnDetected(Entity other) { }
    public virtual void OnDetecting(Entity other) { }
    public virtual void OnDetectEnd(Entity other) { }

    public override void OnDestroy() { StopAllCoroutines(); }

    #region Static Shortcuts
    public static Entity? Find(string name) => Entity.Find(name);
    public static Entity? FindWithTag(string tag) => Entity.FindWithTag(tag);
    public static Entity[] FindEntitiesWithTag(string tag) => Entity.FindEntitiesWithTag(tag);
    public static T? FindObjectOfType<T>(bool includeInactive = false) where T : Component => Entity.FindObjectOfType<T>(includeInactive);
    public static T[] FindObjectsOfType<T>(bool includeInactive = false) where T : Component => Entity.FindObjectsOfType<T>(includeInactive);
    public static void Destroy(Entity entity) => Entity.Destroy(entity);
    public static void Destroy(Component component) => Entity.Destroy(component);
    public static Entity Instantiate(string name = "New Entity") => Entity.Instantiate(name);
    public static Entity? Instantiate(Entity original) => Entity.Instantiate(original);
    public static T? Instantiate<T>(T original) where T : Component => Entity.Instantiate(original);
    #endregion
}
