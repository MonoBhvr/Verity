using Verity.Core.Audio;
using Verity.Game.Runtime;
using Verity.Graphics;

namespace Verity.Game.Browser;

public sealed class BrowserRuntimeHost : IRuntimeHost
{
    private readonly IGraphicsDeviceFactory _graphicsDeviceFactory;

    public BrowserRuntimeHost()
        : this(new BrowserGraphicsDeviceFactory(), new SilentAudioRuntime(), false)
    {
    }

    public BrowserRuntimeHost(bool minimalMode)
        : this(new BrowserGraphicsDeviceFactory(), new SilentAudioRuntime(), minimalMode)
    {
    }

    public BrowserRuntimeHost(IGraphicsDeviceFactory graphicsDeviceFactory, IAudioRuntime audioRuntime, bool minimalMode)
    {
        _graphicsDeviceFactory = graphicsDeviceFactory;
        AudioRuntime = audioRuntime;
        MinimalMode = minimalMode;
    }

    public IAudioRuntime AudioRuntime { get; }

    public bool MinimalMode { get; }

    public IRenderDevice CreateGraphicsDevice(string title, int width, int height, bool resizable)
    {
        return _graphicsDeviceFactory.Create(title, width, height, resizable);
    }

    public void AttachInput(IRenderDevice device)
    {
    }
}
