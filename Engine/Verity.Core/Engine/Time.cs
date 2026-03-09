namespace Verity.Core.Engine;

/// <summary>
/// Static time provider for the game loop.
/// </summary>
public static class Time
{
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
    /// Resets all time values to their defaults.
    /// </summary>
    public static void Reset()
    {
        DeltaTime = 0f;
        TotalTime = 0f;
        TimeScale = 1.0f;
        FrameCount = 0;
    }
}
