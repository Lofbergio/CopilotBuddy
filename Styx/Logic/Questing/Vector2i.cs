#nullable disable
using Tripper.XNAMath;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Integer 2D vector for quest area coordinates.
	/// </summary>
	public struct Vector2i
	{
		public int X;
		public int Y;

		public static implicit operator Vector2(Vector2i v)
		{
			return new Vector2((float)v.X, (float)v.Y);
		}
	}
}
