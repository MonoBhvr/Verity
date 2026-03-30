namespace Verity.Graphics;

public static class PostProcessShaders
{
    public const string CopyFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;

void main()
{
    FragColor = texture(uTexture, clamp(vTexCoord, 0.0, 1.0));
}
";

    public const string BrightExtractFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uThreshold;

void main()
{
    vec4 color = texture(uTexture, vTexCoord);
    float brightness = dot(color.rgb, vec3(0.2126, 0.7152, 0.0722));
    float knee = max(uThreshold * 0.5, 0.0001);
    float soft = clamp(brightness - uThreshold + knee, 0.0, 2.0 * knee);
    soft = (soft * soft) / max(4.0 * knee, 0.0001);
    float contribution = max(brightness - uThreshold, soft) / max(brightness, 0.0001);
    FragColor = vec4(max(color.rgb * contribution, vec3(0.0)), 1.0);
}
";

    public const string BlurFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec2 uDirection;
uniform float uRadius;
uniform float uWeight[5] = float[] (0.227027, 0.1945946, 0.1216216, 0.054054, 0.016216);

void main()
{             
    vec2 tex_offset = (1.0 / textureSize(uTexture, 0)) * max(uRadius, 0.001);
    vec3 result = texture(uTexture, vTexCoord).rgb * uWeight[0];

    for(int i = 1; i < 5; ++i)
    {
        vec2 offset = tex_offset * uDirection * float(i);
        result += texture(uTexture, vTexCoord + offset).rgb * uWeight[i];
        result += texture(uTexture, vTexCoord - offset).rgb * uWeight[i];
    }

    FragColor = vec4(result, 1.0);
}
";

    public const string BloomCombineFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uBloomBlur;
uniform float uBloomIntensity;

void main()
{
    vec4 sceneColor = texture(uScene, vTexCoord);
    vec3 bloomColor = texture(uBloomBlur, vTexCoord).rgb;
    float intensity = max(uBloomIntensity, 0.0);
    vec3 combined = sceneColor.rgb + bloomColor * intensity;
    FragColor = vec4(max(combined, vec3(0.0)), sceneColor.a);
}
";

    public const string ColorAdjustFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uExposure;
uniform float uContrast;
uniform float uSaturation;
uniform vec4 uTint;

void main()
{
    vec4 sampleColor = texture(uTexture, vTexCoord);
    vec3 color = sampleColor.rgb * max(uTint.rgb, vec3(0.0));
    color *= exp2(uExposure);
    color = (color - 0.5) * uContrast + 0.5;
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    color = mix(vec3(luma), color, max(uSaturation, 0.0));
    FragColor = vec4(max(color, 0.0), sampleColor.a);
}
";

    public const string MotionBlurFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform sampler2D uHistory;
uniform float uMotionBlurIntensity;
uniform float uHasHistory;

void main()
{
    vec4 currentColor = texture(uTexture, vTexCoord);
    if (uHasHistory < 0.5)
    {
        FragColor = currentColor;
        return;
    }

    vec3 historyColor = texture(uHistory, vTexCoord).rgb;
    vec3 result = mix(currentColor.rgb, historyColor, clamp(uMotionBlurIntensity, 0.0, 0.95));
    FragColor = vec4(result, currentColor.a);
}
";

    public const string DistortionFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec2 uResolution;
uniform float uDistortionIntensity;
uniform vec2 uDistortionCenter;
uniform float uDistortionScale;

void main()
{
    vec2 centered = (vTexCoord - uDistortionCenter) * 2.0;
    vec2 aspect = vec2(uResolution.x / max(uResolution.y, 1.0), 1.0);
    vec2 radial = centered * aspect;
    float radius2 = dot(radial, radial);
    centered *= 1.0 + uDistortionIntensity * radius2;
    centered *= max(uDistortionScale, 0.001);
    vec2 uv = clamp(centered * 0.5 + uDistortionCenter, 0.0, 1.0);
    FragColor = texture(uTexture, uv);
}
";

    public const string PixelateFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec2 uPixelateResolution;

void main()
{
    vec2 pixelResolution = max(uPixelateResolution, vec2(1.0));
    vec2 uv = (floor(vTexCoord * pixelResolution) + 0.5) / pixelResolution;
    FragColor = texture(uTexture, uv);
}
";

    public const string ChromaticAberrationFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uChromaticAberrationIntensity;
uniform vec2 uChromaticAberrationCenter;

void main()
{
    vec2 direction = vTexCoord - uChromaticAberrationCenter;
    float distanceFromCenter = length(direction);
    vec2 offset = direction * (uChromaticAberrationIntensity * distanceFromCenter);

    float r = texture(uTexture, clamp(vTexCoord + offset, 0.0, 1.0)).r;
    float g = texture(uTexture, clamp(vTexCoord, 0.0, 1.0)).g;
    float b = texture(uTexture, clamp(vTexCoord - offset, 0.0, 1.0)).b;
    float a = texture(uTexture, clamp(vTexCoord, 0.0, 1.0)).a;
    FragColor = vec4(r, g, b, a);
}
";

