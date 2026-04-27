namespace Verity.Core.Engine;

/// <summary>
/// Static time provider for the game loop.
/// </summary>
public static class Time
{
    /// <summary>
    /// Current target Ticks Per Second for logic.
    /// </summary>
    public static int TargetTPS { get; internal set; } = 60;

    /// <summary>
    /// Current target Physics Ticks Per Second.
    /// </summary>
    public static int TargetPTPS { get; internal set; } = 50;

    /// <summary>
    /// Time in seconds since the last frame.
    /// </summary>
    public static float DeltaTime { get; internal set; }

    /// <summary>
    /// Fixed timestep interval in seconds. Default: 1/60.
    /// </summary>
    public static float FixedDeltaTime { get; set; } = 1f / 60f;

    /// <summary>
    /// Total elapsed time in seconds since the engine started.
    /// </summary>
    public static float TotalTime { get; internal set; }

    /// <summary>
    /// Time scale factor. 1.0 = normal speed, 0.0 = paused.
    /// </summary>
    public static float TimeScale { get; set; } = 1.0f;

    /// <summary>
    /// Total number of frames rendered since the engine started.
    /// </summary>
    public static int FrameCount { get; internal set; }

    /// <summary>
    /// Total number of logic ticks since the engine started.
    /// </summary>
    public static int LogicTickCount { get; internal set; }

    /// <summary>
    /// Total number of physics ticks since the engine started.
    /// </summary>
    public static int PhysicsTickCount { get; internal set; }

    /// <summary>
    /// Advances the rendered frame counter.
    /// </summary>
    public static void AdvanceFrame()
    {
        FrameCount++;
    }

    /// <summary>
    /// Resets all time values to their defaults.
    /// </summary>
    public static void Reset()
    {
        DeltaTime = 0f;
        TotalTime = 0f;
        TimeScale = 1.0f;
        FrameCount = 0;
        LogicTickCount = 0;
        PhysicsTickCount = 0;
    }
}
