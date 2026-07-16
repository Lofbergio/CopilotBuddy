// Styx.Logic.Inventory.WeightSetEx
// Ported from Honorbuddy 4.3.4 (Cata) - Cleaned

using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

#nullable disable
namespace Styx.Logic.Inventory;

public class WeightSetEx : IDisposable
{
  private static WeightSetEx _cachedWeightSet;
  private static IEnumerable<WeightSetEx> _loadedWeightSets;
  // Default fallback weights used when a weight set does not define these stats.
  // Armor floor: grey/white leveling gear carries no stats at all, so a set without an Armor
  // weight (every caster/healer set) ranked such gear purely by the item-level tiebreak and
  // equipped lower-armor leather over mail. Small enough that real set weights (15-100/pt)
  // always dominate; a set that defines Armor overrides it.
  private static readonly Dictionary<Stat, float> DefaultStatWeights = new Dictionary<Stat, float>
  {
    { Stat.DPS, 1.0f },
    { Stat.MinDamage, 0.5f },
    { Stat.MaxDamage, 0.5f },
    { Stat.Speed, 0.0f },
    { Stat.Armor, 0.01f }
  };

  private WeightSetEx(XElement weightElm)
  {
    this.Specialization = -1;
    if (weightElm == null)
    {
      this.Class = WoWClass.None;
      this.StatScores = (Dictionary<Stat, float>) null;
    }
    else
    {
      foreach (XAttribute attribute in weightElm.Attributes())
      {
        switch (attribute.Name.ToString())
        {
          case nameof (Class):
            try
            {
              this.Class = (WoWClass) Enum.Parse(typeof (WoWClass), attribute.Value, true);
              continue;
            }
            catch (Exception ex)
            {
              Logging.WriteException(ex);
              Logging.WriteDebug("Error parsing attribute Class in WeightSet element:{0}", (object) weightElm);
              continue;
            }
          case nameof (Specialization):
            try
            {
              int result;
              if (!int.TryParse(attribute.Value, out result))
                throw new Exception($"Error parsing attribute Specialization in WeightSet element:{weightElm}");
              this.Specialization = result;
              continue;
            }
            catch (Exception ex)
            {
              Logging.WriteException(ex);
              continue;
            }
          case nameof (Name):
            this.Name = attribute.Value;
            continue;
          default:
            continue;
        }
      }
      Dictionary<Stat, float> dictionary = new Dictionary<Stat, float>();
      foreach (XElement xelement in weightElm.Elements().ToArray<XElement>())
      {
        Stat key;
        try
        {
          key = (Stat) Enum.Parse(typeof (Stat), xelement.Name.LocalName, true);
        }
        catch (ArgumentException ex)
        {
          continue;
        }
        float result;
        if (float.TryParse(xelement.Value, NumberStyles.Float, (IFormatProvider) CultureInfo.InvariantCulture, out result) && !dictionary.ContainsKey(key))
          dictionary.Add(key, result);
      }
      this.StatScores = dictionary;
    }
    Lua.Events.AttachEvent("ACTIVE_TALENT_GROUP_CHANGED", new LuaEventHandlerDelegate(WeightSetEx.OnTalentGroupChanged));
  }

  private static void OnTalentGroupChanged(object sender, LuaEventArgs e)
  {
    Logging.WriteDebug("[WeightSet] Your spec has been changed. Clearing old weight set");
    WeightSetEx._cachedWeightSet = (WeightSetEx) null;
  }

