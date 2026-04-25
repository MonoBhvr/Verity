using System.Drawing;
using Irodori;
using Irodori.Backend.OpenGL;
using Irodori.Framebuffer;
using Irodori.Shader;
using Irodori.Texture;
using Silk.NET.OpenGL;

namespace Verity.Graphics;

public class GraphicsDevice : IRenderDevice
{
    public static IGraphicsDeviceFactory DefaultFactory { get; set; } = new SdlOpenGlGraphicsDeviceFactory();

    private readonly Gfx<OpenGlBackend, VeritySdl2Window> _gfx;
    private readonly OpenGlBackend _backend;

    public VeritySdl2Window Window => (VeritySdl2Window)_gfx.Window;
    public Gfx Gfx => _gfx;
    public GL Gl => _backend.Gl!;
    public uint Width => Window.GetWidth();
    public uint Height => Window.GetHeight();

    internal GraphicsDevice(Gfx<OpenGlBackend, VeritySdl2Window> gfx, OpenGlBackend backend)
    {
        _gfx = gfx;
        _backend = backend;
    }

    public static GraphicsDevice Create(
        string title = "Verity Engine",
        int width = 1280,
        int height = 720,
        bool resizable = true)
    {
        return (GraphicsDevice)DefaultFactory.Create(title, width, height, resizable);
    }

    public void Clear(System.Drawing.Color color, RenderTarget? framebuffer = null)
    {
        _gfx.Clear(color, (framebuffer as NativeRenderTarget)?.Resource).Unwrap();
    }
    
    public void Clear(Verity.Core.Color color, RenderTarget? framebuffer = null)
    {
        _gfx.Clear(color, (framebuffer as NativeRenderTarget)?.Resource).Unwrap();
    }

    public RenderProgram CreateProgram(string vertexSource, string fragmentSource)
    {
        var vertexShader = _gfx.CreateShader(EShaderType.Vertex, vertexSource)
            .Compile()
            .Unwrap();

        var fragmentShader = _gfx.CreateShader(EShaderType.Fragment, fragmentSource)
            .Compile()
            .Unwrap();

        var program = _gfx.CreateShaderProgram()
            .AttachShader(vertexShader)
            .AttachShader(fragmentShader)
            .Link()
            .Unwrap();

        vertexShader.Dispose();
        fragmentShader.Dispose();

        return new NativeRenderProgram(program);
    }

    public RenderMeshBuilder CreateMeshBuilder(RenderMeshLayout layout)
    {
        var format = layout switch
        {
            RenderMeshLayout.PositionTexture2D => Irodori.Buffer.VertexBufferFormat.Create()
                .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2())
                .AddAttrib(Irodori.Buffer.VertexBufferFormat.Attrib.Vector2()),
            _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, null)
        };

        return new NativeRenderMeshBuilder(_gfx.CreateVertexBuffer(format));
    }

    public RenderTextureBuilder CreateTexture()
    {
        return new NativeRenderTextureBuilder(_gfx.CreateTexture());
    }

    public RenderTargetBuilder CreateFramebuffer()
    {
        return new NativeRenderTargetBuilder(_gfx.CreateFramebuffer());
    }

    public void SwapBuffers()
    {
        Window.SwapBuffers();
    }

    public void SetSwapInterval(int interval)
    {
        Window.GlSwapInterval(interval);
    }

    public void PollEvents()
    {
        Window.PollEvents();
    }

    public void SetViewport(int x, int y, uint width, uint height)
    {
        _backend.Gl!.Viewport(x, y, width, height);
    }

    public void EnableScissorTest()
    {
        _backend.Gl!.Enable(EnableCap.ScissorTest);
    }

    public void DisableScissorTest()
    {
        _backend.Gl!.Disable(EnableCap.ScissorTest);
    }

    public void SetScissor(int x, int y, uint width, uint height)
    {
        _backend.Gl!.Scissor(x, y, width, height);
    }

    public void SetSize(int w, int h)
    {
        Window.SetSize(w, h);
    }

    public (int X, int Y) GetWindowPosition()
    {
        return Window.GetPosition();
    }

    public void SetWindowPosition(int x, int y)
    {
        Window.SetPosition(x, y);
    }

    public void SetWindowIcon(byte[] rgbaPixels, int width, int height)
    {
        Window.SetIcon(rgbaPixels, width, height);
    }

    public void SetWindowTitle(string title)
    {
        if (Window is VeritySdl2Window sdlWin) sdlWin.SetTitle(title);
    }

    public bool ShouldClose => Window.ShouldClose;

    public void Dispose()
    {
        _gfx.Dispose();
    }
}
