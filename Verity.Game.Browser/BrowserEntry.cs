using System.Runtime.Versioning;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using Verity.Input;
using InputState = Verity.Input.Input;

namespace Verity.Game.Browser;

[SupportedOSPlatform("browser")]
public static partial class BrowserEntry
{
    private static Verity.Game.Runtime.BrowserRuntimeSession? _runtimeSession;
    private static string? _lastError;
    private static string? _lastInputEvent;
    private static readonly Queue<string> _recentLogs = new();
    private static readonly object _logLock = new();
    private static bool _browserLogHooked;

    [JSExport]
    public static void InitializeRuntime()
    {
        try
        {
            _lastError = null;
            HookBrowserLogs();
            _runtimeSession?.Dispose();
            _runtimeSession = Verity.Game.Runtime.RuntimeBootstrap.CreateBrowserSession(new BrowserRuntimeHost(false));
        }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            throw new InvalidOperationException(_lastError, ex);
        }
    }

    [JSExport]
    public static void TickFrame()
    {
        try
        {
            _runtimeSession?.TickFrame();
        }
        catch (Exception ex)
        {
            _lastError = ex.ToString();
            throw new InvalidOperationException(_lastError, ex);
        }
    }

    [JSExport]
    public static bool ShouldClose()
    {
        return _runtimeSession?.ShouldClose ?? false;
    }

    [JSExport]
    public static string GetDebugState()
    {
        if (!string.IsNullOrWhiteSpace(_lastError))
            return $"error={_lastError}";

        var builder = new StringBuilder();
        builder.Append(_runtimeSession?.GetDebugState() ?? "session=<null>");

        string[] logs;
        lock (_logLock)
            logs = _recentLogs.ToArray();

        if (logs.Length > 0)
        {
            builder.Append("\n\nLogs:");
            foreach (string log in logs)
            {
                builder.Append('\n');
                builder.Append(log);
            }
        }

        return builder.ToString();
    }

    [JSExport]
    public static string GetSceneDebugDump()
    {
        if (!string.IsNullOrWhiteSpace(_lastError))
            return $"<startup error>\n{_lastError}";

        return _runtimeSession?.GetSceneDebugDump() ?? "<session=<null>>";
    }

    [JSExport]
    public static bool GetIntegerScaling()
    {
        var camera = Verity.Graphics.Camera.Main;
        return camera?.IntegerScaling ?? false;
    }

    [JSExport]
    public static void ResetInputState()
    {
        InputState.Reset();
        InputState.Enabled = true;
    }

    [JSExport]
    public static void OnKeyDown(string code)
    {
        _lastInputEvent = $"keyDown:{code}";
        if (TryMapDomCode(code, out KeyCode keyCode))
            InputState.ProcessEvent(InputEvent.KeyDown(keyCode));
    }

    [JSExport]
    public static void OnKeyUp(string code)
    {
        _lastInputEvent = $"keyUp:{code}";
        if (TryMapDomCode(code, out KeyCode keyCode))
            InputState.ProcessEvent(InputEvent.KeyUp(keyCode));
    }

    [JSExport]
    public static void OnMouseMove(float x, float y)
    {
        _lastInputEvent = $"mouseMove:{x:0.##},{y:0.##}";
        InputState.ProcessEvent(InputEvent.MouseMove(x, y));
    }

    [JSExport]
    public static void OnMouseDown(int button)
    {
        _lastInputEvent = $"mouseDown:{button}";
        if (TryMapMouseButton(button, out MouseButton mappedButton))
            InputState.ProcessEvent(InputEvent.MouseButtonDown(mappedButton));
    }

    [JSExport]
    public static void OnMouseUp(int button)
    {
        _lastInputEvent = $"mouseUp:{button}";
        if (TryMapMouseButton(button, out MouseButton mappedButton))
            InputState.ProcessEvent(InputEvent.MouseButtonUp(mappedButton));
    }

    [JSExport]
    public static void OnMouseWheel(float delta)
    {
        _lastInputEvent = $"mouseWheel:{delta:0.##}";
        InputState.ProcessEvent(InputEvent.MouseWheel(delta));
    }

    private static void HookBrowserLogs()
    {
        if (_browserLogHooked)
            return;

        _browserLogHooked = true;
        Verity.Core.Debug.OnLog += (message, level) =>
        {
            string line = $"[{level}] {message}";
            lock (_logLock)
            {
                _recentLogs.Enqueue(line);
                while (_recentLogs.Count > 8)
                    _recentLogs.Dequeue();
            }
        };
    }

    private static bool TryMapMouseButton(int button, out MouseButton mappedButton)
    {
        mappedButton = button switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            3 => MouseButton.X1,
            4 => MouseButton.X2,
            _ => default
        };

        return button is >= 0 and <= 4;
    }

    private static bool TryMapDomCode(string code, out KeyCode keyCode)
    {
        keyCode = code switch
        {
            "KeyA" => KeyCode.A,
            "KeyB" => KeyCode.B,
            "KeyC" => KeyCode.C,
            "KeyD" => KeyCode.D,
            "KeyE" => KeyCode.E,
            "KeyF" => KeyCode.F,
            "KeyG" => KeyCode.G,
            "KeyH" => KeyCode.H,
            "KeyI" => KeyCode.I,
            "KeyJ" => KeyCode.J,
            "KeyK" => KeyCode.K,
            "KeyL" => KeyCode.L,
            "KeyM" => KeyCode.M,
            "KeyN" => KeyCode.N,
            "KeyO" => KeyCode.O,
            "KeyP" => KeyCode.P,
            "KeyQ" => KeyCode.Q,
            "KeyR" => KeyCode.R,
            "KeyS" => KeyCode.S,
            "KeyT" => KeyCode.T,
            "KeyU" => KeyCode.U,
            "KeyV" => KeyCode.V,
            "KeyW" => KeyCode.W,
            "KeyX" => KeyCode.X,
            "KeyY" => KeyCode.Y,
            "KeyZ" => KeyCode.Z,
            "Digit0" => KeyCode.Alpha0,
            "Digit1" => KeyCode.Alpha1,
            "Digit2" => KeyCode.Alpha2,
            "Digit3" => KeyCode.Alpha3,
            "Digit4" => KeyCode.Alpha4,
            "Digit5" => KeyCode.Alpha5,
            "Digit6" => KeyCode.Alpha6,
            "Digit7" => KeyCode.Alpha7,
            "Digit8" => KeyCode.Alpha8,
            "Digit9" => KeyCode.Alpha9,
            "F1" => KeyCode.F1,
            "F2" => KeyCode.F2,
            "F3" => KeyCode.F3,
            "F4" => KeyCode.F4,
            "F5" => KeyCode.F5,
            "F6" => KeyCode.F6,
            "F7" => KeyCode.F7,
            "F8" => KeyCode.F8,
            "F9" => KeyCode.F9,
            "F10" => KeyCode.F10,
            "F11" => KeyCode.F11,
            "F12" => KeyCode.F12,
            "Space" => KeyCode.Space,
            "Enter" => KeyCode.Return,
            "NumpadEnter" => KeyCode.Return,
            "Escape" => KeyCode.Escape,
            "Backspace" => KeyCode.Backspace,
            "Tab" => KeyCode.Tab,
            "Delete" => KeyCode.Delete,
            "ArrowUp" => KeyCode.UpArrow,
            "ArrowDown" => KeyCode.DownArrow,
            "ArrowLeft" => KeyCode.LeftArrow,
            "ArrowRight" => KeyCode.RightArrow,
            "ShiftLeft" => KeyCode.LeftShift,
            "ShiftRight" => KeyCode.RightShift,
            "ControlLeft" => KeyCode.LeftCtrl,
            "ControlRight" => KeyCode.RightCtrl,
            "AltLeft" => KeyCode.LeftAlt,
            "AltRight" => KeyCode.RightAlt,
            _ => KeyCode.Unknown
        };

        return keyCode != KeyCode.Unknown;
    }
}
