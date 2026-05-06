using Verity.Core.ECS;

namespace Verity.Core.Engine;

internal static class ScriptRuntimeExceptionPolicy
{
    internal static bool ShouldContinue(Exception exception)
    {
        return exception is not OutOfMemoryException;
    }

    internal static void LogContinuedException(string phase, Script script, Exception exception)
    {
        Debug.LogError($"[Script] Runtime exception in {phase} on {script.GetType().Name} attached to '{script.Owner.Name}'. Execution continued: {exception}");
    }
}
