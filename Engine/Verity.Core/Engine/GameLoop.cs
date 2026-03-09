using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Core.Engine;

public class GameLoop
{
    public Action? OnUpdate { get; set; }
    public Action? OnFixedUpdate { get; set; }
    public Action? OnLateUpdate { get; set; }
    public Action? OnRender { get; set; }

    private float _fixedTimeAccumulator;
    private const float FixedDeltaTime = 1f / 60f;

    public void TickLogic(float deltaTime)
    {
        if (WorldLoader.PendingWorldName != null) return;

        var scaledDelta = deltaTime * Time.TimeScale;
        Time.DeltaTime = scaledDelta;
        Time.TotalTime += scaledDelta;
        Time.FrameCount++;

        var world = WorldManager.ActiveWorld;
        if (world == null) return;

        // Collect all active scripts from the current world
        var scripts = world.GetAllScripts().ToList();

        // 1. Awake/Start Phase
        foreach (var script in scripts)
        {
            if (!script.HasStarted)
            {
                script.Awake();
                script.Start();
                script.HasStarted = true;
            }
        }

        // 2. Fixed Update Phase
        _fixedTimeAccumulator += scaledDelta;
        while (_fixedTimeAccumulator >= FixedDeltaTime)
        {
            foreach (var script in scripts) script.FixedUpdate();
            OnFixedUpdate?.Invoke();
            _fixedTimeAccumulator -= FixedDeltaTime;
        }

        // 3. Update Phase
        foreach (var script in scripts) script.Update();
        OnUpdate?.Invoke();

        // 4. Late Update Phase
        foreach (var script in scripts) script.LateUpdate();
        OnLateUpdate?.Invoke();

        world.ProcessPendingDestroys();
    }

    public void TickRender()
    {
        OnRender?.Invoke();
    }
}
