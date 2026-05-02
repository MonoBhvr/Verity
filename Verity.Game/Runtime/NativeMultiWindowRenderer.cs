using System.Diagnostics;
using SDL2;
using Verity.Core.Engine;
using Verity.Core.World;
using Verity.Graphics;

namespace Verity.Game.Runtime;

internal sealed class NativeMultiWindowRenderer : IDisposable
{
    private sealed class WindowEntry : IDisposable
    {
        public WindowEntry(string key, VeritySdl2Window window)
        {
            Key = key;
            Window = window;
        }

        public string Key { get; }
        public VeritySdl2Window Window { get; }
        public string Group { get; set; } = string.Empty;
        public int Order { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool LockPosition { get; set; }
        public bool LockSize { get; set; }
        public bool Resizable { get; set; } = true;
        public bool Decorated { get; set; } = true;
        public bool Visible { get; set; }
        public bool CloseQuitsApplication { get; set; } = true;

        public void Dispose() => Window.Dispose();
    }

    private readonly GraphicsDevice _device;
    private readonly RenderPipeline _pipeline;
    private readonly ProjectSettings _projectSettings;
    private readonly Action<string, string> _writeRuntimeLog;
    private readonly Dictionary<string, WindowEntry> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<VeritySdl2Window> _windowPool = new();
    private readonly HashSet<string> _closedByUser = new(StringComparer.OrdinalIgnoreCase);
    private int _createdPoolCount;
    private long _suppressFocusRaiseUntilTicks;

    public NativeMultiWindowRenderer(GraphicsDevice device, RenderPipeline pipeline, ProjectSettings projectSettings, Action<string, string> writeRuntimeLog)
    {
        _device = device;
        _pipeline = pipeline;
        _projectSettings = projectSettings;
        _writeRuntimeLog = writeRuntimeLog;
        _device.Window.OnSdlEvent += HandleSdlEvent;
        if (_projectSettings.MultiWindowPrewarmMode == MultiWindowPrewarmMode.Startup)
            FillPool(_projectSettings.MultiWindowPrewarmCount);
    }

    public void Render(World world)
    {
        UpdateLazyPool();
        var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var createdThisFrame = new List<WindowEntry>();
        var renderList = new List<(WindowEntry Entry, RenderTexture Texture)>();

        foreach (var output in CameraSelection.EnumerateActiveOutputs(world)
                     .Where(static output => output.Target == CameraOutputTarget.Window && output.WindowVisible)
                     .OrderBy(static output => output.Order))
        {
            string key = output.ResolveOutputName();
            if (string.IsNullOrWhiteSpace(key) || _closedByUser.Contains(key))
                continue;

            if (!_pipeline.TryGetCameraOutputTexture(key, out var texture))
                continue;

            desiredKeys.Add(key);
            var entry = EnsureWindow(key, output, texture, createdThisFrame);
            renderList.Add((entry, texture));
        }

        foreach (var item in renderList)
            RenderToWindow(item.Entry.Window, item.Texture);

        foreach (string staleKey in _windows.Keys.Where(key => !desiredKeys.Contains(key)).ToArray())
        {
            ReleaseWindow(_windows[staleKey]);
            _windows.Remove(staleKey);
        }

        foreach (var entry in createdThisFrame)
        {
            entry.Window.SetTaskSwitcherVisible(true);
            entry.Window.Show();
            entry.Window.SetOpacity(1.0f);
            entry.Window.Raise();
            entry.Visible = true;
        }

        RestoreMainWindowContext();
    }

    private WindowEntry EnsureWindow(string key, CameraOutput output, RenderTexture texture, List<WindowEntry> createdThisFrame)
    {
        int width = Math.Max(1, (int)MathF.Round(output.WindowSize.X));
        int height = Math.Max(1, (int)MathF.Round(output.WindowSize.Y));
        if (width <= 1 || height <= 1)
        {
            width = texture.Width;
            height = texture.Height;
        }

        var screen = _device.Window.GetPrimaryDisplayBounds();
        int x = screen.X + (int)MathF.Round(output.WindowPosition.X);
        int y = screen.Y + (int)MathF.Round(output.WindowPosition.Y);

        if (!_windows.TryGetValue(key, out var entry))
        {
            string title = string.IsNullOrWhiteSpace(output.OutputName) ? output.Owner.Name : output.OutputName.Trim();
            var window = AcquireWindow(width, height, x, y);
            window.SetTitle(title);
            entry = new WindowEntry(key, window);
            _windows[key] = entry;
            createdThisFrame.Add(entry);
            _writeRuntimeLog("MultiWindow", $"Activated window '{title}' ({width}x{height})");
        }

        entry.Group = output.WindowGroup.Trim();
        entry.Order = output.Order;
        entry.X = x;
        entry.Y = y;
        entry.Width = width;
        entry.Height = height;
        entry.LockPosition = output.WindowLockPosition;
        entry.LockSize = output.WindowLockSize;
        entry.CloseQuitsApplication = output.WindowCloseQuitsApplication;

        bool resizable = !output.WindowLockSize;
        if (entry.Resizable != resizable)
        {
            entry.Window.SetResizable(resizable);
            entry.Resizable = resizable;
        }

        if (entry.Decorated != output.WindowDecorated)
        {
            entry.Window.SetBordered(output.WindowDecorated);
            entry.Decorated = output.WindowDecorated;
        }

        if (entry.LockSize || !entry.Visible)
        {
            var currentSize = ((int)entry.Window.GetWidth(), (int)entry.Window.GetHeight());
            if (currentSize.Item1 != width || currentSize.Item2 != height)
                entry.Window.SetSize(width, height);
        }

        if (entry.LockPosition || !entry.Visible)
        {
            var currentPosition = entry.Window.GetPosition();
            if (currentPosition.X != x || currentPosition.Y != y)
                entry.Window.SetPosition(x, y);
        }

        if (!entry.LockSize && !entry.Visible)
            entry.Window.SetSize(width, height);

        return entry;
    }

