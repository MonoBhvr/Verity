namespace Verity.Graphics;

public interface IGraphicsDeviceFactory
{
    IRenderDevice Create(string title = "Verity Engine", int width = 1280, int height = 720, bool resizable = true, bool visible = true);
}
