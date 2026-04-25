using System.Numerics;
using Irodori.Buffer;
using Irodori.Framebuffer;
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
out vec2 vWorldPosition;

void main()
{
    vTexCoord = mix(uUvMin, uUvMax, aTexCoord);
    vec4 world = uModel * vec4(aPosition, 0.0, 1.0);
    vWorldPosition = world.xy;
    gl_Position = uProjection * uView * world;
}
";

    private const string FragmentSource = @"#version 330 core
in vec2 vTexCoord;
in vec2 vWorldPosition;

uniform sampler2D uTexture;
uniform vec4 uColor;
uniform float uLightingEnabled;
uniform vec3 uAmbientLight;
uniform float uLightCount;
uniform float uOccluderCount;
uniform float uOccluderVertexCount;

#define MAX_LIGHTS 16
#define MAX_OCCLUDERS 24
#define MAX_OCCLUDER_VERTICES 384
#define MAX_OCCLUDER_VERTICES_PER_POLYGON 48
#define LIGHT_DIRECTION 0.0
#define LIGHT_SPOT 1.0
#define LIGHT_WORLD 2.0
#define LIGHT_SPRITE 3.0
#define LIGHT_SOFT 0.0
#define LIGHT_HARD 1.0

uniform vec4 uLightMeta1[MAX_LIGHTS];
uniform vec4 uLightMeta2[MAX_LIGHTS];
uniform vec4 uLightMeta3[MAX_LIGHTS];
uniform vec4 uLightMeta4[MAX_LIGHTS];
uniform vec4 uLightMeta5[MAX_LIGHTS];
uniform vec4 uOccluderMeta[MAX_OCCLUDERS];
uniform vec4 uOccluderVertices[MAX_OCCLUDER_VERTICES];

out vec4 FragColor;

float saturate(float value)
{
    return clamp(value, 0.0, 1.0);
}

float evaluateHard(float normalizedDistance, float smoothness)
{
    float feather = clamp(smoothness, 0.0, 1.0);
    if (feather <= 0.0001)
    {
        return normalizedDistance <= 1.0 ? 1.0 : 0.0;
    }

    float inner = max(0.0, 1.0 - feather);
    return 1.0 - smoothstep(inner, 1.0, saturate(normalizedDistance));
}

float evaluateSoft(float normalizedDistance)
{
    return 1.0 - smoothstep(0.0, 1.0, saturate(normalizedDistance));
}

float evaluateFalloff(float normalizedDistance, float falloffMode, float smoothness)
{
    return falloffMode >= LIGHT_HARD
        ? evaluateHard(normalizedDistance, smoothness)
        : evaluateSoft(normalizedDistance);
}

float evaluateSpotLight(vec2 fragmentPosition, vec2 lightPosition, float lightDistance, float falloffMode, float smoothness)
{
    float normalizedDistance = distance(fragmentPosition, lightPosition) / max(lightDistance, 0.0001);
    return evaluateFalloff(normalizedDistance, falloffMode, smoothness);
}

float evaluateDirectionalLight(vec2 fragmentPosition, vec2 lightPosition, vec2 direction, float lightDistance, float spreadRadians, float falloffMode, float smoothness)
{
    vec2 delta = fragmentPosition - lightPosition;
    float forwardDistance = dot(delta, direction);
    if (forwardDistance <= 0.0 || forwardDistance > lightDistance)
    {
        return 0.0;
    }

    float halfWidth = max(0.0001, forwardDistance * tan(max(0.0174533, spreadRadians) * 0.5));
    float lateralDistance = length(delta - (direction * forwardDistance));
    float lateralNormalized = lateralDistance / halfWidth;
    float forwardNormalized = forwardDistance / max(lightDistance, 0.0001);
    return evaluateFalloff(forwardNormalized, falloffMode, smoothness) * evaluateFalloff(lateralNormalized, falloffMode, smoothness);
}

float evaluateSpriteLight(vec2 fragmentPosition, vec2 lightPosition, vec4 axis, vec2 halfSize, float falloffMode, float smoothness)
{
    vec2 delta = fragmentPosition - lightPosition;
    vec2 local = vec2(dot(delta, axis.xy), dot(delta, axis.zw));
    vec2 normalized = abs(local) / max(halfSize, vec2(0.0001));
    float edgeDistance = max(normalized.x, normalized.y);
    return evaluateFalloff(edgeDistance, falloffMode, smoothness);
}

float cross2d(vec2 a, vec2 b)
{
    return (a.x * b.y) - (a.y * b.x);
}

bool segmentIntersectsSegment(vec2 p1, vec2 p2, vec2 q1, vec2 q2)
{
    vec2 r = p2 - p1;
    vec2 s = q2 - q1;
    float denominator = cross2d(r, s);
    if (abs(denominator) < 0.0001)
    {
        return false;
    }

    vec2 qp = q1 - p1;
    float t = cross2d(qp, s) / denominator;
    float u = cross2d(qp, r) / denominator;
    return t >= 0.0 && t < 0.9995 && u >= 0.0 && u <= 1.0;
}