  public static WeightSetEx CurrentWeightSet
  {
    get
    {
      if (WeightSetEx._loadedWeightSets == null)
        WeightSetEx.LoadWeightSets();
      if (WeightSetEx._cachedWeightSet != null)
        return WeightSetEx._cachedWeightSet;

      // Capture for closure (replacing Class461)
      WoWClass playerClass = StyxWoW.Me.Class;
      // pointsSpent pinned LUA-SIDE as GetTalentTabInfo's 3rd return. The old retval-4 read was
      // previewPointsSpent (0 unless mid-talent-preview) — every tab read 0 and specIndex always
      // fell to 1, unnoticed because spec 1 matched the test characters (Balance druid, Elemental
      // shaman). Proven against a fully-talented 49 reading 0/0/0 (2026-07-06). groupIndex is
      // still required — without it even the 3rd return reads 0 for all tabs.
      string spent = Lua.GetReturnVal<string>(
        "local g = GetActiveTalentGroup and GetActiveTalentGroup() or 1 local r = '' " +
        "for i = 1, 3 do local _, _, p = GetTalentTabInfo(i, false, false, g) r = r .. (p or 0) .. ';' end return r", 0U);
      string[] spentParts = (spent ?? "").Split(';');
      List<int> talentPoints = new List<int>
      {
        spentParts.Length > 0 && int.TryParse(spentParts[0], out int t1) ? t1 : 0,
        spentParts.Length > 1 && int.TryParse(spentParts[1], out int t2) ? t2 : 0,
        spentParts.Length > 2 && int.TryParse(spentParts[2], out int t3) ? t3 : 0
      };
      int specIndex = talentPoints.IndexOf(talentPoints.Max()) + 1;

      // Find weight set matching class and spec
      WeightSetEx._cachedWeightSet = WeightSetEx._loadedWeightSets.FirstOrDefault<WeightSetEx>(
        ws => ws.Class == playerClass && ws.Specialization == specIndex);

      if (WeightSetEx._cachedWeightSet == null)
      {
        // Fall back to class-only match
        WeightSetEx._cachedWeightSet = WeightSetEx._loadedWeightSets.FirstOrDefault<WeightSetEx>(
          ws => ws.Class == playerClass);
        if (WeightSetEx._cachedWeightSet == null)
          Logging.WriteDebug("[WeightSet] Unable to find a weight set for your class. Please make sure you have all the required files");
      }

      if (WeightSetEx._cachedWeightSet != null && !string.IsNullOrEmpty(WeightSetEx._cachedWeightSet.Name))
        Logging.WriteDebug("[WeightSet] Selected weight set: {0}", (object) WeightSetEx._cachedWeightSet.Name);

      return WeightSetEx._cachedWeightSet;
    }
  }

  public Dictionary<Stat, float> StatScores { get; private set; }

  public WoWClass Class { get; private set; }

  public int Specialization { get; private set; }

  public string Name { get; private set; }

  public float EvaluateItem(string itemLink)
  {
    uint? nullable = WeightSetEx.ParseItemId(itemLink);
    if (!nullable.HasValue)
      return 0.0f;
    ItemStats itemStats = new ItemStats(itemLink);
    return this.EvaluateItem(Styx.WoWInternals.WoWObjects.ItemInfo.FromId(nullable.Value), itemStats);
  }

  public float EvaluateItem(WoWItem item, bool includeEnchants)
  {
    if ((WoWObject) item == (WoWObject) null || item.ItemInfo == null)
      return 0.0f;
    float totalScore = this.EvaluateItem(item.ItemInfo, item.ItemStats);
    if (includeEnchants)
    {
      for (uint enchantIndex = 0; enchantIndex < 3U; ++enchantIndex)
      {
        WoWItem.WoWItemEnchantment enchantment = item.GetEnchantment(enchantIndex);
        if (enchantment.IsValid)
        {
          for (int statIndex = 0; statIndex < 3; ++statIndex)
          {
            WoWItem.WoWItemStat stat = enchantment.GetStat(statIndex);
            if (stat != null)
              totalScore += this.GetStatScore(stat.Type.ToString(), (float) stat.Value);
          }
        }
      }
    }
    return totalScore;
  }

  public float EvaluateItem(WoWItem item) => this.EvaluateItem(item, false);

