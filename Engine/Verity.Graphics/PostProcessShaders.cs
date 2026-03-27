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
    if(brightness > uThreshold)
        FragColor = vec4(color.rgb, 1.0);
    else
        FragColor = vec4(0.0, 0.0, 0.0, 1.0);
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
uniform float uDistortionSpeed;
uniform float uDistortionFrequency;
uniform vec2 uDistortionCenter;

vec3 applyColorAdjustments(vec3 color)
{
    color *= max(uTint.rgb, vec3(0.0));
    color *= exp2(uExposure);
    color = (color - 0.5) * uContrast + 0.5;
    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
    return mix(vec3(luma), color, max(uSaturation, 0.0));
}

void main()
{             
    vec2 uv = vTexCoord;

    if (uDistortionEnabled > 0.5)
    {
        vec2 centered = uv - uDistortionCenter;
        float radial = length(centered);
        float waveX = sin((uv.y + uTime * uDistortionSpeed) * uDistortionFrequency);
        float waveY = cos((uv.x - uTime * uDistortionSpeed * 0.73) * (uDistortionFrequency * 0.83));
        float attenuation = clamp(1.0 - radial, 0.0, 1.0);
        uv += vec2(waveX, waveY) * uDistortionIntensity * attenuation;
        uv = clamp(uv, 0.0, 1.0);
    }

    vec4 sceneSample = texture(uScene, uv);
    vec3 result = sceneSample.rgb;

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
