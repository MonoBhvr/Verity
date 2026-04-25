namespace Verity.Input;

public enum InputEventKind
{
    KeyDown,
    KeyUp,
    MouseButtonDown,
    MouseButtonUp,
    MouseMove,
    MouseWheel
}

public readonly record struct InputEvent(
    InputEventKind Kind,
    KeyCode Key = KeyCode.Unknown,
    MouseButton MouseButton = MouseButton.Left,
    float MouseX = 0,
    float MouseY = 0,
    float ScrollDelta = 0)
{
    public static InputEvent KeyDown(KeyCode key) => new(InputEventKind.KeyDown, Key: key);
    public static InputEvent KeyUp(KeyCode key) => new(InputEventKind.KeyUp, Key: key);
    public static InputEvent MouseButtonDown(MouseButton button) => new(InputEventKind.MouseButtonDown, MouseButton: button);
    public static InputEvent MouseButtonUp(MouseButton button) => new(InputEventKind.MouseButtonUp, MouseButton: button);
    public static InputEvent MouseMove(float x, float y) => new(InputEventKind.MouseMove, MouseX: x, MouseY: y);
    public static InputEvent MouseWheel(float delta) => new(InputEventKind.MouseWheel, ScrollDelta: delta);
}
