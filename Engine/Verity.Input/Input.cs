using System.Numerics;
using SDL2;

namespace Verity.Input;

public static class Input
{
    // 실시간 물리 상태 (GetKey용)
    private static readonly HashSet<KeyCode> _keysDown = [];
    private static readonly HashSet<MouseButton> _buttonsDown = [];

    // 이벤트 버퍼 (이전 틱 이후 발생한 모든 이벤트 저장)
    private static readonly HashSet<KeyCode> _pressedBuffer = [];
    private static readonly HashSet<KeyCode> _releasedBuffer = [];
    private static readonly HashSet<MouseButton> _mousePressedBuffer = [];
    private static readonly HashSet<MouseButton> _mouseReleasedBuffer = []; 

    // 현재 틱의 확정된 상태 (GetKeyDown/Up용)
    private static readonly HashSet<KeyCode> _pressedThisTick = [];
    private static readonly HashSet<KeyCode> _releasedThisTick = [];
    private static readonly HashSet<MouseButton> _mousePressedThisTick = [];
    private static readonly HashSet<MouseButton> _mouseReleasedThisTick = [];

    private static Vector2 _mousePosition;
    private static Vector2 _mouseDelta;
    private static Vector2 _previousMousePosition;
    private static float _scrollDeltaBuffer;
    private static float _scrollDeltaThisTick;

