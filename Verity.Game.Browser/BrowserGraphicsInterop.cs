using System.Runtime.InteropServices.JavaScript;

namespace Verity.Game.Browser;

internal static partial class BrowserGraphicsInterop
{
    [JSImport("globalThis.createContext")]
    internal static partial int CreateContext(string canvasId, int width, int height);

    [JSImport("globalThis.getWidth")]
    internal static partial int GetWidth(int contextHandle);

    [JSImport("globalThis.getHeight")]
    internal static partial int GetHeight(int contextHandle);

    [JSImport("globalThis.setViewport")]
    internal static partial void SetViewport(int contextHandle, int x, int y, int width, int height);

    [JSImport("globalThis.enableScissor")]
    internal static partial void EnableScissor(int contextHandle);

    [JSImport("globalThis.disableScissor")]
    internal static partial void DisableScissor(int contextHandle);

    [JSImport("globalThis.setScissor")]
    internal static partial void SetScissor(int contextHandle, int x, int y, int width, int height);

    [JSImport("globalThis.clear")]
    internal static partial void Clear(int contextHandle, int framebufferHandle, float r, float g, float b, float a);

    [JSImport("globalThis.createProgram")]
    internal static partial int CreateProgram(int contextHandle, string vertexSource, string fragmentSource);

    [JSImport("globalThis.setProgramFloat")]
    internal static partial void SetProgramFloat(int contextHandle, int programHandle, string name, float value);

    [JSImport("globalThis.setProgramVec2")]
    internal static partial void SetProgramVec2(int contextHandle, int programHandle, string name, float x, float y);

    [JSImport("globalThis.setProgramVec3")]
    internal static partial void SetProgramVec3(int contextHandle, int programHandle, string name, float x, float y, float z);

    [JSImport("globalThis.setProgramVec4")]
    internal static partial void SetProgramVec4(int contextHandle, int programHandle, string name, float x, float y, float z, float w);

    [JSImport("globalThis.setProgramMat4")]
    internal static partial void SetProgramMat4(
        int contextHandle,
        int programHandle,
        string name,
        float m11,
        float m12,
        float m13,
        float m14,
        float m21,
        float m22,
        float m23,
        float m24,
        float m31,
        float m32,
        float m33,
        float m34,
        float m41,
        float m42,
        float m43,
        float m44);

    [JSImport("globalThis.bindProgramTexture")]
    internal static partial void BindProgramTexture(int contextHandle, int programHandle, string name, int textureHandle);

    [JSImport("globalThis.deleteProgram")]
    internal static partial void DeleteProgram(int contextHandle, int programHandle);

    [JSImport("globalThis.createTexture")]
    internal static partial int CreateTexture(int contextHandle, int width, int height, bool linear, byte[]? pixels);

    [JSImport("globalThis.deleteTexture")]
    internal static partial void DeleteTexture(int contextHandle, int textureHandle);

    [JSImport("globalThis.createFramebuffer")]
    internal static partial int CreateFramebuffer(int contextHandle, int textureHandle);

    [JSImport("globalThis.deleteFramebuffer")]
    internal static partial void DeleteFramebuffer(int contextHandle, int framebufferHandle);

    [JSImport("globalThis.createMesh")]
    internal static partial int CreateMesh(int contextHandle, string vertices, string indices);

    [JSImport("globalThis.drawMesh")]
    internal static partial void DrawMesh(int contextHandle, int meshHandle, int programHandle, int framebufferHandle);

    [JSImport("globalThis.deleteMesh")]
    internal static partial void DeleteMesh(int contextHandle, int meshHandle);

    [JSImport("globalThis.presentWindowOutputsBegin")]
    internal static partial void PresentWindowOutputsBegin(int contextHandle);

    [JSImport("globalThis.presentWindowOutput")]
    internal static partial void PresentWindowOutput(
        int contextHandle,
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
        int textureHandle);

    [JSImport("globalThis.presentWindowOutputsEnd")]
    internal static partial void PresentWindowOutputsEnd(int contextHandle);
}