    public const string VignetteFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform float uVignetteIntensity;
uniform float uVignetteSmoothness;
uniform float uVignetteRoundness;
uniform vec4 uVignetteColor;

void main()
{
    vec4 sampleColor = texture(uTexture, vTexCoord);
    vec2 vignetteUv = vTexCoord * 2.0 - 1.0;
    vignetteUv = sign(vignetteUv) * pow(abs(vignetteUv), vec2(max(uVignetteRoundness, 0.001)));
    float edge = length(vignetteUv);
    float mask = smoothstep(1.0 - clamp(uVignetteSmoothness, 0.001, 1.0), 1.0, edge);
    vec3 result = mix(sampleColor.rgb, uVignetteColor.rgb, mask * clamp(uVignetteIntensity, 0.0, 1.0));
    FragColor = vec4(result, sampleColor.a);
}
";

    public const string CompositeFragment = @"#version 330 core
in vec2 vTexCoord;
out vec4 FragColor;

uniform sampler2D uScene;
uniform sampler2D uBloomBlur;
uniform sampler2D uHistory;
uniform vec2 uResolution;
uniform float uTime;

uniform float uBloomEnabled;
uniform float uBloomIntensity;

uniform float uVignetteEnabled;
uniform float uVignetteIntensity;
uniform float uVignetteSmoothness;
uniform float uVignetteRoundness;
uniform vec4 uVignetteColor;

uniform float uColorAdjustEnabled;
uniform float uExposure;
uniform float uContrast;
uniform float uSaturation;
uniform vec4 uTint;

uniform float uMotionBlurEnabled;
uniform float uMotionBlurIntensity;
uniform float uHasHistory;

uniform float uDistortionEnabled;
uniform float uDistortionIntensity;
uniform vec2 uDistortionCenter;
uniform float uDistortionScale;

uniform float uPixelateEnabled;
uniform vec2 uPixelateResolution;

uniform float uChromaticAberrationEnabled;
uniform float uChromaticAberrationIntensity;
uniform vec2 uChromaticAberrationCenter;

vec3 applyColorAdjustments(vec3 color)
{
    color *= max(uTint.rgb, vec3(0.0));
    color *= exp2(uExposure);
    color = (color - 0.5) * uContrast + 0.5;
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    return mix(vec3(luma), color, max(uSaturation, 0.0));
}

vec2 applyScreenDistortion(vec2 uv)
{
    vec2 centered = (uv - uDistortionCenter) * 2.0;
    vec2 aspect = vec2(uResolution.x / max(uResolution.y, 1.0), 1.0);
    vec2 radial = centered * aspect;
    float radius2 = dot(radial, radial);
    centered *= 1.0 + uDistortionIntensity * radius2;
    centered *= max(uDistortionScale, 0.001);
    return centered * 0.5 + uDistortionCenter;
}

vec2 applyPixelate(vec2 uv)
{
    if (uPixelateEnabled < 0.5)
        return uv;

    vec2 pixelResolution = max(uPixelateResolution, vec2(1.0));
    return (floor(uv * pixelResolution) + 0.5) / pixelResolution;
}

vec3 sampleSceneChromatic(vec2 uv)
{
    if (uChromaticAberrationEnabled < 0.5)
        return texture(uScene, uv).rgb;

    vec2 direction = uv - uChromaticAberrationCenter;
    float distanceFromCenter = length(direction);
    vec2 offset = direction * (uChromaticAberrationIntensity * distanceFromCenter);

    float r = texture(uScene, clamp(uv + offset, 0.0, 1.0)).r;
    float g = texture(uScene, clamp(uv, 0.0, 1.0)).g;
    float b = texture(uScene, clamp(uv - offset, 0.0, 1.0)).b;
    return vec3(r, g, b);
}

void main()
{             
    vec2 uv = vTexCoord;

    if (uDistortionEnabled > 0.5)
    {
        uv = applyScreenDistortion(uv);
        uv = clamp(uv, 0.0, 1.0);
    }

    uv = applyPixelate(uv);

    vec4 sceneSample = texture(uScene, uv);
    vec3 result = sampleSceneChromatic(uv);

    if (uBloomEnabled > 0.5)
    {
        vec3 bloomColor = texture(uBloomBlur, uv).rgb;
        result += bloomColor * uBloomIntensity;
    }

    if (uMotionBlurEnabled > 0.5 && uHasHistory > 0.5)
    {
        vec3 historyColor = texture(uHistory, vTexCoord).rgb;
        result = mix(result, historyColor, clamp(uMotionBlurIntensity, 0.0, 0.95));
    }

    if (uColorAdjustEnabled > 0.5)
    {
        result = applyColorAdjustments(result);
    }

    if(uVignetteEnabled > 0.5)
    {
        vec2 vignetteUv = vTexCoord * 2.0 - 1.0;
        vignetteUv = sign(vignetteUv) * pow(abs(vignetteUv), vec2(max(uVignetteRoundness, 0.001)));
        float edge = length(vignetteUv);
        float mask = smoothstep(1.0 - clamp(uVignetteSmoothness, 0.001, 1.0), 1.0, edge);
        result = mix(result, uVignetteColor.rgb, mask * clamp(uVignetteIntensity, 0.0, 1.0));
    }

    FragColor = vec4(max(result, 0.0), sceneSample.a);
}
";

    public const string ScreenVertex = @"#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

out vec2 vTexCoord;

void main()
{
    vTexCoord = aTexCoord;
    gl_Position = vec4(aPosition.x * 2.0 - 1.0, aPosition.y * 2.0 - 1.0, 0.0, 1.0);
}
";
}
