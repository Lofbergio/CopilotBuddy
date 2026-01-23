using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

namespace Styx.Logic.AreaManagement
{
	/// <summary>
	/// Manages hotspots for grinding/questing.
	/// </summary>
	public class HotspotManager
	{
		private readonly CircularQueue<WoWPoint> circularQueue_0;
		private readonly Dictionary<WoWPoint, DateTime> dictionary_0 = new Dictionary<WoWPoint, DateTime>();
		private readonly Random random_0 = new Random();
		private static WoWPoint woWPoint_0;
		private static Profile? profile_0;
		private static WoWPoint woWPoint_1;
		private static Stopwatch? stopwatch_0;

		public List<WoWPoint> Hotspots { get; private set; }

		public HotspotManager(IEnumerable<WoWPoint> points)
		{
			Hotspots = points.ToList();
			circularQueue_0 = new CircularQueue<WoWPoint>();
			Hotspots.ForEach(circularQueue_0.Enqueue);
			CycleToNearest();
		}

		public HotspotManager(XElement element)
		{
			if (Timer == null)
			{
				Timer = new Stopwatch();
			}
			Hotspots = new List<WoWPoint>();
			List<XElement> list = element.Elements().ToList();
			foreach (XElement xelement in list)
			{
				try
				{
					if (xelement.Name == "Hotspot")
					{
						try
						{
							float? num = null;
							float? num2 = null;
							float? num3 = null;
							XAttribute[] array = xelement.Attributes().ToArray();
							foreach (XAttribute xattribute in array)
							{
								try
								{
									string? text;
									if ((text = xattribute.Name.ToString().ToUpper()) != null)
									{
										if (text == "X")
										{
											if (!float.TryParse(xattribute.Value, out float num6))
											{
												throw new ProfileAttributeExpectedException<float>(xattribute);
											}
											num = num6;
										}
										else if (text == "Y")
										{
											if (!float.TryParse(xattribute.Value, out float num5))
											{
												throw new ProfileAttributeExpectedException<float>(xattribute);
											}
											num2 = num5;
										}
										else if (text == "Z")
										{
											if (num == null || num2 == null)
											{
												throw new ProfileException("You have placed the 'Z' attribute before the 'X' and 'Y' attribute!");
											}
											if (!float.TryParse(xattribute.Value, out float num4))
											{
												throw new ProfileAttributeExpectedException<float>(xattribute);
											}
											num3 = num4;
										}
										else
										{
											throw new ProfileUnknownAttributeException(xattribute, "X", "Y", "Z");
										}
									}
									else
									{
										throw new ProfileUnknownAttributeException(xattribute, "X", "Y", "Z");
									}
								}
								catch (ProfileException ex)
								{
									Logging.WriteException(ex);
								}
							}
							if (num == null || num2 == null || num3 == null)
							{
								throw new ProfileMissingAttributeException<float>((num != null) ? ((num2 != null) ? "Z" : "Y") : "X", xelement);
							}
							Hotspots.Add(new WoWPoint(num.Value, num2.Value, num3.Value));
							continue;
						}
						catch (ProfileException ex2)
						{
							Logging.WriteException(ex2);
							continue;
						}
					}
					throw new ProfileUnknownElementException(xelement, "Hotspot");
				}
				catch (ProfileException ex3)
				{
					Logging.WriteException(ex3);
				}
			}
			circularQueue_0 = new CircularQueue<WoWPoint>();
			Hotspots.ForEach(circularQueue_0.Enqueue);
			CycleToNearest();
		}

		static HotspotManager()
		{
			woWPoint_0 = WoWPoint.Zero;
		}

		public Dictionary<WoWPoint, DateTime> Blacklist
		{
			get
			{
				CleanupBlacklist();
				return dictionary_0;
			}
		}

		public void CycleToNearest()
		{
			WoWPoint woWPoint = WoWPoint.Zero;
			float num = float.MaxValue;
			foreach (WoWPoint woWPoint2 in Hotspots)
			{
				float num2 = woWPoint2.Distance(ObjectManager.Me.Location);
				if (num2 < num)
				{
					num = num2;
					woWPoint = woWPoint2;
				}
			}
			if (woWPoint != WoWPoint.Zero)
			{
				circularQueue_0.CycleTo(woWPoint);
			}
		}

		public WoWPoint GetNextHotspot()
		{
			if (circularQueue_0.Count <= 0)
			{
				return WoWPoint.Zero;
			}
			return circularQueue_0.Dequeue();
		}

		public WoWPoint GetRandomHotspot()
		{
			List<WoWPoint> list = Hotspots.Where(pnt => !Blacklist.ContainsKey(pnt)).ToList();
			if (list.Count <= 0)
			{
				return WoWPoint.Zero;
			}
			return list[random_0.Next(0, list.Count)];
		}

		public void BlacklistPoint(WoWPoint pnt, TimeSpan forTime)
		{
			DateTime now = DateTime.Now;
			BlacklistPoint(pnt, now.Add(forTime));
		}

		public void BlacklistPoint(WoWPoint pnt, DateTime expiration)
		{
			if (!dictionary_0.ContainsKey(pnt))
			{
				dictionary_0.Add(pnt, expiration);
			}
			else
			{
				dictionary_0[pnt] = expiration;
			}
		}

		private void CleanupBlacklist()
		{
			DateTime now = DateTime.Now;
			foreach (KeyValuePair<WoWPoint, DateTime> keyValuePair in dictionary_0)
			{
				if (now > keyValuePair.Value)
				{
					dictionary_0.Remove(keyValuePair.Key);
				}
			}
		}

		public static WoWPoint CurrentHotSpot
		{
			get
			{
				try
				{
					UpdateCurrentHotspot();
				}
				catch (UserException ex)
				{
					Logging.Write(ex.Message);
				}
				catch (Exception ex2)
				{
					Logging.WriteException(ex2);
				}
				return woWPoint_0;
			}
		}

		public static WoWPoint LastHotSpot
		{
			get { return woWPoint_1; }
			set { woWPoint_1 = value; }
		}

		public static Stopwatch? Timer
		{
			get { return stopwatch_0; }
			set { stopwatch_0 = value; }
		}

		private static void UpdateCurrentHotspot()
		{
			if (ProfileManager.CurrentOuterProfile == null)
			{
				throw new UserException("No profile is loaded!");
			}
			HotspotManager? hotspotManager = ProfileManager.CurrentProfile?.HotspotManager;
			if (hotspotManager == null)
			{
				Logging.Write("No suitable hotspots in {0}.", ProfileManager.CurrentProfile?.Name ?? "profile");
				throw new UserException("No hotspots have been defined!");
			}
			if (ProfileManager.CurrentProfile != profile_0)
			{
				profile_0 = ProfileManager.CurrentProfile;
				LastHotSpot = woWPoint_0;
				woWPoint_0 = hotspotManager.GetNextHotspot();
				Timer?.Reset();
			}
			TimeSpan elapsed = Timer?.Elapsed ?? TimeSpan.Zero;
			if (elapsed.TotalMinutes > 5.0 || (LastHotSpot == woWPoint_0 && Targeting.Instance.TargetList.Count == 0))
			{
				LastHotSpot = woWPoint_0;
				woWPoint_0 = hotspotManager.GetNextHotspot();
				Timer?.Reset();
			}
		}
	}
}
