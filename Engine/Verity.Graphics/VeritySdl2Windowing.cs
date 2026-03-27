using Irodori.Backend;
using Irodori.Error;
using Irodori.Type;
using Irodori.Windowing;
using SDL2;

namespace Verity.Graphics;

public class VeritySdl2Windowing : IWindowing<VeritySdl2Window>
{
    public IrodoriReturn<VeritySdl2Window> CreateWindow(Window.InitConfig config, IBackend backend)
    {
        if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_EVERYTHING) < 0)
        {
            return IrodoriReturn<VeritySdl2Window>.Failure(
                new VeritySdl2Exception($"SDL_Init failed: {SDL.SDL_GetError()}"));
        }

        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK,
            (int)SDL.SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 3);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_DOUBLEBUFFER, 1);
        SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_DEPTH_SIZE, 0); // 2D engine — no depth buffer

        var flags = SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN;
        if (config.Resizable) flags |= SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (config.Fullscreen) flags |= SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;

        var sdlWindow = SDL.SDL_CreateWindow(
            config.Title,
            SDL.SDL_WINDOWPOS_CENTERED,
            SDL.SDL_WINDOWPOS_CENTERED,
            config.Width,
            config.Height,
            flags);

        if (sdlWindow == IntPtr.Zero)
        {
            return IrodoriReturn<VeritySdl2Window>.Failure(
                new VeritySdl2Exception($"SDL_CreateWindow failed: {SDL.SDL_GetError()}"));
        }

        var glContext = SDL.SDL_GL_CreateContext(sdlWindow);
        if (glContext == IntPtr.Zero)
        {
            SDL.SDL_DestroyWindow(sdlWindow);
            return IrodoriReturn<VeritySdl2Window>.Failure(
                new VeritySdl2Exception($"SDL_GL_CreateContext failed: {SDL.SDL_GetError()}"));
        }

        SDL.SDL_GL_MakeCurrent(sdlWindow, glContext);

        return IrodoriReturn<VeritySdl2Window>.Success(new VeritySdl2Window(sdlWindow, glContext));
    }
}
