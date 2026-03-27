using System.Numerics;
using Verity.Core.World;

namespace Verity.Core.Animation;

public enum WrapMode
{
    Once,
    Loop,
    PingPong,
    ClampForever
}

public class Keyframe
{
    public float Time { get; set; }
    public object Value { get; set; } = null!;
    
    // Tangents for cubic interpolation (future proofing)
    public float InTangent { get; set; }
    public float OutTangent { get; set; }

    public Keyframe() { }
    public Keyframe(float time, object value) { Time = time; Value = value; }
}

public class AnimationTrack
{
    // Path to the component/property
    // e.g. "Transform.Position", "SpriteRenderer.Color"
    public string Path { get; set; } = "";
    
    // The type of the value (for deserialization hints)
    // e.g. "System.Single", "System.Numerics.Vector2"
    public string TypeName { get; set; } = "";

    public List<Keyframe> Keyframes { get; set; } = new();

    public void SortKeyframes()
    {
        Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    // Helper to evaluate at time t
    public object Evaluate(float time)
    {
        if (Keyframes.Count == 0) return null!;

        SortKeyframes();

        if (time <= Keyframes[0].Time) return Keyframes[0].Value;
        if (time >= Keyframes[^1].Time) return Keyframes[^1].Value;

        // Find index
        int index = 0;
        for (int i = 0; i < Keyframes.Count - 1; i++)
        {
            if (time >= Keyframes[i].Time && time < Keyframes[i+1].Time)
            {
                index = i;
                break;
            }
        }

        var k1 = Keyframes[index];
        var k2 = Keyframes[index + 1];

        float t = (time - k1.Time) / (k2.Time - k1.Time);

        return Interpolate(k1.Value, k2.Value, t);
    }

    private object Interpolate(object v1, object v2, float t)
    {
        if (!AnimationTypeUtility.IsInterpolatedType(v1.GetType()))
            return v1;

        if (v1 is float f1 && v2 is float f2) return f1 + (f2 - f1) * t;
        if (v1 is int i1 && v2 is int i2) return (int)(i1 + (i2 - i1) * t);
        if (v1 is Verity.Core.Vector2 coreVec2_1 && v2 is Verity.Core.Vector2 coreVec2_2) return Verity.Core.Vector2.Lerp(coreVec2_1, coreVec2_2, t);
        if (v1 is System.Numerics.Vector2 vec2_1 && v2 is System.Numerics.Vector2 vec2_2) return System.Numerics.Vector2.Lerp(vec2_1, vec2_2, t);
        if (v1 is Verity.Core.Vector3 coreVec3_1 && v2 is Verity.Core.Vector3 coreVec3_2) return Verity.Core.Vector3.Lerp(coreVec3_1, coreVec3_2, t);
        if (v1 is System.Numerics.Vector3 vec3_1 && v2 is System.Numerics.Vector3 vec3_2) return System.Numerics.Vector3.Lerp(vec3_1, vec3_2, t);
        if (v1 is Vector4 vec4_1 && v2 is Vector4 vec4_2) return Vector4.Lerp(vec4_1, vec4_2, t);
        if (v1 is Color c1 && v2 is Color c2)
        {
            return new Color(
                c1.R + (c2.R - c1.R) * t,
                c1.G + (c2.G - c1.G) * t,
                c1.B + (c2.B - c1.B) * t,
                c1.A + (c2.A - c1.A) * t
            );
        }
        
        // Bool, String, Sprite, Enum, etc. -> Stepped interpolation
        return v1; 
    }
}
