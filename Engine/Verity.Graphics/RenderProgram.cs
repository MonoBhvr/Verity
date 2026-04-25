using Irodori.Shader;

namespace Verity.Graphics;

public abstract class RenderProgram : IDisposable
{
    public abstract void SetTexture(string name, RenderTexture texture);
    public abstract void SetFloat(string name, float value);
    public abstract void SetVec2(string name, System.Numerics.Vector2 value);
    public abstract void SetVec3(string name, System.Numerics.Vector3 value);
    public abstract void SetVec4(string name, System.Numerics.Vector4 value);
    public abstract void SetMat4(string name, System.Numerics.Matrix4x4 value);
    public abstract void Dispose();
}

internal sealed class NativeRenderProgram : RenderProgram
{
    public NativeRenderProgram(ShaderProgram.Linked resource)
    {
        Resource = resource;
    }

    internal ShaderProgram.Linked Resource { get; }

    public override void SetTexture(string name, RenderTexture texture)
    {
        Resource.SetTexture(name, ((NativeRenderTexture)texture).Resource);
    }

    public override void SetFloat(string name, float value) => Resource.SetFloat(name, value);
    public override void SetVec2(string name, System.Numerics.Vector2 value) => Resource.SetVec2(name, value);
    public override void SetVec3(string name, System.Numerics.Vector3 value) => Resource.SetVec3(name, value);
    public override void SetVec4(string name, System.Numerics.Vector4 value) => Resource.SetVec4(name, value);
    public override void SetMat4(string name, System.Numerics.Matrix4x4 value) => Resource.SetMat4(name, value);
    public override void Dispose() => Resource.Dispose();
}
