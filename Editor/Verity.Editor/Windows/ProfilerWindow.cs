using System.Diagnostics;
using Hexa.NET.ImGui;
using Verity.Core.Engine;
using Verity.Core.World;

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

    public ProfilerWindow(EditorApp app) : base("Profiler")
    {
        _app = app;
        IsOpen = false; // Default disabled as requested
        _lastTime = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    }

    public override void OnGui()
    {
        UpdateMetrics();

        ImGui.Text($"FPS: {_fps:F3}");
        ImGui.Text($"TPS (Actual): {_tps:F3}");
        ImGui.Text($"PTPS (Actual): {_ptps:F3}");
        
        ImGui.Separator();

        var world = WorldManager.ActiveWorld;
        int targetTPS = world?.UseCustomSettings == true ? world.CustomTPS : _app.ProjectSettings.TargetTPS;
        int targetPTPS = world?.UseCustomSettings == true ? world.CustomPTPS : _app.ProjectSettings.TargetPTPS;

        ImGui.Text($"TPS (Setting): {targetTPS}");
        ImGui.Text($"FTPS (Setting): {targetPTPS}");
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
