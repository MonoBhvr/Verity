namespace Verity.Editor.Profiling;

public sealed class EditorProfiler
{
    private const int HistorySize = 180;
    private readonly object _sync = new();
    private readonly Dictionary<string, MetricSeries> _windowSeries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricSeries> _renderSeries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricSeries> _frameStageSeries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _currentWindowSamples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _currentRenderSamples = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _currentFrameStageSamples = new(StringComparer.Ordinal);
    private readonly MetricSeries _frameSeries = new(HistorySize);

    public bool IsCollectingFrame { get; private set; }

    public void BeginFrame(bool enabled)
    {
        lock (_sync)
        {
            IsCollectingFrame = enabled;
            _currentWindowSamples.Clear();
            _currentRenderSamples.Clear();
            _currentFrameStageSamples.Clear();
        }
    }

    public void RecordWindow(string name, double milliseconds)
    {
        if (!IsCollectingFrame)
            return;

        lock (_sync)
        {
            _currentWindowSamples[name] = (float)milliseconds;
            EnsureSeries(_windowSeries, name);
        }
    }

    public void RecordRenderStage(string name, double milliseconds)
    {
        if (!IsCollectingFrame)
            return;

        lock (_sync)
        {
            _currentRenderSamples[name] = (float)milliseconds;
            EnsureSeries(_renderSeries, name);
        }
    }

    public void RecordFrameStage(string name, double milliseconds)
    {
        if (!IsCollectingFrame)
            return;

        lock (_sync)
        {
            _currentFrameStageSamples[name] = (float)milliseconds;
            EnsureSeries(_frameStageSeries, name);
        }
    }

    public void EndFrame(double frameMilliseconds)
    {
        if (!IsCollectingFrame)
            return;

        lock (_sync)
        {
            _frameSeries.Push((float)frameMilliseconds);
            PushFrameSamples(_windowSeries, _currentWindowSamples);
            PushFrameSamples(_renderSeries, _currentRenderSamples);
            PushFrameSamples(_frameStageSeries, _currentFrameStageSamples);
        }
    }

    public EditorProfilerSnapshot CaptureSnapshot()
    {
        lock (_sync)
        {
            return new EditorProfilerSnapshot(
                BuildMetricSnapshot("Frame", _frameSeries),
                BuildMetricList(_frameStageSeries),
                BuildMetricList(_renderSeries),
                BuildMetricList(_windowSeries));
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

    private static void PushFrameSamples(Dictionary<string, MetricSeries> seriesMap, Dictionary<string, float> currentSamples)
    {
        foreach (var pair in seriesMap)
        {
            currentSamples.TryGetValue(pair.Key, out float value);
            pair.Value.Push(value);
        }
    }

    private static EditorProfilerMetricSnapshot BuildMetricSnapshot(string name, MetricSeries series)
    {
        return new EditorProfilerMetricSnapshot(name, series.Latest, series.Average, series.Max, series.CopyHistory());
    }

    private static IReadOnlyList<EditorProfilerMetricSnapshot> BuildMetricList(Dictionary<string, MetricSeries> seriesMap)
    {
        return seriesMap
            .Select(static pair => BuildMetricSnapshot(pair.Key, pair.Value))
            .OrderByDescending(static metric => metric.AverageMs)
            .ThenByDescending(static metric => metric.MaxMs)
            .ThenByDescending(static metric => metric.CurrentMs)
            .ThenBy(static metric => metric.Name, StringComparer.Ordinal)
            .ToArray();
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
}

public sealed record EditorProfilerSnapshot(
    EditorProfilerMetricSnapshot Frame,
    IReadOnlyList<EditorProfilerMetricSnapshot> FrameStages,
    IReadOnlyList<EditorProfilerMetricSnapshot> RenderStages,
    IReadOnlyList<EditorProfilerMetricSnapshot> Windows);

public sealed record EditorProfilerMetricSnapshot(
    string Name,
    float CurrentMs,
    float AverageMs,
    float MaxMs,
    IReadOnlyList<float> History);
