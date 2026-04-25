using Verity.Core.Audio;
using Verity.Graphics;

namespace Verity.Game.Runtime;

public interface IRuntimeHost
{
    IRenderDevice CreateGraphicsDevice(string title, int width, int height, bool resizable);
    void AttachInput(IRenderDevice device);
    IAudioRuntime AudioRuntime { get; }
    bool MinimalMode { get; }
}
