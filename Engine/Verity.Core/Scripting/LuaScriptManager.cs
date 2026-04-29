using System.Collections.Concurrent;
using System.Reflection;
using Lua;
using Lua.Standard;
using Verity.Core.ECS;
using Verity.Core.Engine;
using Verity.Core.Serialization;
using Verity.Input;

namespace Verity.Core.Scripting {

public static class LuaScriptManager
{
    private static readonly object Sync = new();
    private static readonly HashSet<LuaScriptComponent> Components = [];
    private static readonly ConcurrentDictionary<string, long> RecentReloads = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Type> RegisteredComponentTypes = new(StringComparer.OrdinalIgnoreCase);

    private static LuaState? _bootstrapState;
    private static FileSystemWatcher? _watcher;
    private static string? _assetRootPath;
    private static Assembly? _userAssembly;

    public static event Action<IReadOnlyList<string>>? HotReloadRequested;

    public static bool IsInitialized => _bootstrapState != null;
    public static bool SuspendHotReloadEvents { get; set; }
    public static Assembly? UserAssembly => _userAssembly;
    internal static string? AssetRootPath => _assetRootPath;

    public static void Initialize(string? assetRootPath = null, Assembly? userAssembly = null)
    {
        lock (Sync)
        {
            string resolvedRoot = ResolveAssetRoot(assetRootPath);
            if (_bootstrapState != null &&
                string.Equals(_assetRootPath, resolvedRoot, StringComparison.OrdinalIgnoreCase) &&
                Equals(_userAssembly, userAssembly))
            {
                return;
            }

            DisposeInternal();

            _assetRootPath = resolvedRoot;
            _userAssembly = userAssembly;
            RebuildRegisteredTypes();

            _bootstrapState = CreateLuaStateCore();
            RegisterSharedEnvironment(_bootstrapState);
            _bootstrapState.DoStringAsync(BootstrapScript, chunkName: "verity_lua_bootstrap")
                .GetAwaiter()
                .GetResult();

            try
            {
                CreateWatcher(resolvedRoot);
            }
            catch (PlatformNotSupportedException)
            {
                _watcher = null;
            }
        }
    }

    public static void RefreshBindings(Assembly? userAssembly, string? assetRootPath = null)
    {
        Initialize(assetRootPath, userAssembly);
    }

    public static void Dispose()
    {
        lock (Sync)
        {
            DisposeInternal();
        }
    }

    internal static LuaState CreateState(LuaScriptComponent component)
    {
        lock (Sync)
        {
            if (_bootstrapState == null)
                Initialize();
        }

        var state = CreateLuaStateCore();
        RegisterSharedEnvironment(state);
        RegisterComponentEnvironment(state, component);
        state.DoStringAsync(BootstrapScript, chunkName: "verity_lua_bootstrap")
            .GetAwaiter()
            .GetResult();
        return state;
    }

    internal static void RegisterComponent(LuaScriptComponent component)
    {
        lock (Sync)
        {
            Components.Add(component);
        }
    }

    internal static void UnregisterComponent(LuaScriptComponent component)
    {
        lock (Sync)
        {
            Components.Remove(component);
        }
    }

    internal static string ResolveScriptPath(string scriptPath, string? scriptGuid = null)
    {
        string basePath = _assetRootPath ?? ResolveAssetRoot(null);
        return AssetPathUtility.ResolvePath(basePath, scriptPath, scriptGuid);
    }

    internal static Type? ResolveComponentType(string componentTypeName)
    {
        lock (Sync)
        {
            return RegisteredComponentTypes.TryGetValue(componentTypeName, out var type) ? type : null;
        }
    }

    internal static void NotifyScriptChangedForTesting(string path)
    {
        NotifyComponentsForReload(path);
    }

    private static void DisposeInternal()
    {
        _watcher?.Dispose();
        _watcher = null;

        _bootstrapState?.Dispose();
        _bootstrapState = null;
        _assetRootPath = null;
        _userAssembly = null;
        RegisteredComponentTypes.Clear();
        RecentReloads.Clear();
    }

    private static LuaState CreateLuaStateCore()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        return state;
    }

