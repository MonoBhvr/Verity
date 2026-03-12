using System.Numerics;
using Irodori.Texture;
using Verity.Core;

namespace Verity.Graphics;

public class StyleRuntime
{
    public Shader2D? Shader { get; set; }

    public Dictionary<string, float> Floats { get; } = new();
    public Dictionary<string, Vector2> Vector2s { get; } = new();
    public Dictionary<string, Vector3> Vector3s { get; } = new();
    public Dictionary<string, Vector4> Vector4s { get; } = new();
    public Dictionary<string, Color> Colors { get; } = new();
    public Dictionary<string, TextureObjectUploaded> Textures { get; } = new();

    public void Apply(Shader2D shader)
    {
        foreach (var (name, val) in Floats) shader.Program.SetFloat(name, val);
        foreach (var (name, val) in Vector2s) shader.Program.SetVec2(name, val);
        foreach (var (name, val) in Vector3s) shader.Program.SetVec3(name, val);
        foreach (var (name, val) in Vector4s) shader.Program.SetVec4(name, val);
        foreach (var (name, val) in Colors) shader.Program.SetVec4(name, val);
        foreach (var (name, val) in Textures) shader.Program.SetTexture(name, val);
    }
}