    private void UpdateLazyPool()
    {
        if (_projectSettings.MultiWindowPrewarmMode != MultiWindowPrewarmMode.LazyBackground)
            return;

        int target = Math.Clamp(_projectSettings.MultiWindowPrewarmCount, 0, 64);
        if (_createdPoolCount < target)
            FillPool(_createdPoolCount + 1);
    }

    private void FillPool(int targetCount)
    {
        targetCount = Math.Clamp(targetCount, 0, 64);
        while (_createdPoolCount < targetCount)
        {
            _windowPool.Push(CreateHiddenPoolWindow());
            _createdPoolCount++;
        }
    }

    private VeritySdl2Window CreateHiddenPoolWindow()
    {
        var screen = _device.Window.GetPrimaryDisplayBounds();
        var window = _device.Window.CreateAuxiliaryWindow("Verity Window", 64, 64, screen.X + screen.Width + 256, screen.Y, resizable: true, visible: true);
        window.SetTaskSwitcherVisible(false);
        window.SetOpacity(0.0f);
        return window;
    }

    private VeritySdl2Window AcquireWindow(int width, int height, int x, int y)
    {
        VeritySdl2Window window = _windowPool.Count > 0
            ? _windowPool.Pop()
            : _device.Window.CreateAuxiliaryWindow("Verity Window", width, height, x, y, resizable: true, visible: true);

        window.SetSize(width, height);
        window.SetPosition(x, y);
        window.SetResizable(true);
        window.SetTaskSwitcherVisible(true);
        window.SetOpacity(0.0f);
        return window;
    }

    private void ReleaseWindow(WindowEntry entry)
    {
        var screen = _device.Window.GetPrimaryDisplayBounds();
        entry.Window.SetOpacity(0.0f);
        entry.Window.SetTaskSwitcherVisible(false);
        entry.Window.SetPosition(screen.X + screen.Width + 256, screen.Y);
        if (!entry.Resizable)
        {
            entry.Window.SetResizable(true);
            entry.Resizable = true;
        }
        if (!entry.Decorated)
        {
            entry.Window.SetBordered(true);
            entry.Decorated = true;
        }
        entry.Visible = false;
        _windowPool.Push(entry.Window);
    }

    private void RenderToWindow(VeritySdl2Window window, RenderTexture texture)
    {
        window.GlMakeCurrent(_device.Window.GlContextHandle);
        int width = Math.Max(1, (int)window.GetWidth());
        int height = Math.Max(1, (int)window.GetHeight());
        _device.DisableScissorTest();
        _device.SetViewport(0, 0, (uint)width, (uint)height);
        _device.Clear(Verity.Core.Color.Black);
        _pipeline.BlitTexture(texture, null, width, height);
        window.SwapBuffers();
    }

    private void RestoreMainWindowContext()
    {
        _device.Window.GlMakeCurrent(_device.Window.GlContextHandle);
        _device.SetViewport(0, 0, _device.Width, _device.Height);
    }

    private void HandleSdlEvent(SDL.SDL_Event e)
    {
        if (e.type != SDL.SDL_EventType.SDL_WINDOWEVENT ||
            e.window.windowEvent != SDL.SDL_WindowEventID.SDL_WINDOWEVENT_CLOSE &&
            e.window.windowEvent != SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED)
        {
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        uint windowId = e.window.windowID;
        foreach (var pair in _windows.ToArray())
        {
            if (pair.Value.Window.WindowId != windowId)
                continue;

            if (e.window.windowEvent == SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED)
            {
                if (timestamp < _suppressFocusRaiseUntilTicks)
                    break;

                RaiseGroupOnce(pair.Value);
                break;
            }

            if (pair.Value.CloseQuitsApplication)
            {
                _device.Window.RequestClose();
            }
            else
            {
                _closedByUser.Add(pair.Key);
                ReleaseWindow(pair.Value);
                _windows.Remove(pair.Key);
                RestoreMainWindowContext();
            }
            break;
        }
    }

    private void RaiseGroupOnce(WindowEntry focused)
    {
        string group = focused.Group.Trim();
        var entries = string.IsNullOrWhiteSpace(group)
            ? _windows.Values.OrderBy(entry => entry.Order).ToList()
            : _windows.Values
                .Where(entry => string.Equals(entry.Group, group, StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Order)
                .ToList();

        if (entries.Count <= 1)
            return;

        _suppressFocusRaiseUntilTicks = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 4);
        VeritySdl2Window? previous = null;
        foreach (var entry in entries)
        {
            entry.Window.PlaceAfter(previous);
            previous = entry.Window;
        }

        RestoreMainWindowContext();
    }

    public void Dispose()
    {
        _device.Window.OnSdlEvent -= HandleSdlEvent;
        foreach (var entry in _windows.Values)
            entry.Dispose();
        foreach (var window in _windowPool)
            window.Dispose();

        _windows.Clear();
        _windowPool.Clear();
    }
}
