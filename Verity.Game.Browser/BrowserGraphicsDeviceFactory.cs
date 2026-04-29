using Verity.Graphics;

namespace Verity.Game.Browser;

public sealed class BrowserGraphicsDeviceFactory : IGraphicsDeviceFactory
{
    public IRenderDevice Create(string title = "Verity Engine", int width = 1280, int height = 720, bool resizable = true, bool visible = true)
    {
        return new BrowserRenderDevice("verity-canvas", width, height);
    }
}
