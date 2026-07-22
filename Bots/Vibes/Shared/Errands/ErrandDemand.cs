using System;
using System.Collections.Generic;
using Styx;
using Styx.Helpers;
using Styx.Logic;
using Styx.Logic.Inventory;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.Logic.BehaviorTree;
using Styx.Combat.CombatRoutine;

namespace Bots.Vibes.Shared.Errands
{
    /// <summary>One thing the character owes, and why. Carries no destination — see <see cref="ErrandStop"/>.</summary>
    public readonly struct ErrandDemand
    {
        public readonly ErrandKind Kind;
        public readonly string Why;
        /// <summary>True for a demand worth serving only if the tour passes it anyway. A dedicated trip
        /// to mail three items is waste; a mailbox we walk past is free.</summary>
        public readonly bool Opportunistic;

        public ErrandDemand(ErrandKind kind, string why, bool opportunistic = false)
        {
            Kind = kind; Why = why; Opportunistic = opportunistic;
        }

        public override string ToString() => Opportunistic ? Kind + "?" : Kind.ToString();
    }

    /// <summary>
    /// Derives errands from the character's own state — bags, durability, coin, the mail payload.
    /// NEVER from geometry. A demand that is asked "is there a vendor near?" answers yes wherever a
    /// vendor happens to be, which is how a mail run fires with an empty bag and a bot re-walks a town:
    /// LevelBot's NeedTo* chain suppresses the NEED when no vendor is known and re-asserts it when one
    /// is, so the need tracks the map instead of the character.
    ///
    /// Every kind is additionally required to be SATISFIABLE at the character level — sellable items
    /// exist, the repair is affordable, the mail payload is non-empty. An errand with nothing to do
    /// never becomes a demand, so an empty trip is structurally impossible rather than merely unlikely.
    ///
    /// This deliberately duplicates the intent of LevelBot's private NeedTo* predicates. LevelBot's
    /// vendor path is frozen for its five other consumers, so the corrected copy lives here; the
    /// difference is not cosmetic (those consult geometry, these consult the character).
    /// </summary>
    public static class ErrandDemands
    {
        // Bag pressure is a PERCENTAGE of carried capacity, not a flat free-slot count: a leveler with
        // 40-60 slots is either never under a flat threshold or permanently under it depending on bags.
        // Carried from LevelBot.MailFreeSlotsPressurePct with its history (see Loot/CLAUDE.md).
        private const int MailFreeSlotsPressurePct = 25;

        // Ammo below one classic stack: the restock fires while the trip is still shootable rather
        // than after Auto Shot has already died. Carried from LevelBot.AmmoLowThreshold.
        private const int AmmoLowThreshold = 200;

        // Reading equipment durability and the ammo census cost real work; both change slowly. These
        // throttle a COST, not a decision, and they apply whether the answer was yes or no — they are
        // not memory of a failed attempt.
        private static readonly TimeSpan CensusEvery = TimeSpan.FromSeconds(8);
        private static DateTime _censusAt = DateTime.MinValue;
        private static ulong _repairCost;
        private static int _lowDuraPolls;
        private static int _ammoCount;
        private static WoWItemProjectileClass _ammoClass = WoWItemProjectileClass.None;
        private static int _sellable;
        private static int _mailPayload;

        /// <summary>Session state that must not survive a Start. Called from the botbase's Start.</summary>
        public static void Reset()
        {
            _censusAt = DateTime.MinValue;
            _repairCost = 0;
            _lowDuraPolls = 0;
            _ammoCount = 0;
            _ammoClass = WoWItemProjectileClass.None;
            _sellable = 0;
            _mailPayload = 0;
        }

        /// <summary>Force the next scan to re-measure. Called after a transaction: we just changed the
        /// very state the census caches, and a stale cargo count would re-assert an errand we just did.</summary>
        public static void Invalidate() => _censusAt = DateTime.MinValue;

        /// <summary>The cost of the pending repair as last measured, for the affordability canceller.</summary>
        public static ulong RepairCost => _repairCost;

