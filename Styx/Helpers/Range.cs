using System;

namespace Styx.Helpers
{
	public struct Range : IEquatable<Range>
	{
		public int Lower { get; set; }
		public int Higher { get; set; }

		public Range(int lower, int higher)
		{
			Lower = lower;
			Higher = higher;
		}

		public override int GetHashCode()
		{
			return Lower.GetHashCode() ^ Higher.GetHashCode();
		}

		public bool Equals(Range other)
		{
			return Lower == other.Lower && Higher == other.Higher;
		}

		public override bool Equals(object? obj)
		{
			return obj is Range other && Equals(other);
		}

		public static bool operator ==(Range a, Range b) => a.Equals(b);
		public static bool operator !=(Range a, Range b) => !a.Equals(b);
	}
}
