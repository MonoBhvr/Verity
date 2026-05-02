using Verity.Core.World;
using Verity.Core.Physics;
using Verity.Core.UI;

namespace Verity.Core;

public static class VerityCore
{
    public const string Version = "0.1.1-alpha";

    public static void ResetRuntime()
    {
        WorldManager.Reset();
        EventBus.Clear();
        UiSystem.Clear();
        ParticleSystem.Clear();
        PhysicsManager.Reset();
        Engine.Time.Reset();
        Debug.ClearDrawCommands();
    }
}
