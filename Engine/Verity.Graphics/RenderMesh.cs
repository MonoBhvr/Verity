using Irodori.Buffer;

namespace Verity.Graphics;

public abstract class RenderMesh : IDisposable
{
    public abstract void Draw(RenderProgram program, RenderTarget? target = null);
    public abstract void Dispose();
}

internal sealed class NativeRenderMesh : RenderMesh
{
    public NativeRenderMesh(VertexBuffer.Uploaded resource)
    {
        Resource = resource;
    }

    internal VertexBuffer.Uploaded Resource { get; }

    public override void Draw(RenderProgram program, RenderTarget? target = null)
    {
        Resource.Draw(((NativeRenderProgram)program).Resource, (target as NativeRenderTarget)?.Resource).Unwrap();
    }

    public override void Dispose() => Resource.Dispose();
}
