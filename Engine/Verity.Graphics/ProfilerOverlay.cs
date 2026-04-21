#if DEBUG
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Verity.Core.Engine;
using Verity.Core.World;

namespace Verity.Graphics;

public sealed class ProfilerOverlay
{
    private const double FpsSampleSeconds = 0.5;
    private const double MemorySampleSeconds = 0.5;
    private const float Margin = 12f;
    private const float Padding = 10f;
    private const float FontSize = 16f;
    private const float LineHeight = 20f;
    private const int MetricLineCount = 6;

    private readonly Stopwatch _sampleTimer = Stopwatch.StartNew();
    private readonly Stopwatch _memoryTimer = Stopwatch.StartNew();
    private readonly StringBuilder _textBuilder = new();
    private int _framesSinceLastSample;
    private int _lastWorldStateVersion = int.MinValue;
    private float _fps;
    private float _renderMs;
    private float _memoryMb;
    private int _entityCount;

    public static bool ShowProfiler { get; set; }

    public void TickFrame()
    {
        if (!ShowProfiler)
            return;

        _framesSinceLastSample++;

        double elapsedSeconds = _sampleTimer.Elapsed.TotalSeconds;
        if (elapsedSeconds < FpsSampleSeconds)
            return;

        _fps = (float)(_framesSinceLastSample / elapsedSeconds);
        _framesSinceLastSample = 0;
        _sampleTimer.Restart();
    }

    public void SetRenderTime(double milliseconds)
    {
        if (!ShowProfiler)
            return;

        _renderMs = (float)milliseconds;
    }

    public void Render(RenderPipeline pipeline, World? world, int viewportWidth, int viewportHeight)
    {
        if (!ShowProfiler || viewportWidth <= 0 || viewportHeight <= 0 || DefaultSprites.Square == null)
            return;

        UpdateWorldStats(world);
        UpdateMemoryUsage();

        RuntimeProfilerSnapshot snapshot = RuntimeProfiler.CaptureSnapshot();
        string text = BuildText(snapshot);

        float panelWidth = 310f;
        float panelHeight = Padding * 2f + LineHeight * (MetricLineCount + 1);
        Matrix4x4 projection = Matrix4x4.CreateOrthographicOffCenter(0f, viewportWidth, viewportHeight, 0f, -1f, 1f);
        Matrix4x4 view = Matrix4x4.Identity;
        Matrix4x4 backgroundModel = Matrix4x4.CreateScale(panelWidth, panelHeight, 1f) * Matrix4x4.CreateTranslation(Margin, Margin, 0f);

        pipeline.DrawTile(
            DefaultSprites.Square,
            backgroundModel,
            new Color(0.05f, 0.07f, 0.10f, 0.82f),
            projection,
            view,
            null);

        pipeline.DrawText(
            new TextRenderOptions(
                text,
                new System.Numerics.Vector2(Margin + Padding, Margin + Padding),
                new System.Numerics.Vector2(panelWidth - Padding * 2f, panelHeight - Padding * 2f),
                new Color(0.95f, 0.97f, 1.0f, 1.0f),
                FontSize,
                false,
                true,
                UiRenderer.DefaultFontPath,
                UiRenderer.DefaultFontFamily,
                TextHorizontalAlignment.Left,
                TextVerticalAlignment.Top),
            projection,
            view,
            null);
    }

    private void UpdateWorldStats(World? world)
    {
        if (world == null)
        {
            _entityCount = 0;
            _lastWorldStateVersion = int.MinValue;
            return;
        }

        if (_lastWorldStateVersion == world.StateVersion)
            return;

        _entityCount = world.GetAllEntities().Count;
        _lastWorldStateVersion = world.StateVersion;
    }

    private void UpdateMemoryUsage()
    {
        if (_memoryTimer.Elapsed.TotalSeconds < MemorySampleSeconds)
            return;

        _memoryMb = GC.GetTotalMemory(false) / (1024f * 1024f);
        _memoryTimer.Restart();
    }

    private string BuildText(RuntimeProfilerSnapshot snapshot)
    {
        _textBuilder.Clear();
        _textBuilder.AppendLine("Profiler");
        _textBuilder.Append("FPS: ").Append(_fps.ToString("F1")).AppendLine();
        _textBuilder.Append("Logic: ").Append(snapshot.LogicTick.CurrentMs.ToString("F3")).AppendLine(" ms");
        _textBuilder.Append("Physics: ").Append(snapshot.PhysicsTick.CurrentMs.ToString("F3")).AppendLine(" ms");
        _textBuilder.Append("Render: ").Append(_renderMs.ToString("F3")).AppendLine(" ms");
        _textBuilder.Append("Memory: ").Append(_memoryMb.ToString("F1")).AppendLine(" MB");
        _textBuilder.Append("Entities: ").Append(_entityCount);
        return _textBuilder.ToString();
    }
}
#else
using Verity.Core.World;

namespace Verity.Graphics;

public sealed class ProfilerOverlay
{
    public static bool ShowProfiler { get; set; }

    public void TickFrame()
    {
    }

    public void SetRenderTime(double milliseconds)
    {
    }

    public void Render(RenderPipeline pipeline, World? world, int viewportWidth, int viewportHeight)
    {
    }
}
#endif
