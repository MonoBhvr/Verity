using Verity.Core.World;

namespace Verity.Core;

public static class VerityCore
{
    public const string Version = "0.0.1";

    public static void ResetRuntime()
    {
        WorldManager.Reset();
        Engine.Time.Reset();
    }
}
