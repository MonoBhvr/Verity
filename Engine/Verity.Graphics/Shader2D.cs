using System.Numerics;
using Irodori.Buffer;
using Irodori.Shader;
using Irodori.Texture;

namespace Verity.Graphics;

public class Shader2D : IDisposable
{
    private const string VertexSource = @"#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

uniform mat4 uProjection;
uniform mat4 uView;
uniform mat4 uModel;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = uProjection * uView * uModel * vec4(aPosition, 0.0, 1.0);
}
";

    private const string FragmentSource = @"#version 330 core
in vec2 vTexCoord;

uniform sampler2D uTexture;
uniform vec4 uColor;

out vec4 FragColor;

void main()
{
    FragColor = texture(uTexture, vTexCoord) * uColor;
}
";

    private readonly ShaderProgram.Linked _program;
    private readonly VertexBuffer.Uploaded _quadBuffer;

    public ShaderProgram.Linked Program => _program;
    public VertexBuffer.Uploaded QuadBuffer => _quadBuffer;

    private Shader2D(ShaderProgram.Linked program, VertexBuffer.Uploaded quadBuffer)
    {
        _program = program;
        _quadBuffer = quadBuffer;
    }

    public static Shader2D Create(GraphicsDevice device)
    {
        var vertexShader = device.CreateShader(EShaderType.Vertex, VertexSource)
            .Compile()
            .Unwrap();

        var fragmentShader = device.CreateShader(EShaderType.Fragment, FragmentSource)
            .Compile()
            .Unwrap();

        var program = device.CreateShaderProgram()
            .AttachShader(vertexShader)
            .AttachShader(fragmentShader)
            .Link()
            .Unwrap();

        vertexShader.Dispose();
        fragmentShader.Dispose();

        var quadBuffer = CreateQuadBuffer(device);

        return new Shader2D(program, quadBuffer);
    }

    // Unit quad: [0,0] to [1,1] — model matrix handles position, scale, rotation
    private static VertexBuffer.Uploaded CreateQuadBuffer(GraphicsDevice device)
    {
        var format = VertexBufferFormat.Create()
            .AddAttrib(VertexBufferFormat.Attrib.Vector2())  // aPosition
            .AddAttrib(VertexBufferFormat.Attrib.Vector2()); // aTexCoord

        var data = IVertexData.Create<Vector2, Vector2>();
        //   0---1
        //   | / |
        //   2---3
        data.AddVertex(new Vector2(0, 0), new Vector2(0, 0)); // top-left
        data.AddVertex(new Vector2(1, 0), new Vector2(1, 0)); // top-right
        data.AddVertex(new Vector2(0, 1), new Vector2(0, 1)); // bottom-left
        data.AddVertex(new Vector2(1, 1), new Vector2(1, 1)); // bottom-right

        var indices = new int[] { 0, 2, 1, 1, 2, 3 };

        var buffer = device.CreateVertexBuffer(format);
        return buffer.Upload(data, indices).Unwrap();
    }

    public void SetProjection(Matrix4x4 projection)
    {
        _program.SetMat4("uProjection", projection);
    }

    public void SetView(Matrix4x4 view)
    {
        _program.SetMat4("uView", view);
    }

    public void SetModel(Matrix4x4 model)
    {
        _program.SetMat4("uModel", model);
    }

    public void SetTexture(TextureObjectUploaded texture)
    {
        _program.SetTexture("uTexture", texture);
    }

    public void SetColor(Verity.Core.Color color)
    {
        _program.SetVec4("uColor", color);
    }

    public void Dispose()
    {
        _program.Dispose();
        _quadBuffer.Dispose();
    }
}
