using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.AreaManagement
{
	public class GrindArea : Area
	{
		private static readonly Regex _numberRegex = new Regex("\\d+", RegexOptions.Compiled);

		private readonly Stopwatch _hotspotTimer = new Stopwatch();
		private Hotspot _currentHotspot = (Hotspot)WoWPoint.Zero;
		private Profile? _lastProfile;

		public GrindArea()
		{
			StyxWoW.AreaManager.Add(this);
			TargetMinLevel = 0;
			TargetMaxLevel = int.MaxValue;
			Factions = new List<int>();
		}

		public GrindArea(HotspotManager manager)
			: this()
		{
			if (manager != null)
			{
				foreach (WoWPoint point in manager.Hotspots)
				{
					Hotspots.Add(point);
					CircledHotspots.Enqueue(point);
				}
			}
		}

		public override AreaType Type
		{
			get { return AreaType.Grind; }
		}

		public string? Name { get; set; }

		public double? LootRadius { get; set; }

		public bool RandomizeHotspots { get; set; }

		public double? MaxDistance { get; set; }

		public int TargetMinLevel { get; set; }

		public int TargetMaxLevel { get; set; }

		public List<int> Factions { get; set; }

		public int? MaximumHotspotTime { get; set; }

		public Hotspot LastHotSpot { get; set; }

		public bool HotspotChanged => CurrentHotSpot != LastHotSpot;

		public Hotspot CurrentHotSpot
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
				catch (Exception ex)
				{
					Logging.WriteException(ex);
				}
				return _currentHotspot;
			}
		}

		private void UpdateCurrentHotspot()
		{
			if (Hotspots.Count <= 0 && CircledHotspots.Count <= 0)
			{
				Logging.Write("No suitable grind or quest hotspots in {0}", ProfileManager.CurrentProfile?.Name ?? "unknown");
				throw new UserException("No grind or quest hotspots have been defined.");
			}

			float dist = ObjectManager.Me?.Location.Distance2D(_currentHotspot.Position) ?? float.MaxValue;
			if ((WoWPoint)_currentHotspot != WoWPoint.Zero && dist <= Navigator.PathPrecision)
			{
				LastHotSpot = _currentHotspot;
				_hotspotTimer.Reset();
				_hotspotTimer.Start();
			}

			if (ProfileManager.CurrentProfile != _lastProfile)
			{
				_lastProfile = ProfileManager.CurrentProfile;
				LastHotSpot = _currentHotspot;
				_currentHotspot = RandomizeHotspots 
					? GetRandomHotspot() ?? CircledHotspots.Dequeue() 
					: CircledHotspots.Dequeue();
				_hotspotTimer.Reset();
			}

			if (CircledHotspots.Count > 1 && ShouldDequeue)
			{
				LastHotSpot = _currentHotspot;
				_currentHotspot = RandomizeHotspots 
					? GetRandomHotspot() ?? CircledHotspots.Dequeue() 
					: CircledHotspots.Dequeue();
				_hotspotTimer.Reset();
			}
		}

		private bool ShouldDequeue
		{
			get
			{
				double maxTime = MaximumHotspotTime ?? 900000.0;
				if (_hotspotTimer.Elapsed.TotalMilliseconds > maxTime)
					return true;
				if (LastHotSpot == _currentHotspot && Targeting.Instance.TargetList.Count == 0)
					return true;
				if (LastHotSpot == _currentHotspot && LevelbotSettings.Instance.GroundMountFarmingMode)
					return true;
				if (LastHotSpot == _currentHotspot && StyxWoW.Me?.Mounted == true && StyxWoW.Me?.Combat == true)
					return true;
				return false;
			}
		}

		public static GrindArea FromXML(XElement value)
		{
			var grindArea = new GrindArea();
			if (value == null)
				return grindArea;

			XElement? GetElement(string name)
			{
				return value.Element(name) ?? value.Element(name.ToLowerInvariant()) ?? value.Element(name.ToUpperInvariant());
			}

			var nameElement = GetElement("Name");
			if (nameElement != null)
				grindArea.Name = nameElement.Value;

			var lootRadiusElement = GetElement("LootRadius");
			if (lootRadiusElement != null)
				grindArea.LootRadius = lootRadiusElement.Value.ToFloat();

			var randomizeElement = GetElement("RandomizeHotspots");
			if (randomizeElement != null)
				grindArea.RandomizeHotspots = randomizeElement.Value.ToBoolean();

			var maxDistanceElement = GetElement("MaxDistance");
			if (maxDistanceElement != null)
				grindArea.MaxDistance = maxDistanceElement.Value.ToFloat();

			var maxHotspotTimeElement = GetElement("MaximumHotspotTime");
			if (maxHotspotTimeElement != null)
				grindArea.MaximumHotspotTime = maxHotspotTimeElement.Value.ToInt32();

			var minLevelElement = GetElement("TargetMinLevel");
			if (minLevelElement != null)
				grindArea.TargetMinLevel = minLevelElement.Value.ToInt32();

			var maxLevelElement = GetElement("TargetMaxLevel");
			if (maxLevelElement != null)
				grindArea.TargetMaxLevel = maxLevelElement.Value.ToInt32();

			var hotspotsElement = GetElement("Hotspots");
			if (hotspotsElement != null)
			{
				foreach (var hotspotElement in hotspotsElement.Descendants("Hotspot"))
				{
					var hotspot = new Hotspot(hotspotElement);
					grindArea.CircledHotspots.Enqueue(hotspot);
					grindArea.Hotspots.Add(hotspot);
				}
			}

			var factionsElement = GetElement("Factions") ?? GetElement("factions");
			if (factionsElement != null)
			{
				foreach (Match match in _numberRegex.Matches(factionsElement.Value))
				{
					if (int.TryParse(match.Value, out int factionId))
						grindArea.Factions.Add(factionId);
				}
			}

			return grindArea;
		}

		public override string ToString()
		{
			return $"[GrindArea Name: {Name}, LootRadius: {LootRadius}, RandomizeHotspots: {RandomizeHotspots}, MaxDistance: {MaxDistance}, MaximumHotspotTime: {MaximumHotspotTime}, Hotspots: Count: {Hotspots.Count}]";
		}
	}
}
