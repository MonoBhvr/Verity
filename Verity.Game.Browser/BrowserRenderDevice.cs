using System.Drawing;
using Verity.Graphics;

namespace Verity.Game.Browser;

public sealed class BrowserRenderDevice : IRenderDevice
{
    private readonly int _contextHandle;

    public BrowserRenderDevice(string canvasId, int width, int height)
    {
        _contextHandle = BrowserGraphicsInterop.CreateContext(canvasId, width, height);
    }

    public uint Width => (uint)BrowserGraphicsInterop.GetWidth(_contextHandle);
    public uint Height => (uint)BrowserGraphicsInterop.GetHeight(_contextHandle);
    public bool ShouldClose => false;

    public void PollEvents()
    {
    }

    public void SwapBuffers()
    {
    }

    public void Clear(Color color, RenderTarget? framebuffer = null)
    {
        BrowserGraphicsInterop.Clear(_contextHandle, framebuffer is BrowserRenderTarget target ? target.FramebufferHandle : 0, color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
    }

    public void Clear(Verity.Core.Color color, RenderTarget? framebuffer = null)
    {
        BrowserGraphicsInterop.Clear(_contextHandle, framebuffer is BrowserRenderTarget target ? target.FramebufferHandle : 0, color.R, color.G, color.B, color.A);
    }

    public RenderProgram CreateProgram(string vertexSource, string fragmentSource)
    {
        int handle = BrowserGraphicsInterop.CreateProgram(_contextHandle, BrowserShaderSourceAdaptation.ToWebGl2Vertex(vertexSource), BrowserShaderSourceAdaptation.ToWebGl2Fragment(fragmentSource));
        return new BrowserRenderProgram(_contextHandle, handle);
    }

    public RenderMeshBuilder CreateMeshBuilder(RenderMeshLayout layout)
    {
        return new BrowserRenderMeshBuilder(_contextHandle);
    }

    public RenderTextureBuilder CreateTexture()
    {
        return new BrowserRenderTextureBuilder(_contextHandle);
    }

    public RenderTargetBuilder CreateFramebuffer()
    {
        return new BrowserRenderTargetBuilder(_contextHandle);
    }

    public void SetViewport(int x, int y, uint width, uint height)
    {
        BrowserGraphicsInterop.SetViewport(_contextHandle, x, y, (int)width, (int)height);
    }

    public void EnableScissorTest()
    {
        BrowserGraphicsInterop.EnableScissor(_contextHandle);
    }

    public void DisableScissorTest()
    {
        BrowserGraphicsInterop.DisableScissor(_contextHandle);
    }

    public void SetScissor(int x, int y, uint width, uint height)
    {
        BrowserGraphicsInterop.SetScissor(_contextHandle, x, y, (int)width, (int)height);
    }

    public void BeginWindowOutputs()
    {
        BrowserGraphicsInterop.PresentWindowOutputsBegin(_contextHandle);
    }

    public void PresentWindowOutput(
        string key,
        string title,
        int x,
        int y,
        int width,
        int height,
        int order,
        string group,
        bool decorated,
        bool lockPosition,
        bool lockSize,
        RenderTexture texture)
    {
        BrowserGraphicsInterop.PresentWindowOutput(
            _contextHandle,
            key,
            title,
            x,
            y,
            width,
            height,
            order,
            group,
            decorated,
            lockPosition,
            lockSize,
            ((BrowserRenderTexture)texture).TextureHandle);
    }

    public void EndWindowOutputs()
    {
        BrowserGraphicsInterop.PresentWindowOutputsEnd(_contextHandle);
    }

    public void Dispose()
    {
    }
}
