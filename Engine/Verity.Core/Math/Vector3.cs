using System.Runtime.CompilerServices;

namespace Verity.Core;

/// <summary>
/// Unity-style Vector3 wrapper with interop to System.Numerics.Vector3.
/// </summary>
public struct Vector3
{
    private System.Numerics.Vector3 _inner;

    public float X { get => _inner.X; set => _inner.X = value; }
    public float Y { get => _inner.Y; set => _inner.Y = value; }
    public float Z { get => _inner.Z; set => _inner.Z = value; }
    public float x { get => _inner.X; set => _inner.X = value; }
    public float y { get => _inner.Y; set => _inner.Y = value; }
    public float z { get => _inner.Z; set => _inner.Z = value; }

    public Vector3(float x, float y, float z) => _inner = new System.Numerics.Vector3(x, y, z);
    public Vector3(float value) => _inner = new System.Numerics.Vector3(value);
    public Vector3(Vector2 value, float z) => _inner = new System.Numerics.Vector3(value.X, value.Y, z);
    public Vector3(System.Numerics.Vector3 inner) => _inner = inner;

    public static Vector3 Zero => new(System.Numerics.Vector3.Zero);
    public static Vector3 One => new(System.Numerics.Vector3.One);
    public static Vector3 Up => new(0f, 1f, 0f);
    public static Vector3 Down => new(0f, -1f, 0f);
    public static Vector3 Left => new(-1f, 0f, 0f);
    public static Vector3 Right => new(1f, 0f, 0f);
    public static Vector3 Forward => new(0f, 0f, 1f);
    public static Vector3 Back => new(0f, 0f, -1f);
    public static Vector3 UnitX => new(System.Numerics.Vector3.UnitX);
    public static Vector3 UnitY => new(System.Numerics.Vector3.UnitY);
    public static Vector3 UnitZ => new(System.Numerics.Vector3.UnitZ);
    public static Vector3 zero => Zero;
    public static Vector3 one => One;
    public static Vector3 up => Up;
    public static Vector3 down => Down;
    public static Vector3 left => Left;
    public static Vector3 right => Right;
    public static Vector3 forward => Forward;
    public static Vector3 back => Back;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator System.Numerics.Vector3(Vector3 v) => v._inner;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Vector3(System.Numerics.Vector3 v) => new(v);

    public static Vector3 operator +(Vector3 a, Vector3 b) => a._inner + b._inner;
    public static Vector3 operator -(Vector3 a, Vector3 b) => a._inner - b._inner;
    public static Vector3 operator *(Vector3 a, Vector3 b) => a._inner * b._inner;
    public static Vector3 operator /(Vector3 a, Vector3 b) => a._inner / b._inner;
    public static Vector3 operator *(Vector3 a, float b) => a._inner * b;
    public static Vector3 operator *(float a, Vector3 b) => b._inner * a;
    public static Vector3 operator /(Vector3 a, float b) => a._inner / b;
    public static Vector3 operator -(Vector3 a) => -a._inner;
    public static bool operator ==(Vector3 a, Vector3 b) => a._inner == b._inner;
    public static bool operator !=(Vector3 a, Vector3 b) => a._inner != b._inner;

    public float Length() => _inner.Length();
    public float LengthSquared() => _inner.LengthSquared();
    public System.Numerics.Vector3 ToNumerics() => _inner;
    public static Vector3 FromNumerics(System.Numerics.Vector3 v) => new(v);

    public static float Dot(Vector3 a, Vector3 b) => System.Numerics.Vector3.Dot(a, b);
    public static Vector3 Cross(Vector3 a, Vector3 b) => System.Numerics.Vector3.Cross(a, b);
    public static float Distance(Vector3 a, Vector3 b) => System.Numerics.Vector3.Distance(a, b);
    public static float DistanceSquared(Vector3 a, Vector3 b) => System.Numerics.Vector3.DistanceSquared(a, b);
    public static Vector3 Normalize(Vector3 v) => v.LengthSquared() > 0.000001f ? System.Numerics.Vector3.Normalize(v) : Zero;
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => System.Numerics.Vector3.Lerp(a, b, t);
    public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => a + (b - a) * t;
    public static Vector3 Min(Vector3 a, Vector3 b) => System.Numerics.Vector3.Min(a, b);
    public static Vector3 Max(Vector3 a, Vector3 b) => System.Numerics.Vector3.Max(a, b);
    public static Vector3 Transform(Vector3 position, System.Numerics.Matrix4x4 matrix) => System.Numerics.Vector3.Transform(position, matrix);
    public static Vector3 TransformNormal(Vector3 normal, System.Numerics.Matrix4x4 matrix) => System.Numerics.Vector3.TransformNormal(normal, matrix);
    public static Vector3 Scale(Vector3 a, Vector3 b) => a * b;
    public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal) => inDirection - (2f * Dot(inDirection, inNormal) * inNormal);
    public static Vector3 Project(Vector3 vector, Vector3 onNormal)
    {
        float denominator = Dot(onNormal, onNormal);
        if (denominator < 0.000001f) return Zero;
        return onNormal * (Dot(vector, onNormal) / denominator);
    }

    public static Vector3 ClampMagnitude(Vector3 vector, float maxLength)
    {
        float sqrMagnitude = vector.LengthSquared();
        if (sqrMagnitude <= (maxLength * maxLength)) return vector;
        return Normalize(vector) * maxLength;
    }

    public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
    {
        Vector3 delta = target - current;
        float distance = delta.Length();
        if (distance <= maxDistanceDelta || distance < 0.000001f) return target;
        return current + (delta / distance) * maxDistanceDelta;
    }

    public float magnitude => Length();
    public float sqrMagnitude => LengthSquared();
    public Vector3 normalized => Normalize(this);

    public override bool Equals(object? obj) => obj is Vector3 other && _inner.Equals(other._inner);
    public override int GetHashCode() => _inner.GetHashCode();
    public override string ToString() => _inner.ToString();
}

public static class Vector3Extensions
{
    public static float Magnitude(this Vector3 v) => v.Length();
    public static float SqrMagnitude(this Vector3 v) => v.LengthSquared();
    public static Vector3 Normalized(this Vector3 v) => Vector3.Normalize(v);
    public static System.Numerics.Vector3 ToNumerics(this Vector3 v) => v.ToNumerics();
}
