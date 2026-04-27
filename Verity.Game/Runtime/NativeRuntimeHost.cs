using Verity.Core.Audio;
using Verity.Graphics;

namespace Verity.Game.Runtime;

public sealed class NativeRuntimeHost : IRuntimeHost
{
    private readonly IGraphicsDeviceFactory _graphicsDeviceFactory;

    public NativeRuntimeHost()
        : this(new SdlOpenGlGraphicsDeviceFactory(), new MiniaudioAudioRuntime())
    {
    }

    public NativeRuntimeHost(IGraphicsDeviceFactory graphicsDeviceFactory, IAudioRuntime audioRuntime)
    {
        _graphicsDeviceFactory = graphicsDeviceFactory;
        AudioRuntime = audioRuntime;
    }

    public IAudioRuntime AudioRuntime { get; }

    public bool MinimalMode => false;

    public IRenderDevice CreateGraphicsDevice(string title, int width, int height, bool resizable)
    {
        return _graphicsDeviceFactory.Create(title, width, height, resizable);
    }

    public void AttachInput(IRenderDevice device)
    {
        if (device is GraphicsDevice graphicsDevice)
            graphicsDevice.Window.OnSdlEvent += Verity.Input.Input.ProcessEvent;
    }
}