  public float EvaluateItem(Styx.WoWInternals.WoWObjects.ItemInfo itemInfo, ItemStats itemStats)
  {
    if (itemStats == null)
      return 0.0f;
    float totalScore = 0.0f;
    foreach (var kvp in itemStats.Stats)
    {
      totalScore += this.GetStatScore(kvp.Key.ToString(), (float) kvp.Value);
    }
    if (itemInfo != null)
    {
      totalScore = totalScore + this.GetStatScore(Stat.DPS, itemInfo.DPS) + this.GetStatScore(Stat.MinDamage, itemInfo.MinDamage) + this.GetStatScore(Stat.MaxDamage, itemInfo.MaxDamage) + this.GetStatScore(Stat.Armor, (float) itemInfo.Armor) + this.GetStatScore(Stat.HolyResistance, (float) itemInfo.HolyResistance) + this.GetStatScore(Stat.FrostResistance, (float) itemInfo.FrostResistance) + this.GetStatScore(Stat.FireResistance, (float) itemInfo.FireResistance) + this.GetStatScore(Stat.NatureResistance, (float) itemInfo.NatureResistance) + this.GetStatScore(Stat.ArcaneResistance, (float) itemInfo.ArcaneResistance) + this.GetStatScore(Stat.ShadowResistance, (float) itemInfo.ShadowResistance);
      WeightSetEx currentWeightSet = WeightSetEx.CurrentWeightSet;
      if (currentWeightSet != null)
      {
        float speedBaseline = 0.0f;
        if (currentWeightSet.StatScores.ContainsKey(Stat.SpeedBaseLine))
          speedBaseline = currentWeightSet.StatScores[Stat.SpeedBaseLine];
        if (currentWeightSet.StatScores.ContainsKey(Stat.Speed))
          totalScore += this.GetStatScore(Stat.Speed, (float) itemInfo.WeaponSpeed - speedBaseline * 1000f);
      }
      for (int index = 0; index < itemInfo.InternalInfo.SocketColor.Length; ++index)
      {
        Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags socketColorFlags = itemInfo.InternalInfo.SocketColor[index];
        if (socketColorFlags != Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.None)
        {
          if ((socketColorFlags & Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.Meta) != Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.None)
            totalScore += this.GetStatScore(Stat.MetaSocket, 1f);
          if ((socketColorFlags & Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.Red) != Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.None)
            totalScore += this.GetStatScore(Stat.RedSocket, 1f);
          if ((socketColorFlags & Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.Yellow) != Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.None)
            totalScore += this.GetStatScore(Stat.YellowSocket, 1f);
          if ((socketColorFlags & Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.Blue) != Styx.WoWInternals.WoWCache.WoWCache.SocketColorFlags.None)
            totalScore += this.GetStatScore(Stat.BlueSocket, 1f);
        }
      }
      // Fair tiebreak: otherwise-equal items (statless gear ties on the Armor floor too) resolve
      // by the game's own progression metric: item level, then quality. Scaled so stat scores —
      // including the default Armor floor — always dominate; sits above the armor-class nudge so
      // on-class items win pure ties.
      totalScore += itemInfo.Level * 0.01f + (int) itemInfo.Quality * 0.001f;
      if (itemInfo.ItemClass == WoWItemClass.Armor
          && itemInfo.ArmorClass != WoWItemArmorClass.Misc
          && itemInfo.ArmorClass != WoWItemArmorClass.None)
      {
        // Gentle nudge toward the class's armor type, NOT a hard divide. WotLK has no armor-spec stat
        // bonus, better itemization is already reflected in the stat score, armor value is minor while
        // leveling, and tanks weight Armor directly - so a mildly-better off-type piece (e.g. a caster
        // cloth item for a mail-wearer) can still win on stats. ~15% per tier off (diff 1 -> x0.85,
        // diff 3 -> x0.55) instead of the old /2, /3, /4 which threw away half the score for one tier.
        int armorClass = (int) itemInfo.ArmorClass;
        int wantedArmorClass = (int) this.GetWantedArmorClass();
        int diff = Math.Abs(wantedArmorClass - armorClass);
        totalScore *= Math.Max(1f - 0.15f * diff, 0.4f);
      }
    }
    return totalScore;
  }

