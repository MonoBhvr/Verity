using System.Collections;
using Lua;
using Verity.Core.Engine;
using Verity.Core.Scripting;

namespace Verity.Core.ECS;

public sealed class LuaScriptComponent : Script
{
    private LuaState? _state;
    public LuaState? State => _state;
    private LuaValue _awakeFunction = LuaValue.Nil;
    private LuaValue _startFunction = LuaValue.Nil;
    private LuaValue _updateFunction = LuaValue.Nil;
    private LuaValue _fixedUpdateFunction = LuaValue.Nil;
    private LuaValue _lateUpdateFunction = LuaValue.Nil;
    private LuaValue _createCoroutineFunction = LuaValue.Nil;
    private LuaValue _resumeCoroutineFunction = LuaValue.Nil;
    private LuaValue _coroutineStatusFunction = LuaValue.Nil;

    private string _scriptPath = string.Empty;

    [SerializeField, AssetReference(".lua")]
    public string ScriptPath
    {
        get => _scriptPath;
        set
        {
            string normalized = AssetPathUtility.Normalize(value);
            if (string.Equals(_scriptPath, normalized, StringComparison.OrdinalIgnoreCase))
                return;

            _scriptPath = normalized;
            ReloadScript();
        }
    }

    [SerializeField, HideInInspector]
    public string ScriptGuid { get; set; } = string.Empty;

    [HideInInspector]
    public string ResolvedScriptPath => string.IsNullOrWhiteSpace(ScriptPath)
        ? string.Empty
        : LuaScriptManager.ResolveScriptPath(ScriptPath, ScriptGuid);

    internal bool HasLoadedState => _state != null;
    internal bool HasStartFunction => _startFunction.Type != LuaValueType.Nil;

    public LuaScriptComponent()
    {
        _awakeDelegate = InvokeAwake;
        _startDelegate = InvokeStart;
        _updateDelegate = InvokeUpdate;
        _fixedUpdateDelegate = InvokeFixedUpdate;
        _lateUpdateDelegate = InvokeLateUpdate;
    }

    protected override void OnEnable()
    {
        LuaScriptManager.RegisterComponent(this);

        if (!string.IsNullOrWhiteSpace(ScriptPath))
            ReloadScript();
    }

    protected override void OnDisable()
    {
        LuaScriptManager.UnregisterComponent(this);
        StopAllCoroutines();
    }

    public override void OnDestroy()
    {
        LuaScriptManager.UnregisterComponent(this);
        DisposeState();
        base.OnDestroy();
    }

