namespace Verity.Graphics;

public interface IRenderSurface
{
    uint Width { get; }
    uint Height { get; }
    bool ShouldClose { get; }
    void PollEvents();
    void SwapBuffers();
}
