namespace Verity.Game.Runtime;

public sealed class BrowserRuntimeSession : IDisposable
{
    private readonly RuntimeApp _runtimeApp;

    internal BrowserRuntimeSession(RuntimeApp runtimeApp)
    {
        _runtimeApp = runtimeApp;
    }

    public bool ShouldClose => _runtimeApp.ShouldClose;

    public void TickFrame()
    {
        _runtimeApp.TickFrame();
    }

    public string GetDebugState()
    {
        return _runtimeApp.GetDebugState();
    }

    public string GetSceneDebugDump()
    {
        return _runtimeApp.GetSceneDebugDump();
    }

    public void Dispose()
    {
        _runtimeApp.Dispose();
    }
}