bool pointInPolygon(vec2 point, int startIndex, int vertexCount)
{
    bool inside = false;
    for (int i = 0; i < MAX_OCCLUDER_VERTICES_PER_POLYGON; i++)
    {
        if (i >= vertexCount)
        {
            break;
        }

        int currentIndex = startIndex + i;
        int nextIndex = startIndex + ((i + 1) % vertexCount);
        vec2 a = uOccluderVertices[currentIndex].xy;
        vec2 b = uOccluderVertices[nextIndex].xy;
        float denominator = b.y - a.y;
        if (abs(denominator) < 0.0001)
        {
            denominator = denominator < 0.0 ? -0.0001 : 0.0001;
        }

        bool intersects = ((a.y > point.y) != (b.y > point.y)) &&
            (point.x < ((b.x - a.x) * (point.y - a.y) / denominator) + a.x);
        if (intersects)
        {
            inside = !inside;
        }
    }
    return inside;
}

bool segmentIntersectsPolygon(vec2 startPoint, vec2 endPoint, int startIndex, int vertexCount)
{
    if (pointInPolygon(startPoint, startIndex, vertexCount) || pointInPolygon(endPoint, startIndex, vertexCount))
    {
        return true;
    }

    for (int i = 0; i < MAX_OCCLUDER_VERTICES_PER_POLYGON; i++)
    {
        if (i >= vertexCount)
        {
            break;
        }

        int currentIndex = startIndex + i;
        int nextIndex = startIndex + ((i + 1) % vertexCount);
        if (segmentIntersectsSegment(startPoint, endPoint, uOccluderVertices[currentIndex].xy, uOccluderVertices[nextIndex].xy))
        {
            return true;
        }
    }

    return false;
}

float evaluateShadow(vec2 fragmentPosition, vec2 lightPosition, float shadowStrength)
{
    if (shadowStrength <= 0.0001)
    {
        return 1.0;
    }

    // Avoid treating rays that only graze the caster at the fragment endpoint as blocked.
    vec2 rayEnd = mix(fragmentPosition, lightPosition, 0.0005);

    for (int i = 0; i < MAX_OCCLUDERS; i++)
    {
        if (float(i) >= uOccluderCount)
        {
            break;
        }

        vec4 meta = uOccluderMeta[i];
        int startIndex = int(meta.x);
        int vertexCount = int(meta.y);
        if (vertexCount < 3 || startIndex < 0 || (startIndex + vertexCount) > int(uOccluderVertexCount))
        {
            continue;
        }

        // An occluder that contains the light source should not cast a shadow for that light.
        // This happens when the light is attached to a sprite or shape and that owner is also collected as an occluder.
        if (pointInPolygon(lightPosition, startIndex, vertexCount))
        {
            continue;
        }

        if (segmentIntersectsPolygon(lightPosition, rayEnd, startIndex, vertexCount))
        {
            return 1.0 - shadowStrength;
        }
    }

    return 1.0;
}

void main()
{
    vec4 baseColor = texture(uTexture, vTexCoord) * uColor;
    if (uLightingEnabled < 0.5)
    {
        FragColor = baseColor;
        return;
    }

    vec3 lighting = uAmbientLight;
    for (int i = 0; i < MAX_LIGHTS; i++)
    {
        if (float(i) >= uLightCount)
        {
            break;
        }

        vec4 meta1 = uLightMeta1[i];
        vec4 meta2 = uLightMeta2[i];
        vec4 meta3 = uLightMeta3[i];
        vec4 meta4 = uLightMeta4[i];
        vec4 meta5 = uLightMeta5[i];

        vec2 lightPosition = meta1.xy;
        float lightDistance = meta1.z;
        float lightIntensity = meta1.w;
        vec2 lightDirection = normalize(meta2.xy);
        float spreadRadians = meta2.z;
        float smoothness = meta2.w;
        vec3 lightColor = meta3.rgb;
        float lightType = meta3.a;
        vec4 spriteAxis = meta4;
        vec2 halfSize = meta5.xy;
        float falloffMode = meta5.z;
        float shadowStrength = meta5.w;

        float contribution = 0.0;
        if (lightType == LIGHT_SPOT)
        {
            contribution = evaluateSpotLight(vWorldPosition, lightPosition, lightDistance, falloffMode, smoothness);
        }
        else if (lightType == LIGHT_DIRECTION)
        {
            contribution = evaluateDirectionalLight(vWorldPosition, lightPosition, lightDirection, lightDistance, spreadRadians, falloffMode, smoothness);
        }
        else if (lightType == LIGHT_SPRITE)
        {
            contribution = evaluateSpriteLight(vWorldPosition, lightPosition, spriteAxis, halfSize, falloffMode, smoothness);
        }

        if (contribution > 0.0 && (lightType == LIGHT_SPOT || lightType == LIGHT_DIRECTION))
        {
            contribution *= evaluateShadow(vWorldPosition, lightPosition, shadowStrength);
        }

        lighting += lightColor * (contribution * lightIntensity);
    }

    FragColor = vec4(baseColor.rgb * clamp(lighting, vec3(0.0), vec3(1.0)), baseColor.a);
}
";

    private readonly RenderProgram _program;

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

    private Shader2D(RenderProgram program)
    {
        _program = program;
    }

    public static Shader2D Create(IRenderDevice device, string? vertexSource = null, string? fragmentSource = null)
    {
        return new Shader2D(device.CreateProgram(vertexSource ?? VertexSource, fragmentSource ?? FragmentSource));
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

    public void SetTexture(RenderTexture texture)
    {
        _program.SetTexture("uTexture", texture);
    }

    public void SetTexture(string name, RenderTexture texture)
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

    public void Draw(RenderMesh buffer, RenderTarget? targetFbo = null)
    {
        buffer.Draw(_program, targetFbo);
    }

    public void Dispose()
    {
        _program.Dispose();
    }
}
