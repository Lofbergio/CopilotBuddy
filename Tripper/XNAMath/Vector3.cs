#nullable disable
using System;
using System.Globalization;

namespace Tripper.XNAMath
{
    /// <summary>
    /// Represents a 3D vector.
    /// </summary>
    public struct Vector3 : IEquatable<Vector3>
    {
        public float X;
        public float Y;
        public float Z;

        public static Vector3 Zero { get; private set; }
        public static Vector3 One { get; private set; }

        static Vector3()
        {
            Zero = new Vector3(0f, 0f, 0f);
            One = new Vector3(1f, 1f, 1f);
        }

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3(Vector2 v, float z)
        {
            X = v.X;
            Y = v.Y;
            Z = z;
        }

        public float MagnitudeSqr => X * X + Y * Y + Z * Z;

        public float Magnitude => (float)Math.Sqrt(MagnitudeSqr);

        public void Normalize()
        {
            float magnitude = Magnitude;
            if (magnitude > 0)
            {
                X /= magnitude;
                Y /= magnitude;
                Z /= magnitude;
            }
        }

        public Vector2 ToVector2()
        {
            return new Vector2(X, Y);
        }

        public float Distance(Vector3 to)
        {
            return Distance(ref this, ref to);
        }

        public float DistanceSqr(Vector3 to)
        {
            return DistanceSqr(ref this, ref to);
        }

        public float Distance2D(Vector3 to)
        {
            return Distance2D(ref this, ref to);
        }

        public float Distance2DSqr(Vector3 to)
        {
            return Distance2DSqr(ref this, ref to);
        }

        public bool Equals(Vector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            if (obj is Vector3 v)
                return Equals(v);
            return false;
        }

        public override int GetHashCode()
        {
            return ((X.GetHashCode() * 397) ^ Y.GetHashCode() * 397) ^ Z.GetHashCode();
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "<{0}, {1}, {2}>", X, Y, Z);
        }

        public static float Distance(ref Vector3 v1, ref Vector3 v2)
        {
            float dx = v1.X - v2.X;
            float dy = v1.Y - v2.Y;
            float dz = v1.Z - v2.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static float Distance(Vector3 v1, Vector3 v2)
        {
            return Distance(ref v1, ref v2);
        }

        public static float DistanceSqr(ref Vector3 v1, ref Vector3 v2)
        {
            float dx = v1.X - v2.X;
            float dy = v1.Y - v2.Y;
            float dz = v1.Z - v2.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public static float DistanceSqr(Vector3 v1, Vector3 v2)
        {
            return DistanceSqr(ref v1, ref v2);
        }

        public static float Distance2D(ref Vector3 v1, ref Vector3 v2)
        {
            float dx = v1.X - v2.X;
            float dy = v1.Y - v2.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        public static float Distance2DSqr(ref Vector3 v1, ref Vector3 v2)
        {
            float dx = v1.X - v2.X;
            float dy = v1.Y - v2.Y;
            return dx * dx + dy * dy;
        }

        public static float Dot(ref Vector3 v1, ref Vector3 v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        public static float Dot(Vector3 v1, Vector3 v2)
        {
            return Dot(ref v1, ref v2);
        }

        public static void Cross(ref Vector3 v1, ref Vector3 v2, out Vector3 result)
        {
            result.X = v1.Y * v2.Z - v1.Z * v2.Y;
            result.Y = v1.Z * v2.X - v1.X * v2.Z;
            result.Z = v1.X * v2.Y - v1.Y * v2.X;
        }

        public static Vector3 Cross(Vector3 v1, Vector3 v2)
        {
            Cross(ref v1, ref v2, out Vector3 result);
            return result;
        }

        public static Vector3 NormalizedDirection(Vector3 start, Vector3 end)
        {
            Vector3 direction = end - start;
            direction.Normalize();
            return direction;
        }

        public static bool operator ==(Vector3 left, Vector3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Vector3 left, Vector3 right)
        {
            return !left.Equals(right);
        }

        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        public static Vector3 operator *(Vector3 value, float scalar)
        {
            return new Vector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        public static Vector3 operator *(float scalar, Vector3 value)
        {
            return new Vector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        public static Vector3 operator /(Vector3 value, float divisor)
        {
            return new Vector3(value.X / divisor, value.Y / divisor, value.Z / divisor);
        }

        public static Vector3 operator -(Vector3 value)
        {
            return new Vector3(-value.X, -value.Y, -value.Z);
        }
    }
}