  public float GetStatScore(Stat stat, float statPoints)
  {
    if (float.IsNaN(statPoints) || statPoints == 0f)
      return 0.0f;

    WeightSetEx currentWeightSet = WeightSetEx.CurrentWeightSet;

    // Use weight from loaded weight set when present
    if (currentWeightSet != null && currentWeightSet.StatScores != null && currentWeightSet.StatScores.ContainsKey(stat))
      return currentWeightSet.StatScores[stat] * statPoints;

    // Fall back to conservative defaults for weapon-related stats when no weight set defines them
    if (DefaultStatWeights.ContainsKey(stat))
      return DefaultStatWeights[stat] * statPoints;

    return 0.0f;
  }

  public float GetStatScore(string statName, float statPoints)
  {
    if (statName == null)
      throw new ArgumentNullException(nameof (statName));
    float statScore;
    try
    {
      if (statName.EndsWith("2"))
        statName = statName.Remove(statName.Length - 1, 1);
      statScore = this.GetStatScore((Stat) Enum.Parse(typeof (Stat), statName, true), statPoints);
    }
    catch (ArgumentException ex)
    {
      if (statName == "None")
      {
        statScore = 0.0f;
      }
      else
      {
        // Replaced StyxResources.Stat_name__0__is_unknown__Please_report_this_to_the_HB_team_ with hardcoded string
        Logging.Write("Stat name '{0}' is unknown. Please report this to the HB team.", (object) statName);
        statScore = 0.0f;
      }
    }
    return statScore;
  }

  public WoWItemArmorClass GetWantedArmorClass()
  {
    switch (StyxWoW.Me.Class)
    {
      case WoWClass.Warrior:
      case WoWClass.Paladin:
        return StyxWoW.Me.Level < 40 ? WoWItemArmorClass.Mail : WoWItemArmorClass.Plate;
      case WoWClass.Hunter:
      case WoWClass.Shaman:
        return StyxWoW.Me.Level < 40 ? WoWItemArmorClass.Leather : WoWItemArmorClass.Mail;
      case WoWClass.Rogue:
      case WoWClass.Druid:
        return WoWItemArmorClass.Leather;
      case WoWClass.Priest:
      case WoWClass.Mage:
      case WoWClass.Warlock:
        return WoWItemArmorClass.Cloth;
      case WoWClass.DeathKnight:
        return WoWItemArmorClass.Plate;
      default:
        return WoWItemArmorClass.None;
    }
  }

  private static uint? ParseItemId(string itemLink)
  {
    uint result;
    return uint.TryParse(new Regex("([0-9]\\d\\d+)", RegexOptions.IgnoreCase).Match(itemLink).Value, out result) ? new uint?(result) : new uint?();
  }

  private static void LoadWeightSets()
  {
    string str1 = Path.Combine(Logging.ApplicationPath, "Data");
    string str2 = Path.Combine(str1, "Weight Sets");
    if (Directory.Exists(str1) && Directory.Exists(str2))
    {
      List<WeightSetEx> weightSetExList = new List<WeightSetEx>();
      foreach (string file in Directory.GetFiles(Path.Combine(str1, str2)))
      {
        WeightSetEx weightSetEx = new WeightSetEx(XElement.Load(file));
        weightSetExList.Add(weightSetEx);
      }
      WeightSetEx._loadedWeightSets = (IEnumerable<WeightSetEx>) weightSetExList;
    }
    else
    {
      Logging.WriteDebug("Unable to find Data and/or 'Weight Sets' folder, cannot parse weight set's. ");
      // Initialize to empty collection to prevent null reference exceptions
      WeightSetEx._loadedWeightSets = Enumerable.Empty<WeightSetEx>();
    }
  }

  public override string ToString()
  {
    return $"Name:{this.Name} Class:{this.Class} Specialization:{this.Specialization}";
  }

  public void Dispose()
  {
    Lua.Events.DetachEvent("ACTIVE_TALENT_GROUP_CHANGED", new LuaEventHandlerDelegate(WeightSetEx.OnTalentGroupChanged));
  }
}
