using System.Numerics;
using System.Runtime.InteropServices;

namespace Verity.Graphics;

public sealed class RenderMeshData
{
    private readonly List<float> _vertexData = [];

    private RenderMeshData()
    {
    }

    public int VertexCount => _vertexData.Count / 4;

    internal ReadOnlySpan<float> VertexData => CollectionsMarshal.AsSpan(_vertexData);

    public float[] ToInterleavedArray() => _vertexData.ToArray();

    public static RenderMeshData CreatePositionTexture2D()
    {
        return new RenderMeshData();
    }

    public void AddVertex(Vector2 position, Vector2 texCoord)
    {
        _vertexData.Add(position.X);
        _vertexData.Add(position.Y);
        _vertexData.Add(texCoord.X);
        _vertexData.Add(texCoord.Y);
    }
}
