using Irodori.Backend;
using Irodori.Error;
using Irodori.Type;
using Irodori.Windowing;
using SDL2;

namespace Verity.Graphics;

public class VeritySdl2Window : Window
{
    private readonly IntPtr _sdlWindow;
    private readonly IntPtr _glContext;

    public IntPtr SdlWindowHandle => _sdlWindow;
    public IntPtr GlContextHandle => _glContext;

    internal VeritySdl2Window(IntPtr sdlWindow, IntPtr glContext)
    {
        _sdlWindow = sdlWindow;
        _glContext = glContext;
    }

    public override bool ShouldClose { get; protected set; }

    public void Close()
    {
        ShouldClose = true;
    }

    // irodori's IrodoriSilkContext.Create() calls this — must return existing context, not create new
    public override IrodoriReturn<IntPtr> CreateGlContext()
    {
        return IrodoriReturn<IntPtr>.Success(_glContext);
    }

    public override IrodoriReturn<IntPtr> GetGlProcAddress(string procName)
    {
        SDL.SDL_ClearError();
        var addr = SDL.SDL_GL_GetProcAddress(procName);

        if (addr == IntPtr.Zero)
        {
            var error = SDL.SDL_GetError();
            // Some GL functions legitimately have address 0 on certain drivers.
            // Only treat as error if SDL actually reported one.
            if (!string.IsNullOrEmpty(error))
            {
                return IrodoriReturn<IntPtr>.Failure(
                    new VeritySdl2Exception($"SDL_GL_GetProcAddress failed for '{procName}': {error}"));
            }
        }

        return IrodoriReturn<IntPtr>.Success(addr);
    }

    public override void DeleteGlContext(IntPtr ctx)
    {
        SDL.SDL_GL_DeleteContext(ctx);
    }

    public override void GlSwapInterval(int interval)
    {
        SDL.SDL_GL_SetSwapInterval(interval);
    }

    public override void GlSwapBuffers()
    {
        SDL.SDL_GL_SwapWindow(_sdlWindow);
    }

    public override void GlMakeCurrent(IntPtr ctx)
    {
        SDL.SDL_GL_MakeCurrent(_sdlWindow, ctx);
    }

    public override IntPtr GlGetCurrentContext()
    {
        return SDL.SDL_GL_GetCurrentContext();
    }

    public override void PollEvents()
    {
        while (SDL.SDL_PollEvent(out SDL.SDL_Event e) == 1)
        {
            OnSdlEvent?.Invoke(e);

            switch (e.type)
            {
                case SDL.SDL_EventType.SDL_QUIT:
                    ShouldClose = true;
                    break;
            }
        }
    }

    public override void SwapBuffers()
    {
        SDL.SDL_GL_SwapWindow(_sdlWindow);
    }

    public override uint GetWidth()
    {
        SDL.SDL_GetWindowSize(_sdlWindow, out int w, out _);
        return (uint)w;
    }

    public override uint GetHeight()
    {
        SDL.SDL_GetWindowSize(_sdlWindow, out _, out int h);
        return (uint)h;
    }

    public void SetSize(int w, int h)
    {
        SDL.SDL_SetWindowSize(_sdlWindow, w, h);
    }

    public void SetTitle(string title)
    {
        SDL.SDL_SetWindowTitle(_sdlWindow, title);
    }

    public unsafe void SetIcon(byte[] rgbaPixels, int width, int height)
    {
        fixed (byte* ptr = rgbaPixels)
        {
            // SDL_CreateRGBSurfaceFrom expects masks for R, G, B, A
            // For standard RGBA8888:
            uint rmask = 0x000000ff;
            uint gmask = 0x0000ff00;
            uint bmask = 0x00ff0000;
            uint amask = 0xff000000;

            IntPtr surface = SDL.SDL_CreateRGBSurfaceFrom(
                (IntPtr)ptr, width, height, 32, width * 4,
                rmask, gmask, bmask, amask);

            if (surface != IntPtr.Zero)
            {
                SDL.SDL_SetWindowIcon(_sdlWindow, surface);
                SDL.SDL_FreeSurface(surface);
            }
        }
    }

    public override void Dispose()
    {
        SDL.SDL_GL_DeleteContext(_glContext);
        SDL.SDL_DestroyWindow(_sdlWindow);
    }

    public event Action<SDL.SDL_Event>? OnSdlEvent;
}

public class VeritySdl2Exception : Exception, IError
{
    public VeritySdl2Exception(string message) : base(message) { }
}