    private static void RegisterSharedEnvironment(LuaState state)
    {
        state.Environment["print"] = new LuaFunction((context, cancellationToken) =>
        {
            var values = new string[context.ArgumentCount];
            for (int i = 0; i < context.ArgumentCount; i++)
                values[i] = context.GetArgument<object>(i)?.ToString() ?? "nil";

            Debug.Log($"[Lua] {string.Join("\t", values)}");
            return new(context.Return());
        });

        state.Environment["Vector2"] = new LuaFunction((context, cancellationToken) =>
        {
            float x = context.ArgumentCount > 0 ? (float)context.GetArgument<double>(0) : 0f;
            float y = context.ArgumentCount > 1 ? (float)context.GetArgument<double>(1) : 0f;
            return new(context.Return(LuaValue.FromObject(new LuaVector2Value(x, y))));
        });

        state.Environment["Vector3"] = new LuaFunction((context, cancellationToken) =>
        {
            float x = context.ArgumentCount > 0 ? (float)context.GetArgument<double>(0) : 0f;
            float y = context.ArgumentCount > 1 ? (float)context.GetArgument<double>(1) : 0f;
            float z = context.ArgumentCount > 2 ? (float)context.GetArgument<double>(2) : 0f;
            return new(context.Return(LuaValue.FromObject(new LuaVector3Value(x, y, z))));
        });

        state.Environment["Color"] = new LuaFunction((context, cancellationToken) =>
        {
            float r = context.ArgumentCount > 0 ? (float)context.GetArgument<double>(0) : 0f;
            float g = context.ArgumentCount > 1 ? (float)context.GetArgument<double>(1) : 0f;
            float b = context.ArgumentCount > 2 ? (float)context.GetArgument<double>(2) : 0f;
            float a = context.ArgumentCount > 3 ? (float)context.GetArgument<double>(3) : 1f;
            return new(context.Return(LuaValue.FromObject(new LuaColorValue(r, g, b, a))));
        });

        state.Environment["Time"] = LuaValue.FromObject(new LuaTimeApi());
        state.Environment["__verity_input"] = LuaValue.FromObject(new LuaInputApi());
        state.Environment["Keys"] = LuaValue.FromObject(new LuaKeysApi());
        state.Environment["Entity"] = LuaValue.FromObject(new LuaEntityApi(state));

        state.Environment["Wait"] = new LuaFunction((context, cancellationToken) =>
        {
            float seconds = context.ArgumentCount > 0 ? (float)context.GetArgument<double>(0) : 0f;
            return new(context.Return(LuaValue.FromObject(new WaitForSeconds(seconds))));
        });

        state.Environment["WaitForSeconds"] = new LuaFunction((context, cancellationToken) =>
        {
            float seconds = context.ArgumentCount > 0 ? (float)context.GetArgument<double>(0) : 0f;
            return new(context.Return(LuaValue.FromObject(new WaitForSeconds(seconds))));
        });

        state.Environment["WaitForTicks"] = new LuaFunction((context, cancellationToken) =>
        {
            int ticks = context.ArgumentCount > 0 ? (int)context.GetArgument<double>(0) : 0;
            return new(context.Return(LuaValue.FromObject(new WaitForTicks(ticks))));
        });

        state.Environment["WaitForPhysicalTicks"] = new LuaFunction((context, cancellationToken) =>
        {
            int ticks = context.ArgumentCount > 0 ? (int)context.GetArgument<double>(0) : 0;
            return new(context.Return(LuaValue.FromObject(new WaitForPhysicalTicks(ticks))));
        });

        state.Environment["WaitUntil"] = new LuaFunction((context, cancellationToken) =>
        {
            object? predicate = context.GetArgument<object>(0);
            return new(context.Return(LuaValue.FromObject(new WaitUntil(() => EvaluatePredicate(state, predicate)))));
        });

        state.Environment["WaitWhile"] = new LuaFunction((context, cancellationToken) =>
        {
            object? predicate = context.GetArgument<object>(0);
            return new(context.Return(LuaValue.FromObject(new WaitWhile(() => EvaluatePredicate(state, predicate)))));
        });
    }

    private static void RegisterComponentEnvironment(LuaState state, LuaScriptComponent component)
    {
        var owner = new LuaEntityHandle(state, component.Owner);
        var context = new LuaScriptContext(component, owner);
        state.Environment["self"] = context;
        state.Environment["Owner"] = LuaValue.FromObject(owner);
    }

    private static bool EvaluatePredicate(LuaState state, object? predicate)
    {
        if (predicate == null)
            return false;

        LuaValue callable = predicate is LuaValue luaValue ? luaValue : LuaValue.FromObject(predicate);
        if (callable.Type == LuaValueType.Nil)
            return false;

        var results = state.CallAsync(callable, [])
            .GetAwaiter()
            .GetResult();

        return results.Length > 0 && results[0].Read<bool>();
    }

