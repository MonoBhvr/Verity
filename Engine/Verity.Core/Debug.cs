namespace Verity.Core;

public enum LogLevel { Info, Warning, Error }

public static class Debug
{
    public static event Action<string, LogLevel>? OnLog;

    public static void Log(string message) => OnLog?.Invoke(message, LogLevel.Info);
    public static void LogWarning(string message) => OnLog?.Invoke(message, LogLevel.Warning);
    public static void LogError(string message) => OnLog?.Invoke(message, LogLevel.Error);

    public struct LineCommand
    {
        public System.Numerics.Vector2 Start;
        public System.Numerics.Vector2 End;
        public System.Numerics.Vector4 Color;
        public float Thickness;
    }

    private static readonly List<LineCommand> _lines = [];

    public static IReadOnlyList<LineCommand> Lines => _lines;

    public static void DrawLine(System.Numerics.Vector2 start, System.Numerics.Vector2 end, System.Numerics.Vector4? color = null, float thickness = 0.02f)
    {
        _lines.Add(new LineCommand
        {
            Start = start,
            End = end,
            Color = color ?? new System.Numerics.Vector4(0, 1, 0, 1),
            Thickness = thickness
        });
    }

    public static void DrawBox(System.Numerics.Vector2 center, System.Numerics.Vector2 size, System.Numerics.Vector4? color = null, float thickness = 0.02f)
    {
        var c = color ?? new System.Numerics.Vector4(0, 1, 0, 1);
        var half = size * 0.5f;
        var tl = center + new System.Numerics.Vector2(-half.X, half.Y);
        var tr = center + new System.Numerics.Vector2(half.X, half.Y);
        var br = center + new System.Numerics.Vector2(half.X, -half.Y);
        var bl = center + new System.Numerics.Vector2(-half.X, -half.Y);

        DrawLine(tl, tr, c, thickness);
        DrawLine(tr, br, c, thickness);
        DrawLine(br, bl, c, thickness);
        DrawLine(bl, tl, c, thickness);
    }

    public static void ClearDrawCommands()
    {
        _lines.Clear();
    }
}
