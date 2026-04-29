using Irodori.Backend;
using Irodori.Error;
using Irodori.Type;
using Irodori.Windowing;
using SDL2;
using System.Runtime.InteropServices;

namespace Verity.Graphics;

public class VeritySdl2Window : Window
{
    private readonly IntPtr _sdlWindow;
    private readonly IntPtr _glContext;
    private readonly bool _ownsGlContext;

    public IntPtr SdlWindowHandle => _sdlWindow;
    public IntPtr GlContextHandle => _glContext;
    public uint WindowId { get; }

    internal VeritySdl2Window(IntPtr sdlWindow, IntPtr glContext, bool ownsGlContext = true)
    {
        _sdlWindow = sdlWindow;
        _glContext = glContext;
        _ownsGlContext = ownsGlContext;
        WindowId = SDL.SDL_GetWindowID(sdlWindow);
    }

    public override bool ShouldClose { get; protected set; }

    public void Close()
    {
        ShouldClose = true;
    }

    public void RequestClose()
    {
        ShouldClose = true;
    }

    public void CancelClose()
    {
        ShouldClose = false;
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

    public void SetResizable(bool resizable)
    {
        SDL.SDL_SetWindowResizable(_sdlWindow, resizable ? SDL.SDL_bool.SDL_TRUE : SDL.SDL_bool.SDL_FALSE);
    }

    public void SetBordered(bool bordered)
    {
        SDL.SDL_SetWindowBordered(_sdlWindow, bordered ? SDL.SDL_bool.SDL_TRUE : SDL.SDL_bool.SDL_FALSE);
    }

    public (int X, int Y) GetPosition()
    {
        SDL.SDL_GetWindowPosition(_sdlWindow, out int x, out int y);
        return (x, y);
    }

    public void SetPosition(int x, int y)
    {
        SDL.SDL_SetWindowPosition(_sdlWindow, x, y);
    }

    public void Show()
    {
        SDL.SDL_ShowWindow(_sdlWindow);
    }

    public void Hide()
    {
        SDL.SDL_HideWindow(_sdlWindow);
    }

    public void SetOpacity(float opacity)
    {
        SDL.SDL_SetWindowOpacity(_sdlWindow, Math.Clamp(opacity, 0.0f, 1.0f));
    }

    public void SetTaskSwitcherVisible(bool visible)
    {
        if (!OperatingSystem.IsWindows())
            return;

        IntPtr hwnd = GetWin32Hwnd();
        if (hwnd == IntPtr.Zero)
            return;

        nint exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        nint newExStyle = visible
            ? (exStyle | WsExAppWindow) & ~WsExToolWindow
            : (exStyle | WsExToolWindow) & ~WsExAppWindow;

        if (newExStyle == exStyle)
            return;

        SetWindowLongPtr(hwnd, GwlExStyle, newExStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_FRAMECHANGED);
    }

    public void Raise()
    {
        SDL.SDL_RaiseWindow(_sdlWindow);
    }

    public void PlaceAfter(VeritySdl2Window? insertAfter)
    {
        IntPtr hwnd = GetWin32Hwnd();

        if (hwnd == IntPtr.Zero)
        {
            Raise();
            return;
        }

        IntPtr insertAfterHwnd = HWND_TOP;
        if (insertAfter != null)
        {
            SDL.SDL_SysWMinfo afterInfo = default;
            SDL.SDL_GetVersion(out afterInfo.version);
            if (SDL.SDL_GetWindowWMInfo(insertAfter._sdlWindow, ref afterInfo) == SDL.SDL_bool.SDL_TRUE)
                insertAfterHwnd = afterInfo.info.win.window;
        }

        if (!SetWindowPos(hwnd, insertAfterHwnd, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER))
        {
            Raise();
        }
    }

    public (int X, int Y, int Width, int Height) GetPrimaryDisplayBounds()
    {
        if (SDL.SDL_GetDisplayBounds(0, out SDL.SDL_Rect bounds) != 0)
            return (0, 0, (int)GetWidth(), (int)GetHeight());

        return (bounds.x, bounds.y, bounds.w, bounds.h);
    }

    public void SetTitle(string title)
    {
        SDL.SDL_SetWindowTitle(_sdlWindow, title);
    }

    public VeritySdl2Window CreateAuxiliaryWindow(string title, int width, int height, int x, int y, bool resizable, bool visible = true, bool bordered = true)
    {
        var flags = SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL |
                    (visible ? SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN : SDL.SDL_WindowFlags.SDL_WINDOW_HIDDEN);
        if (resizable)
            flags |= SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
        if (!bordered)
            flags |= SDL.SDL_WindowFlags.SDL_WINDOW_BORDERLESS;

        IntPtr sdlWindow = SDL.SDL_CreateWindow(
            title,
            x,
            y,
            Math.Max(1, width),
            Math.Max(1, height),
            flags);

        if (sdlWindow == IntPtr.Zero)
            throw new VeritySdl2Exception($"SDL_CreateWindow failed: {SDL.SDL_GetError()}");

        return new VeritySdl2Window(sdlWindow, _glContext, ownsGlContext: false);
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
        if (_ownsGlContext)
            SDL.SDL_GL_DeleteContext(_glContext);

        SDL.SDL_DestroyWindow(_sdlWindow);
    }

    public event Action<SDL.SDL_Event>? OnSdlEvent;

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const int GwlExStyle = -20;
    private static readonly nint WsExToolWindow = 0x00000080;
    private static readonly nint WsExAppWindow = 0x00040000;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOOWNERZORDER = 0x0200;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private IntPtr GetWin32Hwnd()
    {
        if (!OperatingSystem.IsWindows())
            return IntPtr.Zero;

        SDL.SDL_SysWMinfo info = default;
        SDL.SDL_GetVersion(out info.version);
        return SDL.SDL_GetWindowWMInfo(_sdlWindow, ref info) == SDL.SDL_bool.SDL_TRUE
            ? info.info.win.window
            : IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(IntPtr hWnd, int nIndex, nint dwNewLong);
}

public class VeritySdl2Exception : Exception, IError
{
    public VeritySdl2Exception(string message) : base(message) { }
}
