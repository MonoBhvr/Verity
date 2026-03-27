using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Core.Engine;

public class GameLoop
{
    public Action? OnUpdate { get; set; }
    public Action? OnFixedUpdate { get; set; }
    public Action? OnPhysicsTick { get; set; }
    public Action? OnLateUpdate { get; set; }
    public Action? OnRender { get; set; }

    private float _logicAccumulator;
    private float _physicsAccumulator;

    public ProjectSettings ProjectSettings { get; set; } = ProjectSettings.Default;

    public void TickLogic(float deltaTime)
    {
        if (WorldLoader.PendingWorldName != null) return;

        var world = WorldManager.ActiveWorld;
        if (world == null) return;

        float scaledDelta = deltaTime * Time.TimeScale;

        int targetTPS = world.UseCustomSettings ? world.CustomTPS : ProjectSettings.TargetTPS;
        int targetPTPS = world.UseCustomSettings ? world.CustomPTPS : ProjectSettings.TargetPTPS;

        Time.TargetTPS = targetTPS;
        Time.TargetPTPS = targetPTPS;

        float logicFixedDelta = 1.0f / Math.Max(1, targetTPS);
        float physicsFixedDelta = 1.0f / Math.Max(1, targetPTPS);

        _logicAccumulator += scaledDelta;
        while (_logicAccumulator >= logicFixedDelta)
        {
            PerformLogicTick(world, logicFixedDelta);
            _logicAccumulator -= logicFixedDelta;
        }

        _physicsAccumulator += scaledDelta;
        while (_physicsAccumulator >= physicsFixedDelta)
        {
            PerformPhysicsTick(world, physicsFixedDelta);
            _physicsAccumulator -= physicsFixedDelta;
        }
        
        world.ProcessPendingDestroys();
    }

    private void PerformLogicTick(World.World world, float fixedDelta)
    {
        Verity.Input.Input.NewLogicTick();

        Time.DeltaTime = fixedDelta;
        Time.TotalTime += fixedDelta;
        Time.LogicTickCount++;

        Verity.Core.Animation.AnimationSystem.Update(fixedDelta);

        var scripts = world.GetAllScripts().ToList();

        // Start Phase
        foreach (var script in scripts)
        {
            if (!script.HasAwoken)
            {
                script._awakeDelegate?.Invoke();
                script.HasAwoken = true;
            }
        }

        foreach (var script in scripts)
        {
            if (!script.HasStarted)
            {
                script._startDelegate?.Invoke();
                script.HasStarted = true;
            }
        }

        // FixedUpdate
        foreach (var script in scripts) script._fixedUpdateDelegate?.Invoke();
        OnFixedUpdate?.Invoke();

        // Update Phase
        foreach (var script in scripts) script._updateDelegate?.Invoke();
        OnUpdate?.Invoke();

        // Coroutine Update Phase
        foreach (var script in scripts) script.UpdateCoroutines(fixedDelta);

        // Late Update Phase
        foreach (var script in scripts) script._lateUpdateDelegate?.Invoke();
        OnLateUpdate?.Invoke();
    }

    private void PerformPhysicsTick(World.World world, float fixedDelta)
    {
        Time.PhysicsTickCount++;
        Verity.Core.Physics.PhysicsManager.Step(fixedDelta, world, ProjectSettings);
        OnPhysicsTick?.Invoke();
    }

    public void TickRender()
    {
        OnRender?.Invoke();
    }
}