    private static void CreateWatcher(string assetRootPath)
    {
        string assetsPath = Path.GetFileName(assetRootPath).Equals("Assets", StringComparison.OrdinalIgnoreCase)
            ? assetRootPath
            : Path.Combine(assetRootPath, "Assets");

        if (!Directory.Exists(assetsPath))
            return;

        _watcher = new FileSystemWatcher(assetsPath, "*.lua")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };

        _watcher.Changed += OnScriptFileChanged;
        _watcher.Created += OnScriptFileChanged;
        _watcher.Deleted += OnScriptFileChanged;
        _watcher.Renamed += OnScriptFileRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    private static void OnScriptFileChanged(object sender, FileSystemEventArgs e)
    {
        NotifyComponentsForReload(e.FullPath);
    }

    private static void OnScriptFileRenamed(object sender, RenamedEventArgs e)
    {
        NotifyComponentsForReload(e.OldFullPath);
        NotifyComponentsForReload(e.FullPath);
    }

    private static void NotifyComponentsForReload(string path)
    {
        string normalizedPath = Path.GetFullPath(path);
        long now = DateTime.UtcNow.Ticks;
        long previous = RecentReloads.GetOrAdd(normalizedPath, 0);
        if (now - previous < TimeSpan.FromMilliseconds(150).Ticks)
            return;

        RecentReloads[normalizedPath] = now;

        if (SuspendHotReloadEvents)
            return;

        var handlers = HotReloadRequested;
        if (handlers != null)
        {
            handlers([normalizedPath]);
            return;
        }

        LuaScriptComponent[] targets;
        lock (Sync)
        {
            targets = Components.ToArray();
        }

        foreach (var component in targets)
            component.NotifyExternalScriptChanged(normalizedPath);
    }

    private static void RebuildRegisteredTypes()
    {
        RegisteredComponentTypes.Clear();

        RegisterAssemblyTypes(typeof(Component).Assembly, includeAllComponents: true);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string? name = assembly.GetName().Name;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (name.StartsWith("Verity.Core", StringComparison.Ordinal) ||
                name.StartsWith("Verity.Graphics", StringComparison.Ordinal) ||
                name.StartsWith("Verity.Input", StringComparison.Ordinal))
            {
                RegisterAssemblyTypes(assembly, includeAllComponents: true);
            }
        }

