using System;
using System.Globalization;
using System.Runtime.InteropServices;
using Styx.Helpers;

namespace Styx.Logic.Pathing
{
	[StructLayout(LayoutKind.Explicit, Size = 12)]
	public struct WoWPoint : IEquatable<WoWPoint>, IRangeAble
	{
		[FieldOffset(0)]
		public float X;

		[FieldOffset(4)]
		public float Y;

		[FieldOffset(8)]
		public float Z;

		public static readonly WoWPoint Center;
		public static readonly WoWPoint Zero;
		public static readonly WoWPoint Empty = new WoWPoint(float.NaN, float.NaN, float.NaN);
		public static readonly WoWPoint MinValue = new WoWPoint(float.MinValue);
		public static readonly WoWPoint MaxValue = new WoWPoint(float.MaxValue);
		public static readonly WoWPoint XUnit = new WoWPoint(1f, 0f, 0f);
		public static readonly WoWPoint YUnit = new WoWPoint(0f, 1f, 0f);
		public static readonly WoWPoint ZUnit = new WoWPoint(0f, 0f, 1f);

		public float Length => Distance(Center);

		public float ComponentSum => X + Y + Z;

		public bool IsValid => !float.IsNaN(X) && !float.IsNaN(Y) && !float.IsNaN(Z);

		public WoWPoint(float x, float y, float z)
		{
			this = default;
			X = x;
			Y = y;
			Z = z;
		}

		public WoWPoint(double x, double y, double z)
		{
			this = default;
			X = Convert.ToSingle(x, CultureInfo.InvariantCulture);
			Y = Convert.ToSingle(y, CultureInfo.InvariantCulture);
			Z = Convert.ToSingle(z, CultureInfo.InvariantCulture);
		}

		public WoWPoint(float value)
		{
			this = new WoWPoint(value, value, value);
		}

		public float Distance(WoWPoint other)
		{
			float dx = X - other.X;
			float dy = Y - other.Y;
			float dz = Z - other.Z;
			return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
		}

		public float DistanceSqr(WoWPoint other)
		{
			float dx = X - other.X;
			float dy = Y - other.Y;
			float dz = Z - other.Z;
			return dx * dx + dy * dy + dz * dz;
		}

		public float Distance2D(WoWPoint other)
		{
			float dx = X - other.X;
			float dy = Y - other.Y;
			return (float)Math.Sqrt(dx * dx + dy * dy);
		}

		public float Distance2DSqr(WoWPoint other)
		{
			float dx = X - other.X;
			float dy = Y - other.Y;
			return dx * dx + dy * dy;
		}

		public void Normalize()
		{
			float invLen = 1f / Length;
			X *= invLen;
			Y *= invLen;
			Z *= invLen;
		}

		public void MakePositive()
		{
			X = X < 0f ? -X : X;
			Y = Y < 0f ? -Y : Y;
			Z = Z < 0f ? -Z : Z;
		}

		public void MakeNegative()
		{
			X = X > 0f ? -X : X;
			Y = Y > 0f ? -Y : Y;
			Z = Z > 0f ? -Z : Z;
		}

		public void Negate()
		{
			X = -X;
			Y = -Y;
			Z = -Z;
		}

		public WoWPoint Add(float xOffset, float yOffset, float zOffset)
		{
			return new WoWPoint(X + xOffset, Y + yOffset, Z + zOffset);
		}

		public WoWPoint RayCast(float headingRadians, float distance)
		{
			return RayCast(this, headingRadians, distance);
		}

		public WoWPoint GetDirectionTo(WoWPoint to)
		{
			return GetDirection(this, to);
		}

		public Styx.Helpers.Range GetRange()
		{
			float length = Length;
			int lower = (int)Math.Floor(length);
			int upper = (int)Math.Ceiling(length);
			if (lower == upper)
				upper++;
			return new Styx.Helpers.Range(lower, upper);
		}

		public static WoWPoint RayCast(WoWPoint from, float headingRadians, float distance)
		{
			return new WoWPoint(
				from.X + (float)Math.Cos(-headingRadians) * distance,
				from.Y + (float)Math.Sin(headingRadians) * distance,
				from.Z);
		}

		public static WoWPoint GetDirection(WoWPoint from, WoWPoint to)
		{
			WoWPoint direction = to - from;
			direction.Normalize();
			return direction;
		}

		public static WoWPoint operator +(WoWPoint a, WoWPoint b)
		{
			return new WoWPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
		}

		public static WoWPoint operator -(WoWPoint a, WoWPoint b)
		{
			return new WoWPoint(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
		}

		public static WoWPoint operator *(WoWPoint a, WoWPoint b)
		{
			return new WoWPoint(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
		}

		public static WoWPoint operator /(WoWPoint a, WoWPoint b)
		{
			return new WoWPoint(a.X / b.X, a.Y / b.Y, a.Z / b.Z);
		}

		public static WoWPoint operator +(WoWPoint p, float val)
		{
			return new WoWPoint(p.X + val, p.Y + val, p.Z + val);
		}

		public static WoWPoint operator -(WoWPoint p, float val)
		{
			return new WoWPoint(p.X - val, p.Y - val, p.Z - val);
		}

		public static WoWPoint operator *(WoWPoint p, float scalar)
		{
			return new WoWPoint(p.X * scalar, p.Y * scalar, p.Z * scalar);
		}

		public static WoWPoint operator /(WoWPoint p, float scalar)
		{
			return new WoWPoint(p.X / scalar, p.Y / scalar, p.Z / scalar);
		}

		public static WoWPoint operator -(WoWPoint p)
		{
			return new WoWPoint(-p.X, -p.Y, -p.Z);
		}

		public static bool operator ==(WoWPoint a, WoWPoint b) => a.Equals(b);
		public static bool operator !=(WoWPoint a, WoWPoint b) => !a.Equals(b);

		public bool Equals(WoWPoint other)
		{
			// NaN-aware: two NaN values compare equal (for WoWPoint.Empty sentinel).
			// In IEEE 754, NaN != NaN, but we need Empty == Empty to return true
			// so that checks like 'InstanceCorpseLocation != WoWPoint.Empty' work correctly.
			bool xEq = X == other.X || (float.IsNaN(X) && float.IsNaN(other.X));
			bool yEq = Y == other.Y || (float.IsNaN(Y) && float.IsNaN(other.Y));
			bool zEq = Z == other.Z || (float.IsNaN(Z) && float.IsNaN(other.Z));
			return xEq && yEq && zEq;
		}

		public override bool Equals(object? obj)
		{
			return obj is WoWPoint other && Equals(other);
		}

		public override int GetHashCode()
		{
			int hash = X.GetHashCode();
			hash = (hash * 397) ^ Y.GetHashCode();
			return (hash * 397) ^ Z.GetHashCode();
		}

		public override string ToString()
		{
			return string.Format(CultureInfo.InvariantCulture, "({0:F2}, {1:F2}, {2:F2})", X, Y, Z);
		}
	}
}
