using Irodori;
using Irodori.Backend.OpenGL;
using Irodori.Windowing;
using Silk.NET.OpenGL;

namespace Verity.Graphics;

public sealed class SdlOpenGlGraphicsDeviceFactory : IGraphicsDeviceFactory
{
    public IRenderDevice Create(string title = "Verity Engine", int width = 1280, int height = 720, bool resizable = true)
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

        backend.Gl!.Disable(EnableCap.DepthTest);
        backend.Gl.Enable(EnableCap.Blend);
        backend.Gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

        return new GraphicsDevice(gfx, backend);
    }
}