        public static List<ErrandDemand> Scan()
        {
            var demands = new List<ErrandDemand>();
            LocalPlayer me = StyxWoW.Me;
            Profile profile = ProfileManager.CurrentProfile;
            if (me == null || profile == null) return demands;

            Census(me);

            // --- Sell: bags are filling AND something in them is actually vendorable. ---
            uint freeNormal = me.FreeNormalBagSlots;
            bool bagsTight = Vendors.ForceSell || freeNormal <= profile.MinFreeBagSlots;
            if (bagsTight && _sellable > 0)
                demands.Add(new ErrandDemand(ErrandKind.Sell,
                    string.Format("{0} free normal slot(s) <= {1}, {2} sellable", freeNormal, profile.MinFreeBagSlots, _sellable)));

            // --- Repair: worn AND affordable. An unaffordable repair is not a demand — planning a stop
            // for it is the all-night stand-at-the-vendor loop, and coin is askable before we walk. ---
            if (!Vendors.RepairDisabled)
            {
                double dura = me.LowestDurabilityPercent;
                bool worn = Vendors.ForceRepair || _lowDuraPolls >= 2;
                if (worn)
                {
                    if (_repairCost == 0 || me.Coinage > _repairCost)
                        demands.Add(new ErrandDemand(ErrandKind.Repair,
                            string.Format("durability {0:F0}% <= min {1:F0}%", dura * 100, profile.MinDurability * 100)));
                    else
                        Warn("repair", "[Errand] Gear is at {0:F0}% but the repair costs {1}c and we have {2}c — grinding for money instead.",
                            dura * 100, _repairCost, me.Coinage);
                }
            }

            // --- Ammo: a hunter with no ammo is a melee hunter. Outranks the 1g comfort gate below. ---
            if (_ammoClass != WoWItemProjectileClass.None && _ammoCount < AmmoLowThreshold)
            {
                if (me.Coinage >= 1000)
                    demands.Add(new ErrandDemand(ErrandKind.Ammo,
                        string.Format("{0} {1}s left", _ammoCount, _ammoClass)));
                else
                    Warn("ammo", "[Errand] {0} {1}s left and not enough coin to restock — {2}",
                        _ammoCount, _ammoClass,
                        _ammoCount == 0 ? "Auto Shot is dead, this hunter is meleeing." : "she goes dry soon.");
            }

            // --- Buy food/drink: only when a category is EMPTY (ran out), not merely low. ---
            if (me.Coinage >= 10000)
            {
                bool usesMana = me.PowerType == WoWPowerType.Mana || me.Class == WoWClass.Druid;
                bool needDrink = usesMana && CharacterSettings.Instance.DrinkAmount > 0 && Consumable.GetBestDrink(false) == null;
                bool needFood = CharacterSettings.Instance.FoodAmount > 0 && Consumable.GetBestFood(false) == null;
                if (Vendors.ForceBuy || needDrink || needFood)
                    demands.Add(new ErrandDemand(ErrandKind.Buy,
                        needDrink && needFood ? "out of food and drink" : needDrink ? "out of drink" : "out of food"));
            }

            // --- Train: the server told us new spells exist (level-up), or the user forced it. ---
            if ((CharacterSettings.Instance.TrainNewSkills && Vendors.NeedClassTraining) || Vendors.ForceTrainer)
                demands.Add(new ErrandDemand(ErrandKind.Train, "new class spells available"));

            // --- Mail: a payload is the precondition; bag pressure is what makes it worth a TRIP. ---
            // ForceMail promotes the demand to HARD, exactly as ForceSell/ForceRepair/ForceBuy/ForceTrainer do
            // above: an OPPORTUNISTIC mail demand is dropped outright by ErrandStop.InsertMail when the tour is
            // empty ("no route to be on"), so without this an explicit !forcemail on healthy bags is a silent
            // no-op — the flag had no reader anywhere in the tree.
            if (_mailPayload > 0 && me.Level >= profile.MinMailLevel)
            {
                uint total = TotalBagSlots(me);
                bool pressure = Vendors.ForceMail
                                || (total > 0 && me.FreeBagSlots < total * MailFreeSlotsPressurePct / 100.0);
                demands.Add(new ErrandDemand(ErrandKind.Mail,
                    string.Format("{0} item(s) to send, {1}/{2} slots free", _mailPayload, me.FreeBagSlots, total),
                    opportunistic: !pressure));
            }
            else if (Vendors.ForceMail)
            {
                // Asked to mail with nothing to send. Say WHY and drop the flag — silence reads as a broken
                // button, and a flag nothing can satisfy would otherwise stay armed for the session.
                Warn("forcemail", "[Errand] Told to mail but there is nothing to send — {0}.",
                    !MailboxService.MailingConfigured ? "no MailRecipient is set"
                    : me.Level < profile.MinMailLevel ? string.Format("level {0} is below MinMailLevel {1}", me.Level, profile.MinMailLevel)
                    : "nothing in bags is classified Mail (check the loot policy)");
                Vendors.ForceMail = false;
            }

            return demands;
        }

