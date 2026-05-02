using System.Diagnostics;
using Verity.Core.ECS;
using Verity.Core.World;

namespace Verity.Core.Engine;

public class GameLoop
{
    private const int MaxBrowserLogicTicksPerFrame = 1;
    private const int MaxBrowserPhysicsTicksPerFrame = 1;

    public Action? OnUpdate { get; set; }
    public Action? OnFixedUpdate { get; set; }
    public Action? OnPhysicsTick { get; set; }
    public Action? OnLateUpdate { get; set; }
    public Action? OnRender { get; set; }

    private float _logicAccumulator;
    private float _physicsAccumulator;

    public ProjectSettings ProjectSettings { get; set; } = ProjectSettings.Default;

    public int TickLogic(float deltaTime)
    {
        if (WorldLoader.PendingWorldName != null) return 0;

        var world = WorldManager.ActiveWorld;
        if (world == null) return 0;

        float scaledDelta = deltaTime * Time.TimeScale;

        int targetTPS = world.UseCustomSettings ? world.CustomTPS : ProjectSettings.TargetTPS;
        int targetPTPS = world.UseCustomSettings ? world.CustomPTPS : ProjectSettings.TargetPTPS;

        Time.TargetTPS = targetTPS;
        Time.TargetPTPS = targetPTPS;

        float logicFixedDelta = 1.0f / Math.Max(1, targetTPS);
        float physicsFixedDelta = 1.0f / Math.Max(1, targetPTPS);
        bool browserFastPath = OperatingSystem.IsBrowser();

        _logicAccumulator += scaledDelta;
        int logicTicksThisFrame = 0;
        while (_logicAccumulator >= logicFixedDelta)
        {
            PerformLogicTick(world, logicFixedDelta);
            _logicAccumulator -= logicFixedDelta;

            logicTicksThisFrame++;
            if (browserFastPath && logicTicksThisFrame >= MaxBrowserLogicTicksPerFrame)
            {
                _logicAccumulator = 0f;
                break;
            }
        }

        _physicsAccumulator += scaledDelta;
        int physicsTicksThisFrame = 0;
        while (_physicsAccumulator >= physicsFixedDelta)
        {
            PerformPhysicsTick(world, physicsFixedDelta);
            _physicsAccumulator -= physicsFixedDelta;

            physicsTicksThisFrame++;
            if (browserFastPath && physicsTicksThisFrame >= MaxBrowserPhysicsTicksPerFrame)
            {
                _physicsAccumulator = 0f;
                break;
            }
        }
        
        world.ProcessPendingDestroys();
        return logicTicksThisFrame;
    }

    private void PerformLogicTick(World.World world, float fixedDelta)
    {
        RuntimeProfiler.BeginLogicTick();
        long tickStart = Stopwatch.GetTimestamp();
        Verity.Input.Input.NewLogicTick();

        Time.DeltaTime = fixedDelta;
        Time.TotalTime += fixedDelta;
        Time.LogicTickCount++;

        Verity.Core.Animation.AnimationSystem.Update(fixedDelta);

        var scripts = world.GetActiveScripts();

        // Start Phase
        long phaseStart = Stopwatch.GetTimestamp();
        foreach (var script in scripts)
        {
            if (!script.HasAwoken)
            {
                InvokeScriptEvent("Awake", script, script._awakeDelegate);
                script.HasAwoken = true;
            }
        }
        RuntimeProfiler.RecordPhase("Awake", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        phaseStart = Stopwatch.GetTimestamp();
        foreach (var script in scripts)
        {
            if (!script.HasStarted)
            {
                InvokeScriptEvent("Start", script, script._startDelegate);
                script.HasStarted = true;
            }
        }
        RuntimeProfiler.RecordPhase("Start", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        // FixedUpdate
        phaseStart = Stopwatch.GetTimestamp();
        foreach (var script in scripts)
            InvokeScriptEvent("FixedUpdate", script, script._fixedUpdateDelegate);
        OnFixedUpdate?.Invoke();
        RuntimeProfiler.RecordPhase("FixedUpdate", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        // Update Phase
        phaseStart = Stopwatch.GetTimestamp();
        foreach (var script in scripts)
            InvokeScriptEvent("Update", script, script._updateDelegate);
        OnUpdate?.Invoke();
        RuntimeProfiler.RecordPhase("Update", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        phaseStart = Stopwatch.GetTimestamp();
        Verity.Core.ParticleSystem.UpdateAll(world, fixedDelta);
        RuntimeProfiler.RecordPhase("Particles", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        // Coroutine Update Phase
        phaseStart = Stopwatch.GetTimestamp();
        foreach (var script in scripts)
        {
            if (RuntimeProfiler.CaptureScriptDetails)
            {
                long coroutineStart = Stopwatch.GetTimestamp();
                script.UpdateCoroutines(fixedDelta);
                RuntimeProfiler.RecordScriptEvent("Coroutines", script, Stopwatch.GetElapsedTime(coroutineStart).TotalMilliseconds);
            }
            else
            {
                script.UpdateCoroutines(fixedDelta);
            }
        }
        RuntimeProfiler.RecordPhase("Coroutines", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);

        // Late Update Phase
        phaseStart = Stopwatch.GetTimestamp();
        foreach (var script in scripts)
            InvokeScriptEvent("LateUpdate", script, script._lateUpdateDelegate);
        OnLateUpdate?.Invoke();
        RuntimeProfiler.RecordPhase("LateUpdate", Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds);
        RuntimeProfiler.EndLogicTick(Stopwatch.GetElapsedTime(tickStart).TotalMilliseconds);
    }

    private void PerformPhysicsTick(World.World world, float fixedDelta)
    {
        long physicsStart = Stopwatch.GetTimestamp();
        Time.PhysicsTickCount++;
        Verity.Core.Physics.PhysicsManager.Step(fixedDelta, world, ProjectSettings);
        OnPhysicsTick?.Invoke();
        RuntimeProfiler.EndPhysicsTick(Stopwatch.GetElapsedTime(physicsStart).TotalMilliseconds);
    }

    public void TickRender()
    {
        OnRender?.Invoke();
    }

    private static void InvokeScriptEvent(string phase, Script script, Action? callback)
    {
        if (callback == null)
            return;

        if (!RuntimeProfiler.CaptureScriptDetails)
        {
            callback.Invoke();
            return;
        }

        long start = Stopwatch.GetTimestamp();
        callback.Invoke();
        RuntimeProfiler.RecordScriptEvent(phase, script, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
}
