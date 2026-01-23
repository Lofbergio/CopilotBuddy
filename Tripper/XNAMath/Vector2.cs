#nullable disable
using System;
using System.Globalization;

namespace Tripper.XNAMath
{
	public struct Vector2 : IEquatable<Vector2>
	{
		public float X;
		public float Y;

		public static Vector2 Zero { get; private set; }
		public static Vector2 One { get; private set; }

		static Vector2()
		{
			Zero = new Vector2(0f, 0f);
			One = new Vector2(1f, 1f);
		}

		public Vector2(float x, float y)
		{
			X = x;
			Y = y;
		}

		public float Length()
		{
			return (float)Math.Sqrt(LengthSqr());
		}

		public float LengthSqr()
		{
			return X * X + Y * Y;
		}

		public void Normalize()
		{
			float length = Length();
			Divide(ref this, length, out this);
		}

		public float Distance(Vector2 v)
		{
			return Distance(ref this, ref v);
		}

		public float Distance(ref Vector2 v)
		{
			return Distance(ref this, ref v);
		}

		public float DistanceSqr(Vector2 v)
		{
			return DistanceSqr(ref this, ref v);
		}

		public float DistanceSqr(ref Vector2 v)
		{
			return DistanceSqr(ref this, ref v);
		}

		public bool Equals(Vector2 other)
		{
			return Equals(ref this, ref other);
		}

		public bool Equals(ref Vector2 other)
		{
			return Equals(ref this, ref other);
		}

		public static bool Equals(ref Vector2 v1, ref Vector2 v2)
		{
			return v1.X == v2.X && v1.Y == v2.Y;
		}

		public static bool operator ==(Vector2 ls, Vector2 rs)
		{
			return Equals(ref ls, ref rs);
		}

		public static bool operator !=(Vector2 ls, Vector2 rs)
		{
			return !Equals(ref ls, ref rs);
		}

		public override bool Equals(object obj)
		{
			if (obj is Vector2 v)
				return Equals(v);
			return false;
		}

		public override int GetHashCode()
		{
			return (X.GetHashCode() * 397) ^ Y.GetHashCode();
		}

		public override string ToString()
		{
			return string.Format(CultureInfo.InvariantCulture, "<{0}, {1}>", X, Y);
		}

		public static Vector2 operator +(Vector2 ls, Vector2 rs)
		{
			Add(ref ls, ref rs, out Vector2 result);
			return result;
		}

		public static Vector2 operator -(Vector2 ls, Vector2 rs)
		{
			Subtract(ref ls, ref rs, out Vector2 result);
			return result;
		}

		public static Vector2 operator -(Vector2 v)
		{
			return new Vector2(-v.X, -v.Y);
		}

		public static Vector2 operator *(Vector2 ls, Vector2 rs)
		{
			Multiply(ref ls, ref rs, out Vector2 result);
			return result;
		}

		public static Vector2 operator *(Vector2 ls, float rs)
		{
			Multiply(ref ls, rs, out Vector2 result);
			return result;
		}

		public static Vector2 operator /(Vector2 ls, Vector2 rs)
		{
			Divide(ref ls, ref rs, out Vector2 result);
			return result;
		}

		public static Vector2 operator /(Vector2 ls, float rs)
		{
			Divide(ref ls, rs, out Vector2 result);
			return result;
		}

		public static void Add(ref Vector2 v1, ref Vector2 v2, out Vector2 result)
		{
			result.X = v1.X + v2.X;
			result.Y = v1.Y + v2.Y;
		}

		public static void Subtract(ref Vector2 v1, ref Vector2 v2, out Vector2 result)
		{
			result.X = v1.X - v2.X;
			result.Y = v1.Y - v2.Y;
		}

		public static void Multiply(ref Vector2 v1, ref Vector2 v2, out Vector2 result)
		{
			result.X = v1.X * v2.X;
			result.Y = v1.Y * v2.Y;
		}

		public static void Multiply(ref Vector2 v1, float scalar, out Vector2 result)
		{
			result.X = v1.X * scalar;
			result.Y = v1.Y * scalar;
		}

		public static void Divide(ref Vector2 v1, ref Vector2 v2, out Vector2 result)
		{
			result.X = v1.X / v2.X;
			result.Y = v1.Y / v2.Y;
		}

		public static void Divide(ref Vector2 v1, float divisor, out Vector2 result)
		{
			float inv = 1f / divisor;
			Multiply(ref v1, inv, out result);
		}

		public static float Distance(ref Vector2 v1, ref Vector2 v2)
		{
			return (float)Math.Sqrt(DistanceSqr(ref v1, ref v2));
		}

		public static float DistanceSqr(ref Vector2 v1, ref Vector2 v2)
		{
			float dx = v1.X - v2.X;
			float dy = v1.Y - v2.Y;
			return dx * dx + dy * dy;
		}

		public static void GetDirection(ref Vector2 from, ref Vector2 to, out Vector2 dir)
		{
			Subtract(ref to, ref from, out dir);
			dir.Normalize();
		}

		public static Vector2 Min(Vector2 v1, Vector2 v2)
		{
			Min(ref v1, ref v2, out Vector2 result);
			return result;
		}

		public static void Min(ref Vector2 v1, ref Vector2 v2, out Vector2 result)
		{
			result.X = Math.Min(v1.X, v2.X);
			result.Y = Math.Min(v1.Y, v2.Y);
		}

		public static Vector2 Max(Vector2 v1, Vector2 v2)
		{
			Max(ref v1, ref v2, out Vector2 result);
			return result;
		}

		public static void Max(ref Vector2 v1, ref Vector2 v2, out Vector2 result)
		{
			result.X = Math.Max(v1.X, v2.X);
			result.Y = Math.Max(v1.Y, v2.Y);
		}

		public static float Dot(Vector2 v1, Vector2 v2)
		{
			return v1.X * v2.X + v1.Y * v2.Y;
		}
	}
}