    private static bool _enabled = true;
    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled && !value)
            {
                _keysDown.Clear();
                _buttonsDown.Clear();
                ClearBuffers();
            }
            _enabled = value;
        }
    }

    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseDelta => _mouseDelta;
    public static float ScrollDelta => _scrollDeltaThisTick;
    public static KeyCode AnyKey => _enabled && _keysDown.Count > 0 ? _keysDown.First() : (KeyCode)(-1);
    public static MouseButton AnyMouseButton => _enabled && _buttonsDown.Count > 0 ? _buttonsDown.First() : (MouseButton)(-1);
    public static bool AnyKeyDown => _enabled && _pressedThisTick.Count > 0;
    public static bool GetKey(KeyCode key) => _enabled && _keysDown.Contains(key);
    public static bool GetKeyDown(KeyCode key) => _enabled && _pressedThisTick.Contains(key);
    public static bool GetKeyUp(KeyCode key) => _enabled && _releasedThisTick.Contains(key);

    public static bool GetKey(Filter? filter)
    {
        if (filter == null || !_enabled) return false;
        
        foreach (var k in _keysDown) if (filter.Check(k)) return true;
        foreach (var b in _buttonsDown) if (filter.Check(b)) return true;
        return false;
    }

    public static bool GetKeyDown(Filter? filter)
    {
        if (filter == null || !_enabled) return false;
        
        foreach (var k in _pressedThisTick) if (filter.Check(k)) return true;
        foreach (var b in _mousePressedThisTick) if (filter.Check(b)) return true;
        return false;
    }

    public static bool GetKeyUp(Filter? filter)
    {
        if (filter == null || !_enabled) return false;
        
        foreach (var k in _releasedThisTick) if (filter.Check(k)) return true;
        foreach (var b in _mouseReleasedThisTick) if (filter.Check(b)) return true;
        return false;
    }

    public static bool GetKey(string filterName) => GetKey(Filter.Get(filterName));
    public static bool GetKeyDown(string filterName) => GetKeyDown(Filter.Get(filterName));
    public static bool GetKeyUp(string filterName) => GetKeyUp(Filter.Get(filterName));

    public static bool GetMouseButton(MouseButton button) => _enabled && _buttonsDown.Contains(button);
    public static bool GetMouseButtonDown(MouseButton button) => _enabled && _mousePressedThisTick.Contains(button);
    public static bool GetMouseButtonUp(MouseButton button) => _enabled && _mouseReleasedThisTick.Contains(button);

    public static bool GetMouseButton(Filter? filter)
    {
        if (filter == null || !_enabled) return false;
        if (filter.Mode == FilterMode.Whitelist)
        {
            foreach (var btn in filter.GetValues<MouseButton>())
                if (GetMouseButton(btn)) return true;
            return false;
        }
        else
        {
            foreach (var btn in _buttonsDown)
                if (!filter.Check(btn)) return true;
            return false;
        }
    }

    public static bool GetMouseButtonDown(Filter? filter)
    {
        if (filter == null || !_enabled) return false;
        if (filter.Mode == FilterMode.Whitelist)
        {
            foreach (var btn in filter.GetValues<MouseButton>())
                if (GetMouseButtonDown(btn)) return true;
            return false;
        }
        else
        {
            foreach (var btn in _mousePressedThisTick)
                if (!filter.Check(btn)) return true;
            return false;
        }
    }

    public static bool GetMouseButtonUp(Filter? filter)
    {
        if (filter == null || !_enabled) return false;
        if (filter.Mode == FilterMode.Whitelist)
        {
            foreach (var btn in filter.GetValues<MouseButton>())
                if (GetMouseButtonUp(btn)) return true;
            return false;
        }
        else
        {
            foreach (var btn in _mouseReleasedThisTick)
                if (!filter.Check(btn)) return true;
            return false;
        }
    }

    public static bool GetMouseButton(string filterName) => GetMouseButton(Filter.Get(filterName));
    public static bool GetMouseButtonDown(string filterName) => GetMouseButtonDown(Filter.Get(filterName));
    public static bool GetMouseButtonUp(string filterName) => GetMouseButtonUp(Filter.Get(filterName));

    private static KeyCode MapMouseButtonToKeyCode(MouseButton button)
    {
        return button switch
        {
            MouseButton.Left => KeyCode.MouseLeft,
            MouseButton.Right => KeyCode.MouseRight,
            MouseButton.Middle => KeyCode.MouseMiddle,
            MouseButton.X1 => KeyCode.MouseX1,
            MouseButton.X2 => KeyCode.MouseX2,
            _ => KeyCode.Unknown
        };
    }

    /// <summary>
    /// 로직 틱(Update) 시작 시 호출되어 버퍼의 내용을 현재 틱의 상태로 확정합니다.
    /// </summary>
    public static void NewLogicTick()
    {
        _pressedThisTick.Clear();
        foreach (var k in _pressedBuffer) _pressedThisTick.Add(k);
        _pressedBuffer.Clear();

        _releasedThisTick.Clear();
        foreach (var k in _releasedBuffer) _releasedThisTick.Add(k);
        _releasedBuffer.Clear();

        _mousePressedThisTick.Clear();
        foreach (var b in _mousePressedBuffer) _mousePressedThisTick.Add(b);
        _mousePressedBuffer.Clear();

        _mouseReleasedThisTick.Clear();
        foreach (var b in _mouseReleasedBuffer) _mouseReleasedThisTick.Add(b);
        _mouseReleasedBuffer.Clear();

        _scrollDeltaThisTick = _scrollDeltaBuffer;
        _scrollDeltaBuffer = 0;

        _mouseDelta = _mousePosition - _previousMousePosition;
        _previousMousePosition = _mousePosition;
    }

    public static void ProcessEvent(SDL.SDL_Event evt)
    {
        if (evt.type == SDL.SDL_EventType.SDL_MOUSEMOTION)
        {
            _mousePosition = new Vector2(evt.motion.x, evt.motion.y);
            return;
        }

        if (!_enabled) return;

        switch (evt.type)
        {
            case SDL.SDL_EventType.SDL_KEYDOWN:
            {
                var key = (KeyCode)evt.key.keysym.scancode;
                if (_keysDown.Add(key)) _pressedBuffer.Add(key);
                break;
            }
            case SDL.SDL_EventType.SDL_KEYUP:
            {
                var key = (KeyCode)evt.key.keysym.scancode;
                if (_keysDown.Remove(key)) _releasedBuffer.Add(key);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
            {
                var button = (MouseButton)evt.button.button;
                if (_buttonsDown.Add(button)) _mousePressedBuffer.Add(button);
                
                // Map to unified KeyCode
                var key = MapMouseButtonToKeyCode(button);
                if (_keysDown.Add(key)) _pressedBuffer.Add(key);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
            {
                var button = (MouseButton)evt.button.button;
                if (_buttonsDown.Remove(button)) _mouseReleasedBuffer.Add(button);
                
                // Map to unified KeyCode
                var key = MapMouseButtonToKeyCode(button);
                if (_keysDown.Remove(key)) _releasedBuffer.Add(key);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEWHEEL:
            {
                _scrollDeltaBuffer += evt.wheel.y;
                break;
            }
        }
    }

    private static void ClearBuffers()
    {
        _pressedBuffer.Clear();
        _releasedBuffer.Clear();
        _mousePressedBuffer.Clear();
        _mouseReleasedBuffer.Clear();
        _pressedThisTick.Clear();
        _releasedThisTick.Clear();
        _mousePressedThisTick.Clear();
        _mouseReleasedThisTick.Clear();
        _scrollDeltaBuffer = 0;
        _scrollDeltaThisTick = 0;
    }

    public static void Reset()
    {
        _keysDown.Clear();
        _buttonsDown.Clear();
        ClearBuffers();
        _mousePosition = Vector2.Zero;
        _mouseDelta = Vector2.Zero;
        _previousMousePosition = Vector2.Zero;
    }

    [Obsolete("Use NewLogicTick instead")]
    public static void BeginFrame() { }
    [Obsolete("Use NewLogicTick instead")]
    public static void EndFrame() { }
}
