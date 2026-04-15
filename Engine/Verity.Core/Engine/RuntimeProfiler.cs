using Verity.Core.ECS;

namespace Verity.Core.Engine;

public static class RuntimeProfiler
{
    private const int HistorySize = 180;
    private const int ScriptDetailSampleStride = 8;
    private const float SpikeThresholdMs = 20f;
    private static readonly object Sync = new();
    private static readonly MetricSeries LogicTickSeries = new(HistorySize);
    private static readonly MetricSeries PhysicsTickSeries = new(HistorySize);
    private static readonly Dictionary<string, MetricSeries> PhaseSeries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MetricSeries> ScriptSeries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, PhaseMetric> CurrentPhaseMetrics = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ScriptMetric> CurrentScriptMetrics = new(StringComparer.Ordinal);
    private static IReadOnlyList<RuntimePhaseMetricSnapshot> _lastPhaseMetrics = [];
    private static IReadOnlyList<RuntimeScriptMetricSnapshot> _lastScriptMetrics = [];
    private static int _logicTickCounter;
    private static bool _captureScriptDetailsNextTick;

    public static bool Enabled { get; set; }
    public static bool CaptureScriptDetails { get; private set; }

    public static void BeginLogicTick()
    {
        if (!Enabled)
            return;

        CaptureScriptDetails = _captureScriptDetailsNextTick || (++_logicTickCounter % ScriptDetailSampleStride == 0);
        _captureScriptDetailsNextTick = false;

        lock (Sync)
        {
            CurrentPhaseMetrics.Clear();
            CurrentScriptMetrics.Clear();
        }
    }

    public static void RecordScriptEvent(string phase, Script script, double milliseconds)
    {
        if (!Enabled)
            return;

        string phaseKey = phase;
        string scriptKey = $"{script.GetType().Name}.{phase}";

        lock (Sync)
        {
            if (!CurrentPhaseMetrics.TryGetValue(phaseKey, out var phaseMetric))
            {
                phaseMetric = new PhaseMetric(phaseKey);
                CurrentPhaseMetrics[phaseKey] = phaseMetric;
                EnsureSeries(PhaseSeries, phaseKey);
            }

            phaseMetric.TotalMs += (float)milliseconds;
            phaseMetric.CallCount++;

            if (!CurrentScriptMetrics.TryGetValue(scriptKey, out var scriptMetric))
            {
                scriptMetric = new ScriptMetric(scriptKey);
                CurrentScriptMetrics[scriptKey] = scriptMetric;
                EnsureSeries(ScriptSeries, scriptKey);
            }

            scriptMetric.TotalMs += (float)milliseconds;
            scriptMetric.CallCount++;
        }
    }