        if (_userAssembly != null)
            RegisterAssemblyTypes(_userAssembly, includeAllComponents: true);
    }

    private static void RegisterAssemblyTypes(Assembly assembly, bool includeAllComponents)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(static type => type != null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            if (!includeAllComponents || !typeof(Component).IsAssignableFrom(type) || type.IsAbstract)
                continue;

            RegisterTypeAlias(type.Name, type);
            if (!string.IsNullOrWhiteSpace(type.FullName))
                RegisterTypeAlias(type.FullName!, type);
        }
    }

    private static void RegisterTypeAlias(string alias, Type type)
    {
        if (string.IsNullOrWhiteSpace(alias) || RegisteredComponentTypes.ContainsKey(alias))
            return;

        RegisteredComponentTypes[alias] = type;
    }

    private static string ResolveAssetRoot(string? assetRootPath)
    {
        if (!string.IsNullOrWhiteSpace(assetRootPath))
            return Path.GetFullPath(assetRootPath);

        if (!string.IsNullOrWhiteSpace(SceneSerializer.AssetRootPath))
            return Path.GetFullPath(SceneSerializer.AssetRootPath!);

        return Path.GetFullPath(AppContext.BaseDirectory);
    }

    private const string BootstrapScript = """
function __verity_create_coroutine(fn)
    return coroutine.create(fn)
end

function __verity_resume_coroutine(co, ...)
    return coroutine.resume(co, ...)
end

function __verity_coroutine_status(co)
    return coroutine.status(co)
end

function __verity_wrap_component(component)
    return setmetatable({}, {
        __index = function(_, key)
            local value = component:GetMember(key)
            if type(value) == "function" then
                return function(_, ...)
                    return value(...)
                end
            end

            return value
        end,
        __newindex = function(_, key, value)
            component:SetMember(key, value)
        end
    })
end

Input = {
    IsKeyDown = function(selfOrKey, maybeKey)
        local key = maybeKey ~= nil and maybeKey or selfOrKey
        return __verity_input:IsKeyDown(key)
    end,
    IsKeyPressed = function(selfOrKey, maybeKey)
        local key = maybeKey ~= nil and maybeKey or selfOrKey
        return __verity_input:IsKeyPressed(key)
    end,
    IsKeyReleased = function(selfOrKey, maybeKey)
        local key = maybeKey ~= nil and maybeKey or selfOrKey
        return __verity_input:IsKeyReleased(key)
    end
}
""";

    internal static LuaValue ConvertToLuaValue(LuaState state, object? value)
    {
        return value switch
        {
            null => LuaValue.Nil,
            LuaValue luaValue => luaValue,
            string text => text,
            bool boolean => boolean,
            int number => number,
            float single => single,
            double dbl => dbl,
            KeyCode keyCode => (int)keyCode,
            Vector2 vector2 => LuaValue.FromObject(new LuaVector2Value(vector2.X, vector2.Y)),
            Vector3 vector3 => LuaValue.FromObject(new LuaVector3Value(vector3.X, vector3.Y, vector3.Z)),
            Color color => LuaValue.FromObject(LuaColorValue.FromColor(color)),
            Transform transform => LuaValue.FromObject(new LuaTransformHandle(transform)),
            Entity entity => LuaValue.FromObject(new LuaEntityHandle(state, entity)),
            Component component => WrapComponent(state, component),
            _ => LuaValue.FromObject(value)
        };
    }

    internal static object? ConvertFromLuaValue(LuaValue value, Type targetType)
    {
        if (value.Type == LuaValueType.Nil)
            return null;

        if (targetType == typeof(string)) return value.Read<string>();
        if (targetType == typeof(bool)) return value.Read<bool>();
        if (targetType == typeof(int)) return (int)value.Read<double>();
        if (targetType == typeof(float)) return (float)value.Read<double>();
        if (targetType == typeof(double)) return value.Read<double>();
        if (targetType == typeof(KeyCode)) return (KeyCode)(int)value.Read<double>();
        if (targetType == typeof(Vector2) && value.TryRead<LuaVector2Value>(out var wrappedVector2))
            return wrappedVector2.ToVector2();
        if (targetType == typeof(Vector3) && value.TryRead<LuaVector3Value>(out var wrappedVector3))
            return wrappedVector3.ToVector3();
        if (targetType == typeof(Color) && value.TryRead<LuaColorValue>(out var wrappedColor))
            return wrappedColor.ToColor();

        return value.TryRead<object>(out var obj) ? obj : null;
    }

    internal static object? ConvertArgumentObject(object? value, Type targetType)
    {
        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (value is LuaValue luaValue)
            return ConvertFromLuaValue(luaValue, targetType);

        if (targetType.IsInstanceOfType(value))
            return value;

        if (targetType == typeof(string)) return Convert.ToString(value);
        if (targetType == typeof(bool)) return Convert.ToBoolean(value);
        if (targetType == typeof(int)) return Convert.ToInt32(value);
        if (targetType == typeof(float)) return Convert.ToSingle(value);
        if (targetType == typeof(double)) return Convert.ToDouble(value);
        if (targetType == typeof(KeyCode)) return value is string name ? Enum.Parse<KeyCode>(name, true) : (KeyCode)Convert.ToInt32(value);
        if (targetType == typeof(Vector2) && value is LuaVector2Value vector2Value) return vector2Value.ToVector2();
        if (targetType == typeof(Vector3) && value is LuaVector3Value vector3Value) return vector3Value.ToVector3();
        if (targetType == typeof(Color) && value is LuaColorValue colorValue) return colorValue.ToColor();

        return value;
    }

    internal static LuaValue WrapComponent(LuaState state, Component component)
    {
        LuaValue wrapFunction = state.Environment["__verity_wrap_component"];
        if (wrapFunction.Type == LuaValueType.Nil)
            return LuaValue.FromObject(new LuaComponentProxy(state, component));

        var results = state.CallAsync(wrapFunction, [LuaValue.FromObject(new LuaComponentProxy(state, component))])
            .GetAwaiter()
            .GetResult();
        return results.Length > 0 ? results[0] : LuaValue.Nil;
    }

}

[LuaObject]
public partial class LuaScriptContext
{
    private readonly LuaScriptComponent _component;
    private readonly LuaEntityHandle _owner;

    public LuaScriptContext(LuaScriptComponent component, LuaEntityHandle owner)
    {
        _component = component;
        _owner = owner;
    }

    [LuaMember("Owner")]
    public LuaEntityHandle Owner => _owner;

    [LuaMember("StartCoroutine")]
    public bool StartCoroutine(LuaValue function)
    {
        return _component.StartLuaCoroutine(function) != null;
    }

