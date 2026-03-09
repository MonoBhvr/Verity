using System.Numerics;
using SDL2;

namespace Verity.Input;

public static class Input
{
    private static readonly HashSet<KeyCode> _currentKeys = [];
    private static readonly HashSet<KeyCode> _previousKeys = [];
    private static readonly HashSet<MouseButton> _currentMouseButtons = [];
    private static readonly HashSet<MouseButton> _previousMouseButtons = [];

    private static Vector2 _mousePosition;
    private static Vector2 _mouseDelta;
    private static Vector2 _previousMousePosition;
    private static float _scrollDelta;

    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseDelta => _mouseDelta;
    public static float ScrollDelta => _scrollDelta;

    public static bool GetKey(KeyCode key) => _currentKeys.Contains(key);
    public static bool GetKeyDown(KeyCode key) => _currentKeys.Contains(key) && !_previousKeys.Contains(key);
    public static bool GetKeyUp(KeyCode key) => !_currentKeys.Contains(key) && _previousKeys.Contains(key);

    public static bool GetMouseButton(MouseButton button) => _currentMouseButtons.Contains(button);
    public static bool GetMouseButtonDown(MouseButton button) => _currentMouseButtons.Contains(button) && !_previousMouseButtons.Contains(button);
    public static bool GetMouseButtonUp(MouseButton button) => !_currentMouseButtons.Contains(button) && _previousMouseButtons.Contains(button);

    public static void BeginFrame()
    {
        _previousKeys.Clear();
        foreach (var k in _currentKeys)
            _previousKeys.Add(k);

        _previousMouseButtons.Clear();
        foreach (var b in _currentMouseButtons)
            _previousMouseButtons.Add(b);

        _previousMousePosition = _mousePosition;
        _scrollDelta = 0;
    }

    public static void EndFrame()
    {
        _mouseDelta = _mousePosition - _previousMousePosition;
    }

    public static void ProcessEvent(SDL.SDL_Event evt)
    {
        switch (evt.type)
        {
            case SDL.SDL_EventType.SDL_KEYDOWN:
            {
                var key = (KeyCode)evt.key.keysym.scancode;
                _currentKeys.Add(key);
                break;
            }
            case SDL.SDL_EventType.SDL_KEYUP:
            {
                var key = (KeyCode)evt.key.keysym.scancode;
                _currentKeys.Remove(key);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
            {
                var button = (MouseButton)evt.button.button;
                _currentMouseButtons.Add(button);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
            {
                var button = (MouseButton)evt.button.button;
                _currentMouseButtons.Remove(button);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEMOTION:
            {
                _mousePosition = new Vector2(evt.motion.x, evt.motion.y);
                break;
            }
            case SDL.SDL_EventType.SDL_MOUSEWHEEL:
            {
                _scrollDelta += evt.wheel.y;
                break;
            }
        }
    }

    public static void Reset()
    {
        _currentKeys.Clear();
        _previousKeys.Clear();
        _currentMouseButtons.Clear();
        _previousMouseButtons.Clear();
        _mousePosition = Vector2.Zero;
        _mouseDelta = Vector2.Zero;
        _previousMousePosition = Vector2.Zero;
        _scrollDelta = 0;
    }
}
