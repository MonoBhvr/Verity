using Irodori.Buffer;

namespace Verity.Graphics;

public abstract class RenderMeshBuilder
{
    public abstract RenderMesh Upload(RenderMeshData data, int[] indices);
}

internal sealed class NativeRenderMeshBuilder : RenderMeshBuilder
{
    public NativeRenderMeshBuilder(VertexBuffer.Unuploaded resource)
    {
        Resource = resource;
    }

    internal VertexBuffer.Unuploaded Resource { get; }

    public override RenderMesh Upload(RenderMeshData data, int[] indices)
    {
        var vertexData = IVertexData.Create<System.Numerics.Vector2, System.Numerics.Vector2>();
        ReadOnlySpan<float> vertices = data.VertexData;
        for (int i = 0; i < vertices.Length; i += 4)
        {
            vertexData.AddVertex(
                new System.Numerics.Vector2(vertices[i], vertices[i + 1]),
                new System.Numerics.Vector2(vertices[i + 2], vertices[i + 3]));
        }

        return new NativeRenderMesh(Resource.Upload(vertexData, indices).Unwrap());
    }
}