    [LuaMember("StartCoroutineByName")]
    public bool StartCoroutineByName(string functionName)
    {
        return _component.StartLuaCoroutine(functionName) != null;
    }

    [LuaMember("StopAllCoroutines")]
    public void StopAllCoroutines()
    {
        _component.StopAllCoroutines();
    }
}

[LuaObject]
public partial class LuaVector2Value
{
    public LuaVector2Value()
    {
    }

    public LuaVector2Value(float x, float y)
    {
        X = x;
        Y = y;
    }

    [LuaMember("X")]
    public float X { get; set; }

    [LuaMember("Y")]
    public float Y { get; set; }

    [LuaMember("create")]
    public static LuaVector2Value Create(float x, float y) => new(x, y);

    public Vector2 ToVector2() => new(X, Y);
    public static LuaVector2Value FromVector2(Vector2 value) => new(value.X, value.Y);
    public override string ToString() => $"Vector2({X}, {Y})";
}

[LuaObject]
public partial class LuaVector3Value
{
    public LuaVector3Value()
    {
    }

    public LuaVector3Value(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    [LuaMember("X")]
    public float X { get; set; }

    [LuaMember("Y")]
    public float Y { get; set; }

    [LuaMember("Z")]
    public float Z { get; set; }

    [LuaMember("create")]
    public static LuaVector3Value Create(float x, float y, float z) => new(x, y, z);

    public Vector3 ToVector3() => new(X, Y, Z);
    public static LuaVector3Value FromVector3(Vector3 value) => new(value.X, value.Y, value.Z);
    public override string ToString() => $"Vector3({X}, {Y}, {Z})";
}

[LuaObject]
public partial class LuaColorValue
{
    public LuaColorValue()
    {
    }

    public LuaColorValue(float r, float g, float b, float a = 1f)
    {
        R = r;
        G = g;
        B = b;
        A = a;
    }

    [LuaMember("R")]
    public float R { get; set; }

    [LuaMember("G")]
    public float G { get; set; }

    [LuaMember("B")]
    public float B { get; set; }

    [LuaMember("A")]
    public float A { get; set; }

    [LuaMember("create")]
    public static LuaColorValue Create(float r, float g, float b, float a = 1f) => new(r, g, b, a);

    [LuaMember("FromRgba")]
    public static LuaColorValue FromRgba(int r, int g, int b, int a = 255) => FromColor(Color.FromRgba(r, g, b, a));

    public Color ToColor() => new(R, G, B, A);
    public static LuaColorValue FromColor(Color color) => new(color.R, color.G, color.B, color.A);
    public override string ToString() => $"Color({R}, {G}, {B}, {A})";
}

[LuaObject]
public partial class LuaTransformHandle
{
    private readonly Transform _transform;

    public LuaTransformHandle(Transform transform)
    {
        _transform = transform;
    }

    [LuaMember("Position")]
    public LuaVector2Value Position
    {
        get => LuaVector2Value.FromVector2(_transform.Position);
        set => _transform.Position = value?.ToVector2() ?? Vector2.Zero;
    }

    [LuaMember("Rotation")]
    public float Rotation
    {
        get => _transform.Rotation;
        set => _transform.Rotation = value;
    }

    [LuaMember("Scale")]
    public LuaVector2Value Scale
    {
        get => LuaVector2Value.FromVector2(_transform.Scale);
        set => _transform.Scale = value?.ToVector2() ?? Vector2.One;
    }
}

[LuaObject]
public partial class LuaEntityHandle
{
    private readonly LuaState _state;
    private readonly Entity _entity;

    public LuaEntityHandle(LuaState state, Entity entity)
    {
        _state = state;
        _entity = entity;
    }

    [LuaMember("Name")]
    public string Name
    {
        get => _entity.Name;
        set => _entity.Name = value;
    }

    [LuaMember("Tag")]
    public string Tag
    {
        get => _entity.Tag;
        set => _entity.Tag = value;
    }

    [LuaMember("Active")]
    public bool Active
    {
        get => _entity.Active;
        set => _entity.Active = value;
    }

    [LuaMember("Transform")]
    public LuaTransformHandle Transform => new(_entity.Transform);

    [LuaMember("HasComponent")]
    public bool HasComponent(string componentTypeName) => FindComponent(componentTypeName) != null;

    [LuaMember("GetComponent")]
    public LuaValue GetComponent(string componentTypeName)
    {
        Component? component = FindComponent(componentTypeName);
        return component != null ? LuaScriptManager.WrapComponent(_state, component) : LuaValue.Nil;
    }

