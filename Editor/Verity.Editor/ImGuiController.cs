using System.Numerics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL2;
using SDL2;
using Verity.Graphics;

namespace Verity.Editor;

public unsafe class ImGuiController : IDisposable
{
    private ImGuiContextPtr _context;

    public void Initialize(GraphicsDevice device, string? fontPath = null, float fontSize = 18f)
    {
        _context = ImGui.CreateContext();
        ImGui.SetCurrentContext(_context);

        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;

        if (!string.IsNullOrEmpty(fontPath) && File.Exists(fontPath))
        {
            LoadFont(fontPath, fontSize);
        }

        ApplyModernDarkTheme();

        ImGuiImplSDL2.SetCurrentContext(_context);
        ImGuiImplSDL2.InitForOpenGL(
            (SDLWindow*)device.Window.SdlWindowHandle,
            (void*)device.Window.GlContextHandle);

        ImGuiImplOpenGL3.SetCurrentContext(_context);
        ImGuiImplOpenGL3.Init((byte*)null);

        device.Window.OnSdlEvent += OnSdlEvent;
    }

    private void LoadFont(string path, float size)
    {
        var io = ImGui.GetIO();
        
        // Manual definition of Korean glyph ranges for ImGui (ImWchar ranges)
        // Format: [start, end, ..., 0]
        // Basic Latin: 0x0020 - 0x00FF
        // Korean: 0x3131 - 0x3163 (Compatibility Jamo), 0xAC00 - 0xD7A3 (Hangul Syllables)
        // Note: Hexa.NET uses uint* for ranges.
        fixed (uint* ranges = new uint[] { 
            0x0020, 0x00FF, // Basic Latin
            0x3131, 0x3163, // Korean Jamo
            0xAC00, 0xD7A3, // Korean Syllables
            0 
        })
        {
            io.Fonts.AddFontFromFileTTF(path, size, (ImFontConfig*)null, ranges);
        }
    }

    private static void ApplyModernDarkTheme()
    {
        var style = ImGui.GetStyle();
        style.WindowRounding = 6f;
        style.ChildRounding = 4f;
        style.FrameRounding = 4f;
        style.PopupRounding = 4f;
        style.ScrollbarRounding = 4f;
        style.GrabRounding = 3f;
        style.TabRounding = 4f;
        style.WindowPadding = new Vector2(8, 8);
        style.FramePadding = new Vector2(4, 2);
        style.CellPadding = new Vector2(4, 2);
        style.ItemSpacing = new Vector2(8, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        style.IndentSpacing = 20f;
        style.ScrollbarSize = 13f;
        style.GrabMinSize = 10f;
        style.WindowBorderSize = 1f;
        style.ChildBorderSize = 1f;
        style.PopupBorderSize = 1f;
        style.WindowTitleAlign = new Vector2(0.5f, 0.5f);

        var colors = style.Colors;
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.12f, 0.12f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.12f, 0.12f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.10f, 0.10f, 0.12f, 0.96f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.22f, 0.22f, 0.26f, 1.00f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.16f, 0.19f, 1.00f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.09f, 0.09f, 0.11f, 1.00f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.12f, 0.12f, 0.14f, 1.00f);
        colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.14f, 0.14f, 0.16f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.10f, 0.10f, 0.12f, 1.00f);
        colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.28f, 0.28f, 0.32f, 1.00f);
        colors[(int)ImGuiCol.Button] = new Vector4(0.22f, 0.30f, 0.42f, 1.00f);
        colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.24f, 0.32f, 1.00f);
        colors[(int)ImGuiCol.Tab] = new Vector4(0.14f, 0.14f, 0.17f, 1.00f);
        colors[(int)ImGuiCol.Text] = new Vector4(0.90f, 0.90f, 0.92f, 1.00f);
    }

    private void OnSdlEvent(SDL.SDL_Event evt)
    {
        // 1. Let the backend process the event first
        ImGuiImplSDL2.ProcessEvent((SDLEvent*)&evt);

        // 2. Explicitly handle mouse wheel if backend missed it or scale is weird
        if (evt.type == SDL.SDL_EventType.SDL_MOUSEWHEEL)
        {
            var io = ImGui.GetIO();
            float wheelX = evt.wheel.x;
            float wheelY = evt.wheel.y;
            
            if (evt.wheel.direction == (uint)SDL.SDL_MouseWheelDirection.SDL_MOUSEWHEEL_FLIPPED)
            {
                wheelX *= -1;
                wheelY *= -1;
            }
            
            io.AddMouseWheelEvent(wheelX, wheelY);
        }
    }

    public void BeginFrame()
    {
        ImGuiImplOpenGL3.NewFrame();
        ImGuiImplSDL2.NewFrame();
        ImGui.NewFrame();
    }

    public void EndFrame()
    {
        ImGui.Render();
        ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
    }

    public void Dispose()
    {
        ImGuiImplOpenGL3.Shutdown();
        ImGuiImplSDL2.Shutdown();
        if ((nint)_context.Handle != 0)
            ImGui.DestroyContext(_context);
    }
}
