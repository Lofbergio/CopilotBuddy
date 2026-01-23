#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Styx.Helpers;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Inventory
{
    /// <summary>
    /// Represents a set of stat weights for item evaluation.
    /// </summary>
    public class WeightSet
    {
        private readonly Dictionary<ulong, float> _itemCache = new Dictionary<ulong, float>();

        /// <summary>
        /// Gets the stat weights.
        /// </summary>
        public Dictionary<Stat, float> Weights { get; private set; }

        /// <summary>
        /// Gets the name of this weight set.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// Creates a new weight set.
        /// </summary>
        public WeightSet(string name, Dictionary<Stat, float> weights)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentNullException(nameof(name), "The name of the weight set can not be null or empty!");

            Name = name;
            Weights = weights;
        }

        /// <summary>
        /// Creates a weight set from XML.
        /// </summary>
        internal WeightSet(string name, XContainer weights)
            : this(name, ParseWeights(weights))
        {
        }

        /// <summary>
        /// Gets the score for a stat with given points.
        /// </summary>
        public float GetStatScore(Stat stat, float statPoints)
        {
            if (float.IsNaN(statPoints) || !Weights.ContainsKey(stat))
                return 0f;

            return Weights[stat] * statPoints;
        }

        /// <summary>
        /// Gets the score for a stat by name.
        /// </summary>
        public float GetStatScore(string statName, float statPoints)
        {
            if (statName == null)
                throw new ArgumentNullException(nameof(statName));

            try
            {
                if (statName.EndsWith("2"))
                    statName = statName.Remove(statName.Length - 1, 1);

                var stat = (Stat)Enum.Parse(typeof(Stat), statName, true);
                return GetStatScore(stat, statPoints);
            }
            catch (ArgumentException)
            {
                Logging.Write($"Stat name {statName} is unknown!");
                return 0f;
            }
        }

        /// <summary>
        /// Evaluates an item's score based on this weight set.
        /// </summary>
        public float EvaluateItem(WoWItem item)
        {
            if (item == null)
                return 0f;

            ulong guid = item.Guid;
            if (_itemCache.ContainsKey(guid))
                return _itemCache[guid];

            var itemStats = item.GetItemStats();
            var itemInfo = item.ItemInfo;

            float score = GetStatScore("DPS", itemStats.DPS);
            score += EvaluateWeaponStats(itemInfo);

            foreach (var kvp in itemStats.Stats)
            {
                score += GetStatScore(kvp.Key.ToString(), kvp.Value);
            }

            _itemCache.Add(guid, score);
            return score;
        }

        /// <summary>
        /// Evaluates an item's score from ItemInfo.
        /// </summary>
        public float EvaluateItem(ItemInfo info)
        {
            if (info == null)
                return 0f;

            var itemStats = info.GetItemStats();

            float score = GetStatScore(Stat.DPS, info.DPS);
            score += EvaluateWeaponStats(info);

            foreach (var kvp in itemStats)
            {
                score += GetStatScore(kvp.Key.ToString(), kvp.Value);
            }

            score += GetStatScore(Stat.Armor, info.Armor);
            score += GetStatScore(Stat.HolyResistance, info.HolyResistance);
            score += GetStatScore(Stat.FrostResistance, info.FrostResistance);
            score += GetStatScore(Stat.FireResistance, info.FireResistance);
            score += GetStatScore(Stat.NatureResistance, info.NatureResistance);
            score += GetStatScore(Stat.ArcaneResistance, info.ArcaneResistance);
            score += GetStatScore(Stat.ShadowResistance, info.ShadowResistance);

            // Socket scoring
            for (int i = 0; i < info.InternalInfo.SocketColor.Length; i++)
            {
                var socketColor = info.InternalInfo.SocketColor[i];
                if (socketColor != WoWCache.SocketColorFlags.None)
                {
                    if ((socketColor & WoWCache.SocketColorFlags.Meta) != WoWCache.SocketColorFlags.None)
                        score += GetStatScore(Stat.MetaSocket, 1f);
                    if ((socketColor & WoWCache.SocketColorFlags.Red) != WoWCache.SocketColorFlags.None)
                        score += GetStatScore(Stat.RedSocket, 1f);
                    if ((socketColor & WoWCache.SocketColorFlags.Yellow) != WoWCache.SocketColorFlags.None)
                        score += GetStatScore(Stat.YellowSocket, 1f);
                    if ((socketColor & WoWCache.SocketColorFlags.Blue) != WoWCache.SocketColorFlags.None)
                        score += GetStatScore(Stat.BlueSocket, 1f);
                }
            }

            return score;
        }

        /// <summary>
        /// Evaluates weapon-specific stats.
        /// </summary>
        private float EvaluateWeaponStats(ItemInfo info)
        {
            float score = 0f;
            float speedBaseline = 0f;

            if (Weights.ContainsKey(Stat.SpeedBaseLine))
                speedBaseline = Weights[Stat.SpeedBaseLine];

            if (Weights.ContainsKey(Stat.Speed))
                score += GetStatScore(Stat.Speed, info.WeaponSpeed - speedBaseline * 1000f);

            score += GetStatScore(Stat.MinDamage, info.MinDamage);
            score += GetStatScore(Stat.MaxDamage, info.MaxDamage);

            return score;
        }

        /// <summary>
        /// Parses weights from XML.
        /// </summary>
        private static Dictionary<Stat, float> ParseWeights(XContainer weightElm)
        {
            var weights = new Dictionary<Stat, float>();

            foreach (var element in weightElm.Elements())
            {
                string name = element.Name.ToString();

                if (!Enum.TryParse(name, true, out Stat stat))
                    continue;

                if (float.TryParse(element.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) 
                    && !weights.ContainsKey(stat))
                {
                    weights.Add(stat, value);
                }
            }

            return weights;
        }
    }
}