    public void ReloadScript()
    {
        HasAwoken = false;
        HasStarted = false;

        _awakeFunction = LuaValue.Nil;
        _startFunction = LuaValue.Nil;
        _updateFunction = LuaValue.Nil;
        _fixedUpdateFunction = LuaValue.Nil;
        _lateUpdateFunction = LuaValue.Nil;
        _createCoroutineFunction = LuaValue.Nil;
        _resumeCoroutineFunction = LuaValue.Nil;
        _coroutineStatusFunction = LuaValue.Nil;

        StopAllCoroutines();
        DisposeState();

        if (Owner == null || string.IsNullOrWhiteSpace(ScriptPath))
            return;

        LuaScriptManager.RegisterComponent(this);
        LuaScriptManager.Initialize(LuaScriptManager.AssetRootPath, LuaScriptManager.UserAssembly);

        string fullPath = ResolvedScriptPath;
        if (!File.Exists(fullPath))
        {
            Debug.LogError($"[Lua] Script not found: {ScriptPath}");
            return;
        }

        try
        {
            _state = LuaScriptManager.CreateState(this);
            string source = File.ReadAllText(fullPath);
            _state.DoStringAsync(source, chunkName: Path.GetFileName(fullPath)).GetAwaiter().GetResult();
            CacheLifecycleFunctions();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Lua] Failed to load '{ScriptPath}': {ex.Message}");
            DisposeState();
        }
    }

    internal void NotifyExternalScriptChanged(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(ScriptPath))
            return;

        string current = ResolvedScriptPath;
        if (!string.Equals(Path.GetFullPath(current), Path.GetFullPath(fullPath), StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            ReloadScript();
            Debug.Log($"[Lua] Reloaded script: {ScriptPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Lua] Hot reload failed for '{ScriptPath}': {ex.Message}");
        }
    }

    internal Coroutine? StartLuaCoroutine(string functionName)
    {
        if (_state == null || string.IsNullOrWhiteSpace(functionName))
            return null;

        LuaValue function = _state.Environment[functionName];
        return function.Type == LuaValueType.Nil ? null : StartLuaCoroutine(function);
    }

    internal Coroutine? StartLuaCoroutine(LuaValue function, params LuaValue[] arguments)
    {
        if (_state == null || function.Type == LuaValueType.Nil || _createCoroutineFunction.Type == LuaValueType.Nil)
            return null;

        try
        {
            var routine = new LuaCoroutineRoutine(this, _state, _createCoroutineFunction, _resumeCoroutineFunction, _coroutineStatusFunction, function, arguments);
            return StartCoroutine(routine);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Lua] Failed to start coroutine in '{ScriptPath}': {ex.Message}");
            return null;
        }
    }

    private void CacheLifecycleFunctions()
    {
        if (_state == null)
            return;

        _awakeFunction = _state.Environment["Awake"];
        _startFunction = _state.Environment["Start"];
        _updateFunction = _state.Environment["Update"];
        _fixedUpdateFunction = _state.Environment["FixedUpdate"];
        _lateUpdateFunction = _state.Environment["LateUpdate"];
        _createCoroutineFunction = _state.Environment["__verity_create_coroutine"];
        _resumeCoroutineFunction = _state.Environment["__verity_resume_coroutine"];
        _coroutineStatusFunction = _state.Environment["__verity_coroutine_status"];
    }

    private void InvokeAwake()
    {
        InvokeFunction(_awakeFunction);
    }

    private void InvokeStart()
    {
        if (StartLuaCoroutine(_startFunction) == null)
            InvokeFunction(_startFunction);
    }

    private void InvokeUpdate()
    {
        InvokeFunction(_updateFunction, Time.DeltaTime);
    }

    private void InvokeFixedUpdate()
    {
        InvokeFunction(_fixedUpdateFunction, Time.DeltaTime);
    }

    private void InvokeLateUpdate()
    {
        InvokeFunction(_lateUpdateFunction, Time.DeltaTime);
    }

    private void InvokeFunction(LuaValue function, params LuaValue[] arguments)
    {
        if (_state == null || function.Type == LuaValueType.Nil)
            return;

        try
        {
            _state.CallAsync(function, arguments).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Lua] Runtime error in '{ScriptPath}': {ex.Message}");
        }
    }

    private void DisposeState()
    {
        _state?.Dispose();
        _state = null;
    }

    private sealed class LuaCoroutineRoutine : IEnumerator
    {
        private readonly LuaScriptComponent _component;
        private readonly LuaState _state;
        private readonly LuaValue _resumeCoroutineFunction;
        private readonly LuaValue _coroutineStatusFunction;
        private readonly LuaValue _thread;
        private readonly LuaValue[] _initialArguments;
        private bool _started;

        public LuaCoroutineRoutine(
            LuaScriptComponent component,
            LuaState state,
            LuaValue createCoroutineFunction,
            LuaValue resumeCoroutineFunction,
            LuaValue coroutineStatusFunction,
            LuaValue function,
            LuaValue[] arguments)
        {
            _component = component;
            _state = state;
            _resumeCoroutineFunction = resumeCoroutineFunction;
            _coroutineStatusFunction = coroutineStatusFunction;
            _initialArguments = arguments;

            var createResults = _state.CallAsync(createCoroutineFunction, [function]).GetAwaiter().GetResult();
            _thread = createResults[0];
        }

        public object? Current { get; private set; }

        public bool MoveNext()
        {
            LuaValue[] resumeArgs = _started ? [_thread] : [_thread, .. _initialArguments];
            _started = true;

            var resumeResults = _state.CallAsync(_resumeCoroutineFunction, resumeArgs).GetAwaiter().GetResult();
            if (resumeResults.Length == 0)
                return false;

            bool ok = resumeResults[0].Read<bool>();
            if (!ok)
            {
                string error = resumeResults.Length > 1 ? resumeResults[1].ToString() : "Unknown Lua coroutine error.";
                throw new InvalidOperationException($"Lua coroutine failed in '{_component.ScriptPath}': {error}");
            }

            string status = GetStatus();
            object? yielded = resumeResults.Length > 1 ? ConvertYieldValue(resumeResults[1]) : null;

            if (string.Equals(status, "dead", StringComparison.OrdinalIgnoreCase))
            {
                Current = null;
                return false;
            }

            Current = yielded;
            return true;
        }

        public void Reset() => throw new NotSupportedException();

        private string GetStatus()
        {
            var statusResults = _state.CallAsync(_coroutineStatusFunction, [_thread]).GetAwaiter().GetResult();
            return statusResults.Length > 0 ? statusResults[0].Read<string>() : "dead";
        }

        private static object? ConvertYieldValue(LuaValue value)
        {
            if (value.Type == LuaValueType.Nil)
                return null;

            if (value.TryRead<WaitForSeconds>(out var waitForSeconds)) return waitForSeconds;
            if (value.TryRead<WaitForTicks>(out var waitForTicks)) return waitForTicks;
            if (value.TryRead<WaitForPhysicalTicks>(out var waitForPhysicalTicks)) return waitForPhysicalTicks;
            if (value.TryRead<WaitUntil>(out var waitUntil)) return waitUntil;
            if (value.TryRead<WaitWhile>(out var waitWhile)) return waitWhile;
            if (value.TryRead<Coroutine>(out var coroutine)) return coroutine;
            if (value.TryRead<IEnumerator>(out var enumerator)) return enumerator;
            if (value.TryRead<object>(out var obj)) return obj;

            return null;
        }
    }
}
