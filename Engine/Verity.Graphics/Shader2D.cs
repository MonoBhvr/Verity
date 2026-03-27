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
uniform vec2 uUvMin;
uniform vec2 uUvMax;

out vec2 vTexCoord;

void main()
{
    vTexCoord = mix(uUvMin, uUvMax, aTexCoord);
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

    public ShaderProgram.Linked Program => _program;

    public struct ShaderUniform
    {
        public string Type;
        public string Name;
    }

    public static List<ShaderUniform> ParseUniforms(string source)
    {
        var uniforms = new List<ShaderUniform>();
        if (string.IsNullOrWhiteSpace(source)) return uniforms;

        var matches = System.Text.RegularExpressions.Regex.Matches(source, @"uniform\s+(\w+)\s+(\w+)\s*;");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            uniforms.Add(new ShaderUniform { Type = match.Groups[1].Value, Name = match.Groups[2].Value });
        }
        return uniforms;
    }

    private Shader2D(ShaderProgram.Linked program)
    {
        _program = program;
    }

    public static Shader2D Create(GraphicsDevice device, string? vertexSource = null, string? fragmentSource = null)
    {
        var vertexShader = device.CreateShader(EShaderType.Vertex, vertexSource ?? VertexSource)
            .Compile()
            .Unwrap();

        var fragmentShader = device.CreateShader(EShaderType.Fragment, fragmentSource ?? FragmentSource)
            .Compile()
            .Unwrap();

        var program = device.CreateShaderProgram()
            .AttachShader(vertexShader)
            .AttachShader(fragmentShader)
            .Link()
            .Unwrap();

        vertexShader.Dispose();
        fragmentShader.Dispose();

        return new Shader2D(program);
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

    public void SetTexture(string name, TextureObjectUploaded texture)
    {
        _program.SetTexture(name, texture);
    }

    public void SetUvRect(Vector2 min, Vector2 max)
    {
        try
        {
            _program.SetVec2("uUvMin", min);
            _program.SetVec2("uUvMax", max);
        }
        catch
        {
        }
    }

    public void SetColor(Verity.Core.Color color)
    {
        _program.SetVec4("uColor", color);
    }

    public void SetColor(string name, Verity.Core.Color value)
    {
        _program.SetVec4(name, value);
    }
    public void SetFloat(string name, float value) => _program.SetFloat(name, value);
    public void SetVec2(string name, Vector2 value) => _program.SetVec2(name, value);
    public void SetVec3(string name, Vector3 value) => _program.SetVec3(name, value);
    public void SetVec4(string name, Vector4 value) => _program.SetVec4(name, value);
    public void SetMat4(string name, Matrix4x4 value) => _program.SetMat4(name, value);

    public void Dispose()
    {
        _program.Dispose();
    }
}
