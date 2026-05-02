using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace Verity.Game.Runtime;

public static class RuntimeBootstrap
{
    private static bool _browserDiagnosticsRegistered;
    private static Action<string, Verity.Core.LogLevel>? _registeredLogHandler;
    private static UnhandledExceptionEventHandler? _registeredUnhandledExceptionHandler;

    public static void RunNative(
        IRuntimeHost runtimeHost,
        bool enableConsole = false,
        bool enableDiagnostics = false)
    {
        if (enableConsole)
            EnsureDebugConsole();

        string executableBaseDir = AppContext.BaseDirectory;
        Assembly assembly = typeof(RuntimeBootstrap).Assembly;
        string assemblyName = assembly.GetName().Name ?? "Verity.Game";
        IRuntimeContentSource contentSource = new RuntimeAssemblyContentSource(assembly, assemblyName, executableBaseDir);
        string baseDir = contentSource.PrepareContentRoot();

        using StreamWriter? logWriter = enableDiagnostics
            ? CreateLogWriter(executableBaseDir, baseDir)
            : null;

        Action<string, string> writeRuntimeLog = CreateLogger(enableConsole || enableDiagnostics, logWriter);

        if (enableDiagnostics)
            RegisterDiagnostics(writeRuntimeLog);

        writeRuntimeLog("Runtime", $"Verity Engine v{Verity.Core.VerityCore.Version}");
        writeRuntimeLog("Runtime", $"ExecutableBaseDir={executableBaseDir}");
        writeRuntimeLog("Runtime", $"ContentBaseDir={baseDir}");

        using var runtimeApp = new RuntimeApp(
            runtimeHost,
            contentSource,
            baseDir,
            FindRuntimeUiFontFamily(),
            writeRuntimeLog);

        while (!runtimeApp.ShouldClose)
            runtimeApp.TickFrame();
    }

    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.Camera))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.CameraOutput))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Audio.AudioListener))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.SpriteRenderer))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.Physical))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.BoxShape))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.CircleShape))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.PolygonRenderer))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.PolygonShape))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Audio.AudioSource))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.TilemapRenderer))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.World.Tilemap))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.TilemapShape))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.Fracture))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.ECS.Animator))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.ECS.ClipAnimator))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.ParticleEmitter))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.NineSliceRenderer))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Graphics.Light2D))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.UI.UiDocument))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Audio.AudioManager))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Physics.Fragment))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.ECS.LuaScriptComponent))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.Animation.ClipAnimatorState))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(Verity.Core.ECS.Script))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.UI.UiScript))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(MoveCharacter))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaScriptContext))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaVector2Value))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaVector3Value))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaColorValue))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaTransformHandle))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaEntityHandle))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaComponentProxy))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaEntityApi))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaTimeApi))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaInputApi))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicConstructors |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Core.Scripting.LuaKeysApi))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicProperties,
        typeof(Verity.Core.StyleData))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicMethods,
        typeof(Verity.Input.Input))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.World.Tile))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.World.AnimatedTile))]
    [DynamicDependency(
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicFields |
        DynamicallyAccessedMemberTypes.NonPublicFields |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.NonPublicProperties,
        typeof(Verity.Core.World.RuleTile))]
    public static BrowserRuntimeSession CreateBrowserSession(IRuntimeHost runtimeHost)
    {
        string executableBaseDir = AppContext.BaseDirectory;
        Assembly assembly = typeof(RuntimeBootstrap).Assembly;
        string assemblyName = assembly.GetName().Name ?? "Verity.Game";
        IRuntimeContentSource contentSource = new EmbeddedRuntimeContentSource(assembly, assemblyName, executableBaseDir);
        string baseDir = contentSource.PrepareContentRoot();

        static void BrowserLog(string category, string message) => Console.WriteLine($"[{category}] {message}");

        RegisterBrowserDiagnostics(BrowserLog);

        bool minimalBrowserMode = runtimeHost.MinimalMode;

        var runtimeApp = new RuntimeApp(
            runtimeHost,
            contentSource,
            baseDir,
            string.Empty,
            BrowserLog,
            minimalBrowserMode);

        return new BrowserRuntimeSession(runtimeApp);
    }

    private static void RegisterBrowserDiagnostics(Action<string, string> writeRuntimeLog)
    {
        if (_browserDiagnosticsRegistered)
            return;

        _browserDiagnosticsRegistered = true;
        RegisterDiagnostics(writeRuntimeLog);
    }

    public static async Task RunBrowserAsync(
        IRuntimeHost runtimeHost,
        CancellationToken cancellationToken = default)
    {
        using var session = CreateBrowserSession(runtimeHost);
        while (!session.ShouldClose && !cancellationToken.IsCancellationRequested)
        {
            session.TickFrame();
            await Task.Yield();
        }
    }

    private static Action<string, string> CreateLogger(bool enabled, StreamWriter? logWriter)
    {
        if (!enabled)
            return static (_, _) => { };

        return (category, message) =>
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] [{category}] {message}";
            Console.WriteLine(line);
            logWriter?.WriteLine(line);
        };
    }

    private static StreamWriter CreateLogWriter(string executableBaseDir, string contentBaseDir)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string logPath = TryGetPreferredLogPath(executableBaseDir, contentBaseDir);
        var writer = new StreamWriter(File.Open(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
        writer.WriteLine($"[{DateTime.Now:HH:mm:ss}] [Runtime] Logging to {logPath}");
        return writer;
    }

    private static void RegisterDiagnostics(Action<string, string> writeRuntimeLog)
    {
        if (_registeredLogHandler != null)
            Verity.Core.Debug.OnLog -= _registeredLogHandler;

        if (_registeredUnhandledExceptionHandler != null)
            AppDomain.CurrentDomain.UnhandledException -= _registeredUnhandledExceptionHandler;

        _registeredLogHandler = (message, level) =>
        {
            string prefix = level switch
            {
                Verity.Core.LogLevel.Warning => "Warn",
                Verity.Core.LogLevel.Error => "Error",
                _ => "Info"
            };

            writeRuntimeLog(prefix, message);
        };
        Verity.Core.Debug.OnLog += _registeredLogHandler;

        _registeredUnhandledExceptionHandler = (_, eventArgs) =>
        {
            writeRuntimeLog("Fatal", eventArgs.ExceptionObject?.ToString() ?? "Unhandled exception");
        };
        AppDomain.CurrentDomain.UnhandledException += _registeredUnhandledExceptionHandler;
    }

    private static string TryGetPreferredLogPath(string executableBaseDir, string contentBaseDir)
    {
        string[] candidates =
        [
            Path.Combine(executableBaseDir, "runtime.log"),
            Path.Combine(contentBaseDir, "runtime.log")
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                string? directory = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                using FileStream stream = File.Open(candidate, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                return candidate;
            }
            catch
            {
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "runtime.log");
    }

    private static string FindRuntimeUiFontFamily()
    {
        string[] candidates =
        [
            "Malgun Gothic",
            "Noto Sans KR",
            "Noto Sans CJK KR",
            "Gulim",
            "Batang",
            "Segoe UI"
        ];

        foreach (string candidate in candidates)
        {
            try
            {
                using var family = new System.Drawing.FontFamily(candidate);
                return family.Name;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static void EnsureDebugConsole()
    {
        if (GetConsoleWindow() != IntPtr.Zero)
            return;

        AllocConsole();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();
}