        /// <summary>
        /// Bags are full and NOTHING can be done about it — nothing sellable, nothing mailable. The
        /// loot the bot is now silently dropping is the whole point of the night, so this must be loud
        /// and it must repeat; the alternative the stock path takes is halting the bot from a worker
        /// thread, which is banned. Never suppresses a demand — there is no demand to suppress.
        /// </summary>
        public static void WarnIfStuckWithFullBags(List<ErrandDemand> demands)
        {
            LocalPlayer me = StyxWoW.Me;
            Profile profile = ProfileManager.CurrentProfile;
            if (me == null || profile == null) return;
            if (me.FreeNormalBagSlots > profile.MinFreeBagSlots) return;
            foreach (ErrandDemand d in demands)
                if (d.Kind == ErrandKind.Sell || (d.Kind == ErrandKind.Mail && !d.Opportunistic))
                    return;

            Warn("fullbags",
                "[Errand] Bags are full ({0} free) and nothing in them can be sold or mailed — loot is being DROPPED. "
                + "Widen the sell policy or set a MailRecipient.", me.FreeNormalBagSlots);
        }

        private static void Census(LocalPlayer me)
        {
            if (DateTime.UtcNow < _censusAt) return;
            _censusAt = DateTime.UtcNow.Add(CensusEvery);

            // The two payload resolvers belong here rather than in Scan: the mail one runs a Lua
            // round-trip per carried item, which is why the old HaveItemsToMail was unusable as a gate.
            // Cargo does not change fast enough for the 8s granularity to matter.
            _sellable = Vendors.ResolveSellPayload().Length;
            _mailPayload = MailboxService.MailingConfigured ? Vendors.ResolveMailPayload().Length : 0;

            // An equipped item's Durability descriptor occasionally reads 0 on the first poll after attach
            // (MaxDurability populated, Durability not) — a phantom "broken" that commits a wasted trip. A
            // real low durability persists across polls; a stale read does not. Carried from NeedToRepair.
            Profile profile = ProfileManager.CurrentProfile;
            bool low = profile != null && me.LowestDurabilityPercent <= profile.MinDurability;
            _lowDuraPolls = low ? _lowDuraPolls + 1 : 0;

            // Outside a merchant frame GetEstimatedRepairCost reads 0; keep the last real figure so the
            // affordability gate has something to compare against.
            try
            {
                var cost = me.GetEstimatedRepairCost();
                if (cost.TotalCoppers > 0) _repairCost = (ulong)cost.TotalCoppers;
            }
            catch (Exception ex)
            {
                Logging.WriteDebug("[Errand] repair-cost read failed: {0}", ex.Message);
            }

            if (me.Class == WoWClass.Hunter)
            {
                WoWItemProjectileClass was = _ammoClass;
                _ammoClass = Consumable.NeededAmmoClass();
                _ammoCount = Consumable.GetAmmoCount(_ammoClass);
                // A ranged-weapon swap makes every "stocks no <old class>" verdict meaningless.
                if (was != WoWItemProjectileClass.None && was != _ammoClass)
                    ProfileManager.CurrentProfile?.VendorManager?.Blacklist.RemovePurpose(Styx.Logic.Profiles.Vendor.VendorType.Ammo);
            }
            else
            {
                _ammoClass = WoWItemProjectileClass.None;
            }
        }

        private static uint TotalBagSlots(LocalPlayer me)
        {
            try
            {
                uint total = me.Inventory.Backpack.Slots;
                for (uint i = 0U; i < 4U; i++)
                {
                    WoWContainer bag = me.GetBagAtIndex(i);
                    if (bag != null) total += bag.Slots;
                }
                return total;
            }
            catch { return 0; }
        }

        // A condition that blocks an errand all night would say so once and go silent under an
        // edge-triggered log. These are the cases worth repeating: unmissable in a morning review,
        // too rare to flood it.
        private static readonly TimeSpan WarnEvery = TimeSpan.FromMinutes(5);
        private static readonly Dictionary<string, DateTime> _warnedAt = new Dictionary<string, DateTime>();

        private static void Warn(string key, string fmt, params object[] args)
        {
            if (_warnedAt.TryGetValue(key, out DateTime at) && DateTime.UtcNow - at < WarnEvery)
                return;
            _warnedAt[key] = DateTime.UtcNow;
            Logging.Write(System.Drawing.Color.Orange, fmt, args);
        }
    }
}
