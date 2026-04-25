using Verity.Input;
using InputState = Verity.Input.Input;

namespace Verity.Tests;

public sealed class InputEventTests : IDisposable
{
    public InputEventTests()
    {
        InputState.Reset();
        InputState.Enabled = true;
    }

    [Fact]
    public void NeutralKeyEvents_UpdateInputStateWithoutSdl()
    {
        InputState.ProcessEvent(InputEvent.KeyDown(KeyCode.Space));
        InputState.NewLogicTick();

        Assert.True(InputState.Down(KeyCode.Space));
        Assert.True(InputState.Pressed(KeyCode.Space));

        InputState.ProcessEvent(InputEvent.KeyUp(KeyCode.Space));
        InputState.NewLogicTick();

        Assert.False(InputState.Down(KeyCode.Space));
        Assert.True(InputState.Released(KeyCode.Space));
    }

    [Fact]
    public void NeutralMouseEvents_UpdateMouseStateWithoutSdl()
    {
        InputState.ProcessEvent(InputEvent.MouseMove(24, 12));
        InputState.ProcessEvent(InputEvent.MouseButtonDown(MouseButton.Left));
        InputState.ProcessEvent(InputEvent.MouseWheel(2));
        InputState.NewLogicTick();

        Assert.Equal(new System.Numerics.Vector2(24, 12), InputState.MousePosition);
    }

    public void Dispose()
    {
        InputState.Enabled = true;
        InputState.Reset();
    }
}
