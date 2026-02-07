#nullable disable
using System;
using System.Globalization;
using System.Numerics;
using Styx.Logic.Pathing;

namespace Tripper.Tools.Math
{
    /// <summary>
    /// Tripper.Tools.Math.Vector3 — thin struct matching HB 4.3.4's API.
    /// QBs do <c>using Tripper.Tools.Math;</c> to access this type.
    /// Provides implicit conversions to/from <see cref="WoWPoint"/> so that
    /// <c>Vector3 v = Location - Location;</c> works without modification.
    /// </summary>
    public struct Vector3 : IEquatable<Vector3>
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly Vector3 Zero = new Vector3(0f, 0f, 0f);
        public static readonly Vector3 One  = new Vector3(1f, 1f, 1f);

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Vector3(float value)
        {
            X = value;
            Y = value;
            Z = value;
        }

        #region Properties

        public float MagnitudeSqr => X * X + Y * Y + Z * Z;

        public float Magnitude => (float)System.Math.Sqrt(MagnitudeSqr);

        #endregion

        #region Instance methods

        public void Normalize()
        {
            float mag = Magnitude;
            if (mag > 0f)
            {
                X /= mag;
                Y /= mag;
                Z /= mag;
            }
        }

        public float Distance(Vector3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public float DistanceSqr(Vector3 other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        #endregion

        #region Static methods

        public static float Dot(Vector3 v1, Vector3 v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        public static Vector3 Cross(Vector3 v1, Vector3 v2)
        {
            return new Vector3(
                v1.Y * v2.Z - v1.Z * v2.Y,
                v1.Z * v2.X - v1.X * v2.Z,
                v1.X * v2.Y - v1.Y * v2.X);
        }

        /// <summary>
        /// Transforms a vector by a matrix (position transform).
        /// Used by MyCTM.cs: <c>Vector3.Transform(relLoc, transport.GetWorldMatrix())</c>
        /// </summary>
        public static Vector3 Transform(Vector3 position, Matrix matrix)
        {
            Matrix4x4 m = matrix;
            System.Numerics.Vector3 p = new System.Numerics.Vector3(position.X, position.Y, position.Z);
            System.Numerics.Vector3 result = System.Numerics.Vector3.Transform(p, m);
            return new Vector3(result.X, result.Y, result.Z);
        }

        /// <summary>
        /// ref/out overload matching HB 4.3.4 signature.
        /// </summary>
        public static void Transform(ref Vector3 position, ref Matrix matrix, out Vector3 result)
        {
            result = Transform(position, matrix);
        }

        public static Vector3 NormalizedDirection(Vector3 start, Vector3 end)
        {
            Vector3 dir = end - start;
            dir.Normalize();
            return dir;
        }

        #endregion

        #region Operators

        public static Vector3 operator +(Vector3 a, Vector3 b)
            => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Vector3 operator -(Vector3 a, Vector3 b)
            => new Vector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Vector3 operator *(Vector3 v, float s)
            => new Vector3(v.X * s, v.Y * s, v.Z * s);

        public static Vector3 operator *(float s, Vector3 v)
            => new Vector3(v.X * s, v.Y * s, v.Z * s);

        public static Vector3 operator /(Vector3 v, float d)
            => new Vector3(v.X / d, v.Y / d, v.Z / d);

        public static Vector3 operator -(Vector3 v)
            => new Vector3(-v.X, -v.Y, -v.Z);

        public static bool operator ==(Vector3 a, Vector3 b) => a.Equals(b);
        public static bool operator !=(Vector3 a, Vector3 b) => !a.Equals(b);

        #endregion

        #region Implicit conversions WoWPoint <-> Vector3

        public static implicit operator Vector3(WoWPoint p)
            => new Vector3(p.X, p.Y, p.Z);

        public static implicit operator WoWPoint(Vector3 v)
            => new WoWPoint(v.X, v.Y, v.Z);

        #endregion

        #region Equality

        public bool Equals(Vector3 other)
            => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj)
            => obj is Vector3 v && Equals(v);

        public override int GetHashCode()
            => ((X.GetHashCode() * 397) ^ Y.GetHashCode() * 397) ^ Z.GetHashCode();

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture, "<{0}, {1}, {2}>", X, Y, Z);

        #endregion
    }
}