    [LuaMember("GetField")]
    public LuaValue GetField(string componentTypeName, string memberName)
    {
        Component? component = FindComponent(componentTypeName);
        if (component == null)
            return LuaValue.Nil;

        MemberInfo? member = GetReadableMember(component.GetType(), memberName);
        if (member == null)
            return LuaValue.Nil;

        object? value = member switch
        {
            PropertyInfo property => property.GetValue(component),
            FieldInfo field => field.GetValue(component),
            _ => null
        };

        return LuaScriptManager.ConvertToLuaValue(_state, value);
    }

    [LuaMember("SetField")]
    public bool SetField(string componentTypeName, string memberName, LuaValue value)
    {
        Component? component = FindComponent(componentTypeName);
        if (component == null)
            return false;

        MemberInfo? member = GetWritableMember(component.GetType(), memberName);
        if (member == null)
            return false;

        Type targetType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        object? converted = LuaScriptManager.ConvertFromLuaValue(value, targetType);
        if (converted == null && targetType.IsValueType)
            return false;

        if (member is PropertyInfo writableProperty)
            writableProperty.SetValue(component, converted);
        else
            ((FieldInfo)member).SetValue(component, converted);

        return true;
    }

    [LuaMember("Invoke")]
    public LuaValue Invoke(string componentTypeName, string methodName)
    {
        Component? component = FindComponent(componentTypeName);
        if (component == null)
            return LuaValue.Nil;

        MethodInfo? method = component.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
        if (method == null)
            return LuaValue.Nil;

        object? result = method.Invoke(component, null);
        return LuaScriptManager.ConvertToLuaValue(_state, result);
    }

    private Component? FindComponent(string componentTypeName)
    {
        Type? resolvedType = LuaScriptManager.ResolveComponentType(componentTypeName);
        if (resolvedType != null)
            return _entity.GetComponent(resolvedType);

        return _entity.GetAllComponents().FirstOrDefault(component =>
            string.Equals(component.GetType().Name, componentTypeName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(component.GetType().FullName, componentTypeName, StringComparison.OrdinalIgnoreCase));
    }

    private static MemberInfo? GetReadableMember(Type type, string memberName)
    {
        return (MemberInfo?)type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)
                ?? type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
    }

    private static MemberInfo? GetWritableMember(Type type, string memberName)
    {
        var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite == true)
            return property;

        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        return field is { IsInitOnly: false } ? field : null;
    }
}

[LuaObject]
public partial class LuaComponentProxy
{
    private readonly LuaState _state;
    private readonly Component _component;

    public LuaComponentProxy(LuaState state, Component component)
    {
        _state = state;
        _component = component;
    }

    [LuaMember("GetMember")]
    public LuaValue GetMember(string memberName)
    {
        MemberInfo? member = GetReadableMember(_component.GetType(), memberName);
        if (member != null)
        {
            object? value = member switch
            {
                PropertyInfo property => property.GetValue(_component),
                FieldInfo field => field.GetValue(_component),
                _ => null
            };

            return LuaScriptManager.ConvertToLuaValue(_state, value);
        }

        MethodInfo? method = ResolveMethod(memberName);
        if (method == null)
            return LuaValue.Nil;

        return new LuaFunction((context, cancellationToken) =>
        {
            object?[] arguments = BuildArguments(method, context);
            object? result = method.Invoke(_component, arguments);
            return new(context.Return(LuaScriptManager.ConvertToLuaValue(_state, result)));
        });
    }

    [LuaMember("SetMember")]
    public bool SetMember(string memberName, object? value)
    {
        MemberInfo? member = GetWritableMember(_component.GetType(), memberName);
        if (member == null)
            return false;

        Type targetType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        object? converted = LuaScriptManager.ConvertArgumentObject(value, targetType);
        if (converted == null && targetType.IsValueType)
            return false;

        if (member is PropertyInfo writableProperty)
            writableProperty.SetValue(_component, converted);
        else
            ((FieldInfo)member).SetValue(_component, converted);

        return true;
    }

    private MethodInfo? ResolveMethod(string memberName)
    {
        return _component.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(method => string.Equals(method.Name, memberName, StringComparison.OrdinalIgnoreCase));
    }

    private static object?[] BuildArguments(MethodInfo method, dynamic context)
    {
        ParameterInfo[] parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            object? argumentValue = context.GetArgument<object>(i);
            arguments[i] = LuaScriptManager.ConvertArgumentObject(argumentValue, parameters[i].ParameterType);
        }

