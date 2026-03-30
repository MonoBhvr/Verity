using System.Numerics;
using System.Runtime.CompilerServices;

namespace Verity.Core;

/// <summary>
/// A Unity-style Vector2 struct that wraps System.Numerics.Vector2 for SIMD performance
/// while providing familiar static properties like Up, Down, Left, Right.
/// </summary>
public struct Vector2
{
    private System.Numerics.Vector2 _inner;

    public float X { get => _inner.X; set => _inner.X = value; } 
    public float Y { get => _inner.Y; set => _inner.Y = value; }
    public float x { get => _inner.X; set => _inner.X = value; }
    public float y { get => _inner.Y; set => _inner.Y = value; }    

    public Vector2(float x, float y) => _inner = new System.Numerics.Vector2(x, y);
    public Vector2(float value) => _inner = new System.Numerics.Vector2(value);
    public Vector2(System.Numerics.Vector2 inner) => _inner = inner;

    // Unity-style static properties
    public static Vector2 Up => new(0, 1);
    public static Vector2 Down => new(0, -1);
    public static Vector2 Left => new(-1, 0);
    public static Vector2 Right => new(1, 0);
    public static Vector2 Zero => new(System.Numerics.Vector2.Zero);
    public static Vector2 One => new(System.Numerics.Vector2.One);
    public static Vector2 UnitX => new(System.Numerics.Vector2.UnitX);
    public static Vector2 UnitY => new(System.Numerics.Vector2.UnitY);
    public static Vector2 zero => Zero;
    public static Vector2 one => One;
    public static Vector2 up => Up;
    public static Vector2 down => Down;
    public static Vector2 left => Left;
    public static Vector2 right => Right;

    // Implicit conversions to/from System.Numerics.Vector2
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector2(Vector2 v) => v._inner;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector2(System.Numerics.Vector2 v) => new(v);

    // Common operators
    public static Vector2 operator +(Vector2 a, Vector2 b) => a._inner + b._inner;
    public static Vector2 operator -(Vector2 a, Vector2 b) => a._inner - b._inner;
    public static Vector2 operator *(Vector2 a, Vector2 b) => a._inner * b._inner;
    public static Vector2 operator /(Vector2 a, Vector2 b) => a._inner / b._inner;
    public static Vector2 operator *(Vector2 a, float b) => a._inner * b;
    public static Vector2 operator *(float a, Vector2 b) => b._inner * a;
    public static Vector2 operator /(Vector2 a, float b) => a._inner / b;
    public static Vector2 operator -(Vector2 a) => -a._inner;
    public static bool operator ==(Vector2 a, Vector2 b) => a._inner == b._inner;
    public static bool operator !=(Vector2 a, Vector2 b) => a._inner != b._inner;

    // Utility methods
    public float Length() => _inner.Length();
    public float LengthSquared() => _inner.LengthSquared();
    public System.Numerics.Vector2 ToNumerics() => _inner;
    public static Vector2 FromNumerics(System.Numerics.Vector2 v) => new(v);
    public static float Distance(Vector2 a, Vector2 b) => System.Numerics.Vector2.Distance(a, b);
    public static float DistanceSquared(Vector2 a, Vector2 b) => System.Numerics.Vector2.DistanceSquared(a, b);
    public static Vector2 Normalize(Vector2 v) => v.LengthSquared() > 0.000001f ? System.Numerics.Vector2.Normalize(v) : Zero;
    public static float Dot(Vector2 a, Vector2 b) => System.Numerics.Vector2.Dot(a, b);
    public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => System.Numerics.Vector2.Lerp(a, b, t);
    public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => a + (b - a) * t;
    public static Vector2 Min(Vector2 a, Vector2 b) => System.Numerics.Vector2.Min(a, b);
    public static Vector2 Max(Vector2 a, Vector2 b) => System.Numerics.Vector2.Max(a, b);
    public static Vector2 Scale(Vector2 a, Vector2 b) => a * b;
    public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal) => inDirection - (2f * Dot(inDirection, inNormal) * inNormal);
    public static Vector2 Perpendicular(Vector2 inDirection) => new(-inDirection.Y, inDirection.X);
    public static Vector2 ClampMagnitude(Vector2 vector, float maxLength)
    {
        float sqrMagnitude = vector.LengthSquared();
        if (sqrMagnitude <= (maxLength * maxLength)) return vector;
        return Normalize(vector) * maxLength;
    }

    public static Vector2 MoveTowards(Vector2 current, Vector2 target, float maxDistanceDelta)
    {
        Vector2 delta = target - current;
        float distance = delta.Length();
        if (distance <= maxDistanceDelta || distance < 0.000001f) return target;
        return current + (delta / distance) * maxDistanceDelta;
    }

    public static float Angle(Vector2 from, Vector2 to)
    {
        float denominator = MathF.Sqrt(from.LengthSquared() * to.LengthSquared());
        if (denominator < 0.000001f) return 0f;
        float dot = Math.Clamp(Dot(from, to) / denominator, -1f, 1f);
        return MathF.Acos(dot) * (180f / MathF.PI);
    }

    public static float SignedAngle(Vector2 from, Vector2 to)
    {
        float unsigned = Angle(from, to);
        float cross = (from.X * to.Y) - (from.Y * to.X);
        return unsigned * MathF.Sign(cross == 0f ? 1f : cross);
    }

    // Unity-style shortcuts
    public float magnitude => _inner.Length();
    public float sqrMagnitude => _inner.LengthSquared();
    public Vector2 normalized => Normalize(this);

    public override bool Equals(object? obj) => obj is Vector2 other && _inner.Equals(other._inner);
    public override int GetHashCode() => _inner.GetHashCode();
    public override string ToString() => _inner.ToString();
}

/// <summary>
/// Extension methods for Vector2 (both our wrapper and the system one)
/// </summary>
public static class Vector2Extensions
{
    public static float Magnitude(this Vector2 v) => v.Length();
    public static float SqrMagnitude(this Vector2 v) => v.LengthSquared();
    public static Vector2 Normalized(this Vector2 v) => Vector2.Normalize(v);
    public static System.Numerics.Vector2 ToNumerics(this Vector2 v) => v.ToNumerics();
    public static float ToRotation(this System.Numerics.Vector2 v) => MathF.Atan2(v.Y, v.X) * (180.0f / MathF.PI);
    public static float ToRotation(this Verity.Core.Vector2 v) => MathF.Atan2(v.Y, v.X) * (180.0f / MathF.PI);

    public static System.Numerics.Vector2 Rotate(this System.Numerics.Vector2 v, float degrees)
    {
        float rad = degrees * MathF.PI / 180.0f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        return new System.Numerics.Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }

    public static Vector2 Rotate(this Vector2 v, float degrees)
    {
        float rad = degrees * MathF.PI / 180.0f;
        float cos = MathF.Cos(rad);
        float sin = MathF.Sin(rad);
        return new Vector2(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
    }
}
