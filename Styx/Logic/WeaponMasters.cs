using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

namespace Styx.Logic
{
	/// <summary>
	/// Weapon-proficiency training support. Data\WeaponMasters.xml (exported from the server DB by
	/// Tools\WeaponMasterExtractor) says who the weapon masters are, where they stand, which side they
	/// serve, and which proficiencies they teach. The class-learnable table below only prunes walks —
	/// the trainer frame is the final truth (TrainerFrame.BuyAll buys 'available' services only), so a
	/// stale entry costs a wasted walk, never an illegal purchase.
	/// </summary>
	public static class WeaponMasters
	{
		private sealed class Master
		{
			public uint Entry;
			public string Name = "";
			public string Side = "";
			public uint Map;
			public WoWPoint Location;
			public readonly List<SkillLine> Lines = new List<SkillLine>();
		}

		// 3.3.5a weapon proficiencies each class may train (wands are innate, never trained).
		private static readonly Dictionary<WoWClass, SkillLine[]> Learnable =
			new Dictionary<WoWClass, SkillLine[]>
		{
			{ WoWClass.Warrior, new[] { SkillLine.Swords, SkillLine.TwoHandedSwords, SkillLine.Axes, SkillLine.TwoHandedAxes, SkillLine.Maces, SkillLine.TwoHandedMaces, SkillLine.Daggers, SkillLine.FistWeapons, SkillLine.Polearms, SkillLine.Staves, SkillLine.Bows, SkillLine.Guns, SkillLine.Crossbows, SkillLine.Thrown } },
			{ WoWClass.Paladin, new[] { SkillLine.Swords, SkillLine.TwoHandedSwords, SkillLine.Axes, SkillLine.TwoHandedAxes, SkillLine.Maces, SkillLine.TwoHandedMaces, SkillLine.Polearms } },
			{ WoWClass.Hunter, new[] { SkillLine.Swords, SkillLine.TwoHandedSwords, SkillLine.Axes, SkillLine.TwoHandedAxes, SkillLine.Daggers, SkillLine.FistWeapons, SkillLine.Polearms, SkillLine.Staves, SkillLine.Bows, SkillLine.Guns, SkillLine.Crossbows, SkillLine.Thrown } },
			{ WoWClass.Rogue, new[] { SkillLine.Swords, SkillLine.Maces, SkillLine.Daggers, SkillLine.FistWeapons, SkillLine.Bows, SkillLine.Guns, SkillLine.Crossbows, SkillLine.Thrown } },
			{ WoWClass.Priest, new[] { SkillLine.Maces, SkillLine.Daggers, SkillLine.Staves } },
			{ WoWClass.DeathKnight, new[] { SkillLine.Swords, SkillLine.TwoHandedSwords, SkillLine.Axes, SkillLine.TwoHandedAxes, SkillLine.Maces, SkillLine.TwoHandedMaces, SkillLine.Polearms } },
			{ WoWClass.Shaman, new[] { SkillLine.Axes, SkillLine.TwoHandedAxes, SkillLine.Maces, SkillLine.TwoHandedMaces, SkillLine.Daggers, SkillLine.FistWeapons, SkillLine.Staves } },
			{ WoWClass.Mage, new[] { SkillLine.Swords, SkillLine.Daggers, SkillLine.Staves } },
			{ WoWClass.Warlock, new[] { SkillLine.Swords, SkillLine.Daggers, SkillLine.Staves } },
			{ WoWClass.Druid, new[] { SkillLine.Maces, SkillLine.TwoHandedMaces, SkillLine.Daggers, SkillLine.FistWeapons, SkillLine.Polearms, SkillLine.Staves } },
		};

		private static List<Master>? _masters;
		private static readonly Dictionary<uint, DateTime> _visited = new Dictionary<uint, DateTime>();
		private static readonly TimeSpan VisitCooldown = TimeSpan.FromMinutes(10);

		private static List<Master> Masters
		{
			get
			{
				if (_masters != null) return _masters;
				_masters = new List<Master>();
				try
				{
					string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? ".", "Data", "WeaponMasters.xml");
					if (!File.Exists(path)) path = Path.Combine("Data", "WeaponMasters.xml");
					if (!File.Exists(path))
					{
						Logging.WriteDebug("[WeaponMasters] Data\\WeaponMasters.xml not found — weapon-skill training disabled.");
						return _masters;
					}
					foreach (XElement m in XElement.Load(path).Elements("Master"))
					{
						var master = new Master
						{
							Entry = (uint)(int)m.Attribute("entry"),
							Name = (string)m.Attribute("name") ?? "",
							Side = (string)m.Attribute("side") ?? "",
							Map = (uint)(int)m.Attribute("map"),
							Location = new WoWPoint(
								float.Parse((string)m.Attribute("x"), CultureInfo.InvariantCulture),
								float.Parse((string)m.Attribute("y"), CultureInfo.InvariantCulture),
								float.Parse((string)m.Attribute("z"), CultureInfo.InvariantCulture)),
						};
						foreach (XElement s in m.Elements("Skill"))
							master.Lines.Add((SkillLine)(int)s.Attribute("line"));
						_masters.Add(master);
					}
					Logging.WriteDebug("[WeaponMasters] {0} weapon master(s) loaded.", _masters.Count);
				}
				catch (Exception ex)
				{
					Logging.WriteDebug("[WeaponMasters] load failed ({0}) — weapon-skill training disabled.", ex.Message);
				}
				return _masters;
			}
		}

		/// <summary>
		/// Nearest same-side weapon master on our map within maxYd that teaches at least one proficiency
		/// this class can learn and doesn't know yet (GetSkill == null). Null if none. Recently-visited
		/// masters are skipped so a failed visit can't tight-loop.
		/// </summary>
		public static Vendor? FindUseful(float maxYd, out string skills)
		{
			skills = "";
			var me = StyxWoW.Me;
			if (me == null) return null;
			if (!Learnable.TryGetValue(me.Class, out SkillLine[]? canLearn)) return null;

			var missing = new HashSet<SkillLine>(canLearn.Where(l => me.GetSkill(l) == null));
			if (missing.Count == 0) return null;

			string side = me.IsAlliance ? "Alliance" : "Horde";
			Master? best = null;
			double bestDist = maxYd;
			List<SkillLine>? bestLines = null;
			foreach (Master m in Masters)
			{
				if (m.Map != me.MapId || m.Side != side) continue;
				if (_visited.TryGetValue(m.Entry, out DateTime at) && DateTime.Now - at < VisitCooldown) continue;
				List<SkillLine> teachable = m.Lines.Where(missing.Contains).ToList();
				if (teachable.Count == 0) continue;
				double d = m.Location.Distance(me.Location);
				if (d >= bestDist) continue;
				best = m; bestDist = d; bestLines = teachable;
			}
			if (best == null) return null;
			skills = string.Join(", ", bestLines!);
			return new Vendor((int)best.Entry, best.Name, Vendor.VendorType.Train, best.Location);
		}

		public static void MarkVisited(uint entry) => _visited[entry] = DateTime.Now;
	}
}