        return arguments;
    }

    private static MemberInfo? GetReadableMember(Type type, string memberName)
    {
        return (MemberInfo?)type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)
            ?? type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
    }

    private static MemberInfo? GetWritableMember(Type type, string memberName)
    {
        var property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.CanWrite == true)
            return property;

        var field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
        return field is { IsInitOnly: false } ? field : null;
    }
}

[LuaObject]
public partial class LuaEntityApi
{
    private readonly LuaState _state;

    public LuaEntityApi(LuaState state)
    {
        _state = state;
    }

    [LuaMember("Find")]
    public LuaEntityHandle? Find(string name)
    {
        return Entity.Find(name) is { } entity ? new LuaEntityHandle(_state, entity) : null;
    }

    [LuaMember("FindWithTag")]
    public LuaEntityHandle? FindWithTag(string tag)
    {
        return Entity.FindWithTag(tag) is { } entity ? new LuaEntityHandle(_state, entity) : null;
    }
}

[LuaObject]
public partial class LuaTimeApi
{
    [LuaMember("DeltaTime")]
    public float DeltaTime => Time.DeltaTime;

    [LuaMember("TotalTime")]
    public float TotalTime => Time.TotalTime;

    [LuaMember("LogicTickCount")]
    public int LogicTickCount => Time.LogicTickCount;

    [LuaMember("PhysicsTickCount")]
    public int PhysicsTickCount => Time.PhysicsTickCount;
}

[LuaObject]
public partial class LuaInputApi
{
    [LuaMember("IsKeyDown")]
    public bool IsKeyDown(object key) => TryConvertKeyCode(key, out var keyCode) && global::Verity.Input.Input.Down(keyCode);

    [LuaMember("IsKeyPressed")]
    public bool IsKeyPressed(object key) => TryConvertKeyCode(key, out var keyCode) && global::Verity.Input.Input.Pressed(keyCode);

    [LuaMember("IsKeyReleased")]
    public bool IsKeyReleased(object key) => TryConvertKeyCode(key, out var keyCode) && global::Verity.Input.Input.Released(keyCode);

    private static bool TryConvertKeyCode(object? value, out KeyCode keyCode)
    {
        if (value is null)
        {
            keyCode = default;
            return false;
        }

        if (value is double numeric)
        {
            keyCode = (KeyCode)(int)numeric;
            return true;
        }

        if (value is int numericInt)
        {
            keyCode = (KeyCode)numericInt;
            return true;
        }

        if (value is string name && Enum.TryParse(name, true, out keyCode))
            return true;

        if (value is LuaValue luaValue)
        {
            if (luaValue.TryRead<KeyCode>(out keyCode))
                return true;

            if (luaValue.TryRead<double>(out var boxedNumeric))
            {
                keyCode = (KeyCode)(int)boxedNumeric;
                return true;
            }

            if (luaValue.TryRead<string>(out var boxedName) && Enum.TryParse(boxedName, true, out keyCode))
                return true;
        }

        keyCode = default;
        return false;
    }
}

