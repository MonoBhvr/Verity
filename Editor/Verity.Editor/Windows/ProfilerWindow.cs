using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using Verity.Core.Engine;
using Verity.Core.World;
using Verity.Editor.Profiling;

namespace Verity.Editor.Windows;

public class ProfilerWindow : EditorWindow
{
    private readonly EditorApp _app;
    private double _lastTime;
    private int _lastFrameCount;
    private int _lastLogicTickCount;
    private int _lastPhysicsTickCount;
    private float _fps;
    private float _tps;
    private float _ptps;
    private float _timer;
    private const float UpdateInterval = 1.0f;

    public ProfilerWindow(EditorApp app) : base(L10n.Tr("window_profiler"))
    {
        _app = app;
        IsOpen = false;
        _lastTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }

    public override void OnGui()
    {
        UpdateMetrics();

        EditorProfilerSnapshot editorSnapshot = _app.Profiler.CaptureSnapshot();
        RuntimeProfilerSnapshot runtimeSnapshot = RuntimeProfiler.CaptureSnapshot();

        DrawSummary();
        ImGui.Separator();

        DrawMetricGraph(
            L10n.Tr("profiler_graph_frame"),
            editorSnapshot.Frame.History,
            editorSnapshot.Frame.CurrentMs,
            16.67f,
            new Vector4(0.30f, 0.80f, 1.00f, 1.00f));

        DrawMetricGraph(
            L10n.Tr("profiler_graph_logic"),
            runtimeSnapshot.LogicTick.History,
            runtimeSnapshot.LogicTick.CurrentMs,
            runtimeSnapshot.LogicTick.AverageMs <= 0f ? null : runtimeSnapshot.LogicTick.AverageMs,
            new Vector4(0.45f, 0.95f, 0.50f, 1.00f));

        DrawMetricGraph(
            L10n.Tr("profiler_graph_physics"),
            runtimeSnapshot.PhysicsTick.History,
            runtimeSnapshot.PhysicsTick.CurrentMs,
            runtimeSnapshot.PhysicsTick.AverageMs <= 0f ? null : runtimeSnapshot.PhysicsTick.AverageMs,
            new Vector4(1.00f, 0.72f, 0.30f, 1.00f));

        DrawMetricGroup(L10n.Tr("profiler_frame_stages"), editorSnapshot.FrameStages, 56f);
        DrawMetricGroup(L10n.Tr("profiler_render_stages"), editorSnapshot.RenderStages, 56f);
        DrawMetricGroup(L10n.Tr("profiler_window_latency"), editorSnapshot.Windows, 56f);
        DrawRuntimePhases(runtimeSnapshot.Phases);
        DrawRuntimeScripts(runtimeSnapshot.Scripts);
    }

    public override void RefreshTitle()
    {
        Title = L10n.Tr("window_profiler");
    }

    private void DrawSummary()
    {
        ImGui.Text(L10n.Tr("label_fps", _fps.ToString("F3")));
        ImGui.Text(L10n.Tr("label_tps_actual", _tps.ToString("F3")));
        ImGui.Text(L10n.Tr("label_ptps_actual", _ptps.ToString("F3")));

        var world = WorldManager.ActiveWorld;
        int targetTps = world?.UseCustomSettings == true ? world.CustomTPS : _app.ProjectSettings.TargetTPS;
        int targetPtps = world?.UseCustomSettings == true ? world.CustomPTPS : _app.ProjectSettings.TargetPTPS;

        ImGui.Text(L10n.Tr("label_tps_setting", targetTps));
        ImGui.Text(L10n.Tr("label_ptps_setting", targetPtps));
    }