    public static void EndLogicTick(double milliseconds)
    {
        if (!Enabled)
            return;

        if (milliseconds >= SpikeThresholdMs)
            _captureScriptDetailsNextTick = true;

        lock (Sync)
        {
            LogicTickSeries.Push((float)milliseconds);

            foreach (var series in PhaseSeries)
            {
                CurrentPhaseMetrics.TryGetValue(series.Key, out var metric);
                series.Value.Push(metric?.TotalMs ?? 0f);
            }

            foreach (var series in ScriptSeries)
            {
                CurrentScriptMetrics.TryGetValue(series.Key, out var metric);
                series.Value.Push(metric?.TotalMs ?? 0f);
            }

            _lastPhaseMetrics = CurrentPhaseMetrics.Values
                .OrderByDescending(static metric => PhaseSeries[metric.Name].Average)
                .ThenByDescending(static metric => PhaseSeries[metric.Name].Max)
                .ThenByDescending(static metric => metric.TotalMs)
                .ThenBy(static metric => metric.Name, StringComparer.Ordinal)
                .Select(static metric => new RuntimePhaseMetricSnapshot(
                    metric.Name,
                    metric.TotalMs,
                    metric.CallCount,
                    PhaseSeries[metric.Name].Average,
                    PhaseSeries[metric.Name].CopyHistory()))
                .ToArray();

            _lastScriptMetrics = ScriptSeries
                .Select(static pair =>
                {
                    CurrentScriptMetrics.TryGetValue(pair.Key, out var metric);
                    float totalMs = metric?.TotalMs ?? 0f;
                    int callCount = metric?.CallCount ?? 0;
                    return new RuntimeScriptMetricSnapshot(
                        pair.Key,
                        totalMs,
                        callCount,
                        pair.Value.Average,
                        callCount == 0 ? 0f : totalMs / callCount);
                })
                .OrderByDescending(static metric => metric.AverageTotalMs)
                .ThenByDescending(static metric => metric.TotalMs)
                .ThenBy(static metric => metric.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public static void EndPhysicsTick(double milliseconds)
    {
        if (!Enabled)
            return;

        lock (Sync)
        {
            PhysicsTickSeries.Push((float)milliseconds);
        }
    }

    public static RuntimeProfilerSnapshot CaptureSnapshot()
    {
        lock (Sync)
        {
            return new RuntimeProfilerSnapshot(
                new RuntimeMetricSnapshot("Logic Tick", LogicTickSeries.Latest, LogicTickSeries.Average, LogicTickSeries.Max, LogicTickSeries.CopyHistory()),
                new RuntimeMetricSnapshot("Physics Tick", PhysicsTickSeries.Latest, PhysicsTickSeries.Average, PhysicsTickSeries.Max, PhysicsTickSeries.CopyHistory()),
                _lastPhaseMetrics,
                _lastScriptMetrics);
        }
    }

    public static void RecordPhase(string phase, double milliseconds)
    {
        if (!Enabled)
            return;

        lock (Sync)
        {
            if (!CurrentPhaseMetrics.TryGetValue(phase, out var phaseMetric))
            {
                phaseMetric = new PhaseMetric(phase);
                CurrentPhaseMetrics[phase] = phaseMetric;
                EnsureSeries(PhaseSeries, phase);
            }

            phaseMetric.TotalMs += (float)milliseconds;
            phaseMetric.CallCount++;
        }
    }

    private static MetricSeries EnsureSeries(Dictionary<string, MetricSeries> map, string name)
    {
        if (!map.TryGetValue(name, out var series))
        {
            series = new MetricSeries(HistorySize);
            map[name] = series;
        }

        return series;
    }

    private sealed class MetricSeries
    {
        private readonly float[] _values;
        private int _count;
        private int _nextIndex;
        private float _sum;
        private float _max;

        public MetricSeries(int capacity)
        {
            _values = new float[capacity];
        }

        public float Latest { get; private set; }
        public float Average => _count == 0 ? 0f : _sum / _count;
        public float Max => _max;

        public void Push(float value)
        {
            float replaced = _values[_nextIndex];
            Latest = value;

            if (_count == _values.Length)
            {
                _sum -= replaced;
            }
            else
            {
                _count++;
            }

            _values[_nextIndex] = value;
            _nextIndex = (_nextIndex + 1) % _values.Length;
            _sum += value;

            if (value >= _max)
            {
                _max = value;
                return;
            }

            if (_count == _values.Length && Math.Abs(replaced - _max) < 0.0001f)
            {
                _max = 0f;
                for (int i = 0; i < _values.Length; i++)
                    _max = Math.Max(_max, _values[i]);
            }
        }

        public float[] CopyHistory()
        {
            if (_count == 0)
                return [];

            float[] copy = new float[_count];
            int start = _count == _values.Length ? _nextIndex : 0;
            for (int i = 0; i < _count; i++)
                copy[i] = _values[(start + i) % _values.Length];

            return copy;
        }
    }

    private sealed class PhaseMetric(string name)
    {
        public string Name { get; } = name;
        public float TotalMs { get; set; }
        public int CallCount { get; set; }
    }

    private sealed class ScriptMetric(string name)
    {
        public string Name { get; } = name;
        public float TotalMs { get; set; }
        public int CallCount { get; set; }
    }
}

public sealed record RuntimeProfilerSnapshot(
    RuntimeMetricSnapshot LogicTick,
    RuntimeMetricSnapshot PhysicsTick,
    IReadOnlyList<RuntimePhaseMetricSnapshot> Phases,
    IReadOnlyList<RuntimeScriptMetricSnapshot> Scripts);

public sealed record RuntimeMetricSnapshot(
    string Name,
    float CurrentMs,
    float AverageMs,
    float MaxMs,
    IReadOnlyList<float> History);

public sealed record RuntimePhaseMetricSnapshot(
    string Name,
    float TotalMs,
    int CallCount,
    float AverageMs,
    IReadOnlyList<float> History);

public sealed record RuntimeScriptMetricSnapshot(
    string Name,
    float TotalMs,
    int CallCount,
    float AverageTotalMs,
    float AverageMsPerCall);