[LuaObject]
public partial class LuaKeysApi
{
    [LuaMember("Unknown")] public int Unknown => (int)KeyCode.Unknown;
    [LuaMember("A")] public int A => (int)KeyCode.A;
    [LuaMember("B")] public int B => (int)KeyCode.B;
    [LuaMember("C")] public int C => (int)KeyCode.C;
    [LuaMember("D")] public int D => (int)KeyCode.D;
    [LuaMember("E")] public int E => (int)KeyCode.E;
    [LuaMember("F")] public int F => (int)KeyCode.F;
    [LuaMember("G")] public int G => (int)KeyCode.G;
    [LuaMember("H")] public int H => (int)KeyCode.H;
    [LuaMember("I")] public int I => (int)KeyCode.I;
    [LuaMember("J")] public int J => (int)KeyCode.J;
    [LuaMember("K")] public int K => (int)KeyCode.K;
    [LuaMember("L")] public int L => (int)KeyCode.L;
    [LuaMember("M")] public int M => (int)KeyCode.M;
    [LuaMember("N")] public int N => (int)KeyCode.N;
    [LuaMember("O")] public int O => (int)KeyCode.O;
    [LuaMember("P")] public int P => (int)KeyCode.P;
    [LuaMember("Q")] public int Q => (int)KeyCode.Q;
    [LuaMember("R")] public int R => (int)KeyCode.R;
    [LuaMember("S")] public int S => (int)KeyCode.S;
    [LuaMember("T")] public int T => (int)KeyCode.T;
    [LuaMember("U")] public int U => (int)KeyCode.U;
    [LuaMember("V")] public int V => (int)KeyCode.V;
    [LuaMember("W")] public int W => (int)KeyCode.W;
    [LuaMember("X")] public int X => (int)KeyCode.X;
    [LuaMember("Y")] public int Y => (int)KeyCode.Y;
    [LuaMember("Z")] public int Z => (int)KeyCode.Z;
    [LuaMember("Alpha0")] public int Alpha0 => (int)KeyCode.Alpha0;
    [LuaMember("Alpha1")] public int Alpha1 => (int)KeyCode.Alpha1;
    [LuaMember("Alpha2")] public int Alpha2 => (int)KeyCode.Alpha2;
    [LuaMember("Alpha3")] public int Alpha3 => (int)KeyCode.Alpha3;
    [LuaMember("Alpha4")] public int Alpha4 => (int)KeyCode.Alpha4;
    [LuaMember("Alpha5")] public int Alpha5 => (int)KeyCode.Alpha5;
    [LuaMember("Alpha6")] public int Alpha6 => (int)KeyCode.Alpha6;
    [LuaMember("Alpha7")] public int Alpha7 => (int)KeyCode.Alpha7;
    [LuaMember("Alpha8")] public int Alpha8 => (int)KeyCode.Alpha8;
    [LuaMember("Alpha9")] public int Alpha9 => (int)KeyCode.Alpha9;
    [LuaMember("F1")] public int F1 => (int)KeyCode.F1;
    [LuaMember("F2")] public int F2 => (int)KeyCode.F2;
    [LuaMember("F3")] public int F3 => (int)KeyCode.F3;
    [LuaMember("F4")] public int F4 => (int)KeyCode.F4;
    [LuaMember("F5")] public int F5 => (int)KeyCode.F5;
    [LuaMember("F6")] public int F6 => (int)KeyCode.F6;
    [LuaMember("F7")] public int F7 => (int)KeyCode.F7;
    [LuaMember("F8")] public int F8 => (int)KeyCode.F8;
    [LuaMember("F9")] public int F9 => (int)KeyCode.F9;
    [LuaMember("F10")] public int F10 => (int)KeyCode.F10;
    [LuaMember("F11")] public int F11 => (int)KeyCode.F11;
    [LuaMember("F12")] public int F12 => (int)KeyCode.F12;
    [LuaMember("Space")] public int Space => (int)KeyCode.Space;
    [LuaMember("Return")] public int Return => (int)KeyCode.Return;
    [LuaMember("Escape")] public int Escape => (int)KeyCode.Escape;
    [LuaMember("Backspace")] public int Backspace => (int)KeyCode.Backspace;
    [LuaMember("Tab")] public int Tab => (int)KeyCode.Tab;
    [LuaMember("Delete")] public int Delete => (int)KeyCode.Delete;
    [LuaMember("UpArrow")] public int UpArrow => (int)KeyCode.UpArrow;
    [LuaMember("DownArrow")] public int DownArrow => (int)KeyCode.DownArrow;
    [LuaMember("LeftArrow")] public int LeftArrow => (int)KeyCode.LeftArrow;
    [LuaMember("RightArrow")] public int RightArrow => (int)KeyCode.RightArrow;
    [LuaMember("LeftShift")] public int LeftShift => (int)KeyCode.LeftShift;
    [LuaMember("RightShift")] public int RightShift => (int)KeyCode.RightShift;
    [LuaMember("LeftCtrl")] public int LeftCtrl => (int)KeyCode.LeftCtrl;
    [LuaMember("RightCtrl")] public int RightCtrl => (int)KeyCode.RightCtrl;
    [LuaMember("LeftAlt")] public int LeftAlt => (int)KeyCode.LeftAlt;
    [LuaMember("RightAlt")] public int RightAlt => (int)KeyCode.RightAlt;
    [LuaMember("MouseLeft")] public int MouseLeft => (int)KeyCode.MouseLeft;
    [LuaMember("MouseRight")] public int MouseRight => (int)KeyCode.MouseRight;
    [LuaMember("MouseMiddle")] public int MouseMiddle => (int)KeyCode.MouseMiddle;
    [LuaMember("MouseX1")] public int MouseX1 => (int)KeyCode.MouseX1;
    [LuaMember("MouseX2")] public int MouseX2 => (int)KeyCode.MouseX2;
}

}
