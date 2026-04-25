using System.Numerics;
using Verity.Graphics;

namespace Verity.Game.Browser;

internal sealed class BrowserRenderProgram : RenderProgram
{
    private readonly int _contextHandle;
    private readonly int _programHandle;
    private readonly Dictionary<string, float> _floatCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector2> _vec2Cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _vec3Cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector4> _vec4Cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix4x4> _mat4Cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _textureCache = new(StringComparer.Ordinal);

    public BrowserRenderProgram(int contextHandle, int programHandle)
    {
        _contextHandle = contextHandle;
        _programHandle = programHandle;
    }

    internal int ProgramHandle => _programHandle;

    public override void SetTexture(string name, RenderTexture texture)
    {
        int handle = ((BrowserRenderTexture)texture).TextureHandle;
        if (_textureCache.TryGetValue(name, out int current) && current == handle)
            return;

        _textureCache[name] = handle;
        BrowserGraphicsInterop.BindProgramTexture(_contextHandle, _programHandle, name, handle);
    }

    public override void SetFloat(string name, float value)
    {
        if (_floatCache.TryGetValue(name, out float current) && current == value)
            return;

        _floatCache[name] = value;
        BrowserGraphicsInterop.SetProgramFloat(_contextHandle, _programHandle, name, value);
    }

    public override void SetVec2(string name, Vector2 value)
    {
        if (_vec2Cache.TryGetValue(name, out Vector2 current) && current == value)
            return;

        _vec2Cache[name] = value;
        BrowserGraphicsInterop.SetProgramVec2(_contextHandle, _programHandle, name, value.X, value.Y);
    }

    public override void SetVec3(string name, Vector3 value)
    {
        if (_vec3Cache.TryGetValue(name, out Vector3 current) && current == value)
            return;

        _vec3Cache[name] = value;
        BrowserGraphicsInterop.SetProgramVec3(_contextHandle, _programHandle, name, value.X, value.Y, value.Z);
    }

    public override void SetVec4(string name, Vector4 value)
    {
        if (_vec4Cache.TryGetValue(name, out Vector4 current) && current == value)
            return;

        _vec4Cache[name] = value;
        BrowserGraphicsInterop.SetProgramVec4(_contextHandle, _programHandle, name, value.X, value.Y, value.Z, value.W);
    }

    public override void SetMat4(string name, Matrix4x4 value)
    {
        if (_mat4Cache.TryGetValue(name, out Matrix4x4 current) && current == value)
            return;

        _mat4Cache[name] = value;
        BrowserGraphicsInterop.SetProgramMat4(
            _contextHandle,
            _programHandle,
            name,
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44
        );
    }

    public override void Dispose() => BrowserGraphicsInterop.DeleteProgram(_contextHandle, _programHandle);
}

internal sealed class BrowserRenderTexture : RenderTexture
{
    private readonly int _contextHandle;

    public BrowserRenderTexture(int contextHandle, int textureHandle, int width, int height)
    {
        _contextHandle = contextHandle;
        TextureHandle = textureHandle;
        Width = width;
        Height = height;
    }

    internal int TextureHandle { get; }

    public override int Width { get; }
    public override int Height { get; }

    public override void Dispose() => BrowserGraphicsInterop.DeleteTexture(_contextHandle, TextureHandle);
}

internal sealed class BrowserRenderTarget : RenderTarget
{
    private readonly int _contextHandle;

    public BrowserRenderTarget(int contextHandle, int framebufferHandle)
    {
        _contextHandle = contextHandle;
        FramebufferHandle = framebufferHandle;
    }

    internal int FramebufferHandle { get; }

    public override void Dispose() => BrowserGraphicsInterop.DeleteFramebuffer(_contextHandle, FramebufferHandle);
}

internal sealed class BrowserRenderMesh : RenderMesh
{
    private readonly int _contextHandle;

    public BrowserRenderMesh(int contextHandle, int meshHandle)
    {
        _contextHandle = contextHandle;
        MeshHandle = meshHandle;
    }

    internal int MeshHandle { get; }

    public override void Draw(RenderProgram program, RenderTarget? target = null)
    {
        BrowserGraphicsInterop.DrawMesh(
            _contextHandle,
            MeshHandle,
            ((BrowserRenderProgram)program).ProgramHandle,
            target is BrowserRenderTarget browserTarget ? browserTarget.FramebufferHandle : 0);
    }

    public override void Dispose() => BrowserGraphicsInterop.DeleteMesh(_contextHandle, MeshHandle);
}

internal sealed class BrowserRenderTextureBuilder : RenderTextureBuilder
{
    private readonly int _contextHandle;
    private int _width;
    private int _height;
    private RenderTextureFilter _filter;

    public BrowserRenderTextureBuilder(int contextHandle)
    {
        _contextHandle = contextHandle;
        _filter = RenderTextureFilter.Nearest;
    }

    public override RenderTextureBuilder WithSize(int width, int height)
    {
        _width = width;
        _height = height;
        return this;
    }

    public override RenderTextureBuilder WithRgba8() => this;

    public override RenderTextureBuilder WithFilter(RenderTextureFilter filter)
    {
        _filter = filter;
        return this;
    }

    public override RenderTexture UploadRgba(byte[] pixels)
    {
        int handle = BrowserGraphicsInterop.CreateTexture(_contextHandle, _width, _height, _filter == RenderTextureFilter.Linear, pixels);
        return new BrowserRenderTexture(_contextHandle, handle, _width, _height);
    }

    public override RenderTexture UploadEmpty()
    {
        int handle = BrowserGraphicsInterop.CreateTexture(_contextHandle, _width, _height, _filter == RenderTextureFilter.Linear, null);
        return new BrowserRenderTexture(_contextHandle, handle, _width, _height);
    }
}

internal sealed class BrowserRenderTargetBuilder : RenderTargetBuilder
{
    private readonly int _contextHandle;
    private BrowserRenderTexture? _texture;

    public BrowserRenderTargetBuilder(int contextHandle)
    {
        _contextHandle = contextHandle;
    }

    public override RenderTargetBuilder WithColorAttachment(RenderTexture texture)
    {
        _texture = (BrowserRenderTexture)texture;
        return this;
    }

    public override RenderTarget Upload()
    {
        int handle = BrowserGraphicsInterop.CreateFramebuffer(_contextHandle, _texture?.TextureHandle ?? 0);
        return new BrowserRenderTarget(_contextHandle, handle);
    }
}

internal sealed class BrowserRenderMeshBuilder : RenderMeshBuilder
{
    private readonly int _contextHandle;

    public BrowserRenderMeshBuilder(int contextHandle)
    {
        _contextHandle = contextHandle;
    }

    public override RenderMesh Upload(RenderMeshData data, int[] indices)
    {
        string vertices = string.Join(',', data.ToInterleavedArray());
        string indexList = string.Join(',', indices);
        int handle = BrowserGraphicsInterop.CreateMesh(_contextHandle, vertices, indexList);
        return new BrowserRenderMesh(_contextHandle, handle);
    }
}
