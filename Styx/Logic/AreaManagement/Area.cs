using System;
using System.Collections.Generic;
using Styx.Helpers;
using Styx.Logic.Pathing;

namespace Styx.Logic.AreaManagement
{
	public abstract class Area : IEquatable<Area>
	{
		private readonly Guid _guid;
		private readonly Random _random = new Random();

		protected Area()
		{
			_guid = Guid.NewGuid();
			CircledHotspots = new CircularQueue<Hotspot>();
			Hotspots = new List<Hotspot>();
		}

		public abstract AreaType Type { get; }

		public Guid Guid
		{
			get { return _guid; }
		}

		public CircularQueue<Hotspot> CircledHotspots { get; set; }

		public List<Hotspot> Hotspots { get; set; }

		public void CycleToNearest()
		{
			if (CircledHotspots != null)
			{
				WoWPoint location = StyxWoW.Me.Location;
				WoWPoint nearest = WoWPoint.Zero;
				float minDist = float.MaxValue;

				foreach (Hotspot hotspot in CircledHotspots)
				{
					WoWPoint point = hotspot;
					float dist = point.Distance(location);
					if (dist < minDist)
					{
						minDist = dist;
						nearest = point;
					}
				}

				if (nearest != WoWPoint.Zero)
				{
					CircledHotspots.CycleTo(nearest);
				}
			}
		}

		public Hotspot? GetNextHotspot()
		{
			if (CircledHotspots.Count <= 0)
			{
				return null;
			}
			return CircledHotspots.Dequeue();
		}

		public Hotspot? GetRandomHotspot()
		{
			if (Hotspots.Count <= 0)
			{
				return null;
			}
			return Hotspots[_random.Next(0, Hotspots.Count)];
		}

		public static bool operator ==(Area? left, Area? right)
		{
			if (ReferenceEquals(left, right))
			{
				return true;
			}
			if (left is null || right is null)
			{
				return false;
			}
			return left._guid == right._guid;
		}

		public static bool operator !=(Area? left, Area? right)
		{
			return !(left == right);
		}

		public bool Equals(Area? other)
		{
			if (ReferenceEquals(this, other))
			{
				return true;
			}
			if (other is null)
			{
				return false;
			}
			return _guid == other._guid;
		}

		public override int GetHashCode()
		{
			return _guid.GetHashCode();
		}

		public override bool Equals(object? obj)
		{
			if (obj is null || obj.GetType() != typeof(Area))
			{
				return false;
			}
			if (ReferenceEquals(this, obj))
			{
				return true;
			}
			return Equals((Area)obj);
		}
	}
}