    private void DrawMetricGroup(string title, IReadOnlyList<EditorProfilerMetricSnapshot> metrics, float graphHeight)
    {
        if (metrics.Count == 0 || !ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (EditorProfilerMetricSnapshot metric in metrics)
        {
            DrawMetricGraph(metric.Name, metric.History, metric.CurrentMs, metric.AverageMs <= 0f ? null : metric.AverageMs, GetMetricColor(metric.Name), graphHeight);
            ImGui.Text($"{L10n.Tr("profiler_current")}: {metric.CurrentMs:F3} ms   {L10n.Tr("profiler_average")}: {metric.AverageMs:F3} ms   {L10n.Tr("profiler_max")}: {metric.MaxMs:F3} ms");
        }
    }

    private void DrawRuntimePhases(IReadOnlyList<RuntimePhaseMetricSnapshot> phases)
    {
        if (phases.Count == 0 || !ImGui.CollapsingHeader(L10n.Tr("profiler_script_phases"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        foreach (RuntimePhaseMetricSnapshot phase in phases)
        {
            DrawMetricGraph(phase.Name, phase.History, phase.TotalMs, null, GetMetricColor(phase.Name), 48f);
            ImGui.Text($"{L10n.Tr("profiler_total")}: {phase.TotalMs:F3} ms   {L10n.Tr("profiler_average")}: {phase.AverageMs:F3} ms   {L10n.Tr("profiler_calls")}: {phase.CallCount}");
        }
    }

    private void DrawRuntimeScripts(IReadOnlyList<RuntimeScriptMetricSnapshot> scripts)
    {
        if (!ImGui.CollapsingHeader(L10n.Tr("profiler_scripts"), ImGuiTreeNodeFlags.DefaultOpen))
            return;

        int displayed = 0;
        foreach (RuntimeScriptMetricSnapshot script in scripts)
        {
            ImGui.Text($"{script.Name}  |  {L10n.Tr("profiler_total")}: {script.TotalMs:F3} ms  |  {L10n.Tr("profiler_average")}: {script.AverageTotalMs:F3} ms  |  {L10n.Tr("profiler_calls")}: {script.CallCount}  |  {L10n.Tr("profiler_average")}{L10n.Tr("profiler_per_call")}: {script.AverageMsPerCall:F3} ms");
            displayed++;
            if (displayed >= 12)
                break;
        }

        if (displayed == 0)
            ImGui.TextDisabled(L10n.Tr("profiler_no_script_samples"));
    }

    private static Vector4 GetMetricColor(string name)
    {
        int hash = StringComparer.Ordinal.GetHashCode(name);
        float hue = Math.Abs(hash % 360) / 360f;
        return HsvToRgb(hue, 0.65f, 0.95f);
    }

    private static Vector4 HsvToRgb(float h, float s, float v)
    {
        float i = MathF.Floor(h * 6f);
        float f = h * 6f - i;
        float p = v * (1f - s);
        float q = v * (1f - f * s);
        float t = v * (1f - (1f - f) * s);

        return ((int)i % 6) switch
        {
            0 => new Vector4(v, t, p, 1f),
            1 => new Vector4(q, v, p, 1f),
            2 => new Vector4(p, v, t, 1f),
            3 => new Vector4(p, q, v, 1f),
            4 => new Vector4(t, p, v, 1f),
            _ => new Vector4(v, p, q, 1f)
        };
    }

    private static void DrawMetricGraph(string label, IReadOnlyList<float> history, float currentMs, float? referenceMs, Vector4 color, float height = 84f)
    {
        ImGui.Text($"{label}  ({currentMs:F3} ms)");
        Vector2 size = new(Math.Max(120f, ImGui.GetContentRegionAvail().X), height);
        Vector2 origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##graph_{label}", size);

        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Vector2 min = origin;
        Vector2 max = origin + size;
        draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0.08f, 0.09f, 0.11f, 1f)), 6f);
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(0.22f, 0.24f, 0.28f, 1f)), 6f);

        float maxValue = Math.Max(1f, currentMs);
        if (referenceMs.HasValue)
            maxValue = Math.Max(maxValue, referenceMs.Value);

        foreach (float value in history)
            maxValue = Math.Max(maxValue, value);

        for (int i = 1; i <= 3; i++)
        {
            float y = max.Y - (size.Y * i / 4f);
            draw.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.06f)));
        }

        if (referenceMs.HasValue && referenceMs.Value > 0.001f)
        {
            float normalized = Math.Clamp(referenceMs.Value / maxValue, 0f, 1f);
            float y = max.Y - normalized * size.Y;
            draw.AddLine(new Vector2(min.X, y), new Vector2(max.X, y), ImGui.GetColorU32(new Vector4(1f, 0.95f, 0.35f, 0.85f)));
        }

        if (history.Count < 2)
            return;

        Vector2[] points = new Vector2[history.Count];
        for (int i = 0; i < history.Count; i++)
        {
            float x = min.X + size.X * i / (history.Count - 1f);
            float normalized = Math.Clamp(history[i] / maxValue, 0f, 1f);
            float y = max.Y - normalized * size.Y;
            points[i] = new Vector2(x, y);
        }

        uint lineColor = ImGui.GetColorU32(color);
        for (int i = 1; i < points.Length; i++)
            draw.AddLine(points[i - 1], points[i], lineColor, 2f);
    }

    private void UpdateMetrics()
    {
        double currentTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        float deltaTime = (float)(currentTime - _lastTime);
        _timer += deltaTime;

        if (_timer >= UpdateInterval)
        {
            int frameDelta = Time.FrameCount - _lastFrameCount;
            int logicDelta = Time.LogicTickCount - _lastLogicTickCount;
            int physicsDelta = Time.PhysicsTickCount - _lastPhysicsTickCount;

            _fps = frameDelta / _timer;
            _tps = logicDelta / _timer;
            _ptps = physicsDelta / _timer;

            _lastFrameCount = Time.FrameCount;
            _lastLogicTickCount = Time.LogicTickCount;
            _lastPhysicsTickCount = Time.PhysicsTickCount;
            _timer = 0;
        }

        _lastTime = currentTime;
    }

}
