namespace Verity.Graphics;

public interface IRenderDevice : IRenderSurface, IDisposable
{
    void Clear(System.Drawing.Color color, RenderTarget? framebuffer = null);
    void Clear(Verity.Core.Color color, RenderTarget? framebuffer = null);
    RenderProgram CreateProgram(string vertexSource, string fragmentSource);
    RenderMeshBuilder CreateMeshBuilder(RenderMeshLayout layout);
    RenderTextureBuilder CreateTexture();
    RenderTargetBuilder CreateFramebuffer();
    void SetViewport(int x, int y, uint width, uint height);
    void EnableScissorTest();
    void DisableScissorTest();
    void SetScissor(int x, int y, uint width, uint height);
}
