using Verity.Game.Runtime;

namespace Verity.Game;

internal class Program
{
    private static bool RuntimeConsoleEnabled =>
#if VERITY_RUNTIME_CONSOLE
        true;
#else
        false;
#endif
    private static bool RuntimeDiagnosticsEnabled =>
#if VERITY_RUNTIME_DIAGNOSTICS
        true;
#else
        false;
#endif

    private static void Main(string[] args)
    {
        RuntimeBootstrap.RunNative(
            new NativeRuntimeHost(),
            enableConsole: RuntimeConsoleEnabled,
            enableDiagnostics: RuntimeDiagnosticsEnabled);
    }
}


