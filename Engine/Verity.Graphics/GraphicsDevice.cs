using System.Drawing;
using Irodori;
using Irodori.Backend.OpenGL;
using Irodori.Buffer;
using Irodori.Framebuffer;
using Irodori.Shader;
using Irodori.Texture;
using Irodori.Type;
using Irodori.Windowing;
using Silk.NET.OpenGL;

namespace Verity.Graphics;

public class GraphicsDevice : IDisposable
{
    private readonly Gfx<OpenGlBackend, VeritySdl2Window> _gfx;
    private readonly OpenGlBackend _backend;

    public VeritySdl2Window Window => (VeritySdl2Window)_gfx.Window;
    public Gfx Gfx => _gfx;
    public GL Gl => _backend.Gl!;

    private GraphicsDevice(Gfx<OpenGlBackend, VeritySdl2Window> gfx, OpenGlBackend backend)
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
        var backend = new OpenGlBackend();
        var windowing = new VeritySdl2Windowing();

        var windowConfig = new Window.InitConfig
        {
            Title = title,
            Width = width,
            Height = height,
            Resizable = resizable,
            Fullscreen = false,
        };

        var gfx = Gfx<OpenGlBackend, VeritySdl2Window>.Create()
            .WithBackend(backend)
            .WithWindowing(windowing)
            .WithWindowConfig(windowConfig)
            .Init()
            .Unwrap();

        // 2D engine: CPU-based sprite sorting, no depth test needed
        backend.Gl!.Disable(EnableCap.DepthTest);
        backend.Gl.Enable(EnableCap.Blend);
        backend.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        return new GraphicsDevice(gfx, backend);
    }

    public void Clear(Color color, FramebufferObject.Uploaded? framebuffer = null)
    {
        _gfx.Clear(color, framebuffer).Unwrap();
    }

    public void Clear(Verity.Core.Color color, FramebufferObject.Uploaded? framebuffer = null)
    {
        _gfx.Clear(color, framebuffer).Unwrap();
    }

    public ShaderObject.BeforeCompile CreateShader(EShaderType type, string source)
    {
        return _gfx.CreateShader(type, source);
    }

    public ShaderProgram.BeforeLinking CreateShaderProgram()
    {
        return _gfx.CreateShaderProgram();
    }

    public VertexBuffer.Unuploaded CreateVertexBuffer(VertexBufferFormat format)
    {
        return _gfx.CreateVertexBuffer(format);
    }

    public TextureObjectUnuploaded CreateTexture()
    {
        return _gfx.CreateTexture();
    }

    public FramebufferObject.Unuploaded CreateFramebuffer()
    {
        return _gfx.CreateFramebuffer();
    }

    public void SwapBuffers()
    {
        Window.SwapBuffers();
    }

    public void PollEvents()
    {
        Window.PollEvents();
    }

    public void SetSize(int w, int h)
    {
        Window.SetSize(w, h);
    }

    public void SetWindowIcon(byte[] rgbaPixels, int width, int height)
    {
        Window.SetIcon(rgbaPixels, width, height);
    }

    public bool ShouldClose => Window.ShouldClose;

    public void Dispose()
    {
        _gfx.Dispose();
    }
}
