using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Verity.Editor;

internal static class NativeDetachedWindowStyler
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;

    private const nint WsCaption = 0x00C00000;
    private const nint WsSysMenu = 0x00080000;
    private const nint WsThickFrame = 0x00040000;
    private const nint WsMinimizeBox = 0x00020000;
    private const nint WsMaximizeBox = 0x00010000;

    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;

    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private const uint GaRootOwner = 3;

    public static void Apply()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using Process process = Process.GetCurrentProcess();
        var state = new EnumState(process.Id, process.MainWindowHandle);
        GCHandle stateHandle = GCHandle.Alloc(state);
        try
        {
            EnumWindows(static (hwnd, lParam) =>
            {
                var handle = GCHandle.FromIntPtr(lParam);
                var state = (EnumState)handle.Target!;
                if (IsAuxiliarySdlWindow(hwnd, state.ProcessId, state.MainHandle))
                    ApplyStandardWindowStyle(hwnd);

                return true;
            }, GCHandle.ToIntPtr(stateHandle));
        }
        finally
        {
            stateHandle.Free();
        }
    }

    private static bool IsAuxiliarySdlWindow(nint hwnd, int processId, nint mainHandle)
    {
        if (hwnd == nint.Zero || hwnd == mainHandle || !IsWindowVisible(hwnd))
            return false;

        GetWindowThreadProcessId(hwnd, out uint ownerProcessId);
        if (ownerProcessId != processId)
            return false;

        nint rootOwner = GetAncestor(hwnd, GaRootOwner);
        if (mainHandle != nint.Zero && rootOwner != mainHandle)
            return false;

        StringBuilder classNameBuffer = new(64);
        int classNameLength = GetClassName(hwnd, classNameBuffer, classNameBuffer.Capacity);
        if (classNameLength <= 0)
            return false;

        return string.Equals(classNameBuffer.ToString(), "SDL_app", StringComparison.Ordinal);
    }

    private static void ApplyStandardWindowStyle(nint hwnd)
    {
        nint style = GetWindowLongPtr(hwnd, GwlStyle);
        nint exStyle = GetWindowLongPtr(hwnd, GwlExStyle);

        nint requiredStyle = WsCaption | WsSysMenu | WsThickFrame | WsMinimizeBox | WsMaximizeBox;
        nint newStyle = style | requiredStyle;
        nint newExStyle = (exStyle | WsExAppWindow) & ~WsExToolWindow;

        if (newStyle == style && newExStyle == exStyle)
            return;

        SetWindowLongPtr(hwnd, GwlStyle, newStyle);
        SetWindowLongPtr(hwnd, GwlExStyle, newExStyle);
        SetWindowPos(hwnd, nint.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    private readonly record struct EnumState(int ProcessId, nint MainHandle);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hWnd, uint gaFlags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
