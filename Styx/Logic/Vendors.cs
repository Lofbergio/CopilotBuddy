using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Styx.Combat.CombatRoutine;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.MailBox;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Inventory.Frames.Trainer;
using Styx.Logic.POI;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic
{
	/// <summary>
	/// Provides functionality for interacting with vendors, trainers, and mailboxes.
	/// </summary>
	public static class Vendors
	{
		private static readonly TrainerFrame _trainerFrame;
		private static readonly MerchantFrame _merchantFrame;
		private static readonly MailFrame _mailFrame;
		private static readonly GossipFrame _gossipFrame;

		public static VendorItemsEventHandler? OnVendorItems;
		public static EventHandler? OnRepairItems;
		public static MailItemsEventHandler? OnMailItems;
		public static BuyItemsEventHandler? OnBuyItems;

		public static bool ForceTrainer { get; set; }
		public static bool ForceSell { get; set; }
		public static bool ForceRepair { get; set; }
		public static bool ForceMail { get; set; }
		public static bool ForceBuy { get; set; }
		public static bool NeedClassTraining { get; set; }

		/// <summary>
		/// Gets or sets whether repair functionality is disabled.
		/// </summary>
		public static bool RepairDisabled { get; set; }

		static Vendors()
		{
			_trainerFrame = new TrainerFrame();
			_merchantFrame = new MerchantFrame();
			_mailFrame = new MailFrame();
			_gossipFrame = new GossipFrame();
			BotEvents.Player.OnLevelUp += OnLevelUp;
			// HB 3.3.5a: NeedClassTraining is ONLY set on level up, not on startup
			// This prevents the bot from going to trainer when all spells are already learned
		}

		/// <summary>
		/// Gets the nearest flight master with taxi available.
		/// </summary>
		public static WoWUnit? NearestFlightMerchant
		{
			get
			{
				// leverage cached units for better performance
				return ObjectManager.CachedUnits
					.Where(u => u.IsFlightMaster && u.InteractType == WoWInteractType.TaxiPathAvailable)
					.OrderBy(u => u.Distance)
					.FirstOrDefault();
			}
		}

		private static void OnLevelUp(BotEvents.Player.LevelUpEventArgs args)
		{
			// Use CharacterSettings because it's bound to the UI checkbox
			if (!CharacterSettings.Instance.TrainNewSkills)
				return;

			// In 3.3.5a, new spells available at even levels
			if (args != null && args.NewLevel % 2 == 0)
			{
				NeedClassTraining = true;
				Logging.Write("New spells available at trainer (level {0})!", args.NewLevel);
			}
		}

		/// <summary>
		/// Trains all available skills at the current trainer.
		/// </summary>
		public static void TrainSkills()
		{
			var result = _trainerFrame.BuyAll();

			// Only consider training done when nothing affordable-and-available remains. If spells were
			// skipped purely for cost, keep NeedClassTraining set so NeedToTrain() re-fires after the next
			// sell run earns money (i.e. training is re-evaluated on every sell run, for free).
			if (result.UnaffordableRemaining == 0)
			{
				NeedClassTraining = false;
				// Tier-limited servers: an outgrown trainer (e.g. starting-area, teaches <= lvl 6) has an
				// EMPTY available list — indistinguishable from "fully trained". Name it in the log so a
				// missing-spells report is a one-log diagnosis, not a mystery.
				if (result.Bought == 0)
					Logging.Write("Trainer had nothing to teach at level {0} — if spells are missing, this trainer's teach list may cap below your level.", StyxWoW.Me.Level);
			}
			else
				Logging.Write("Could not afford {0} spell(s) — will retry training after earning more money.", result.UnaffordableRemaining);

			ForceTrainer = false;

			// Best-effort refresh. BuyTrainerService(0) is async — the server response
			// (and the WoW client's spellbook update) arrives ~30-50ms later. If this
			// call short-circuits because NumKnownSpells hasn't changed yet, the
			// LEARNED_SPELL_IN_TAB Lua event handler in SpellManager will catch it.
			Styx.Logic.Combat.SpellManager.Refresh();
		}

		/// <summary>
		/// Mails all items that should be mailed.
		/// </summary>
		/// <summary>
		/// THE answer to "what would we mail right now" — the profile-quality set unioned with every
		/// OnMailItems subscriber's contribution. Both the mail GATE and the mail EXECUTOR must ask this
		/// one function: a gate that counts a different set than the executor attaches produces a log line
		/// that LIES, which is worse than the silent empty run it replaced. Pure apart from the optional
		/// logging — subscribers only append to the args.
		/// </summary>
		/// <param name="verbose">Log per-item detail. FALSE from the gate (it runs per tick).</param>
		public static WoWItem[] ResolveMailPayload(bool verbose = false)
		{
			var items = new List<WoWItem>();
			items.AddRange(InventoryManager.GetItemsToMail(verbose));

			if (OnMailItems != null)
			{
				var args = new MailItemsEventArgs { AdditionalItems = new List<WoWItem>(), Verbose = verbose };

				foreach (Delegate handler in OnMailItems.GetInvocationList())
				{
					try
					{
						handler.DynamicInvoke(args);
					}
					catch (Exception ex)
					{
						Logging.WriteException(ex);
						continue;
					}

					foreach (var item in args.AdditionalItems)
					{
						if (!items.Contains(item))
							items.Add(item);
					}
					args.AdditionalItems.Clear();
				}
			}

			return items.ToArray();
		}

		public static void MailAllItems()
		{
			WoWItem[] items = ResolveMailPayload(verbose: true);
			if (items.Length == 0)
			{
				// The gate is supposed to make this unreachable. If it fires, the gate and this executor
				// disagree about the payload — say so loudly rather than sending an empty mail.
				Logging.Write(System.Drawing.Color.Orange,
					"[Mail] asked to mail with an empty payload — the mail gate and ResolveMailPayload disagree.");
				ForceMail = false;
				return;
			}

			Logging.Write("Mailing {0} item(s) to {1}.", items.Length, LevelbotSettings.Instance.MailRecipient);
			_mailFrame.SendMailWithManyAttachments(LevelbotSettings.Instance.MailRecipient, 0, items);
			ForceMail = false;
		}

		/// <summary>
		/// Repairs all items.
		/// </summary>
		public static void RepairAllItems()
		{
			OnRepairItems?.Invoke(null, EventArgs.Empty);
			_merchantFrame.RepairAllItems();
			ForceRepair = false;

			// Repair is its own errand branch, so without this a hunter could stand at an open merchant
			// stocking her exact ammo and walk away dry. Every merchant window is a chance to top up —
			// that's what keeps the dedicated ammo trip rare.
			BuyAmmoIfNeeded();
		}

		/// <summary>
		/// THE answer to "what would we sell right now", in the same shape as
		/// <see cref="ResolveMailPayload"/> and for the same reason: a gate that decides a sell trip
		/// from a different set than the executor disposes of is how a bot walks to a vendor with a bag
		/// of unsellable greys and comes back with them. Pure and Lua-free — the executor's own sweep is
		/// Lua (it needs the open merchant frame), so this mirrors its INPUTS (the profile quality mask
		/// and every protection SellAllItems applies) against BagItems rather than re-deriving a policy.
		///
		/// SellPrice &gt; 0 is the server's own statement that an item is vendorable at all; without it
		/// the count includes junk no merchant accepts and the trip looks satisfiable when it is not.
		/// </summary>
		public static WoWItem[] ResolveSellPayload()
		{
			if (!TryBuildSellFilter(false, out ItemQuality qualityMask, out List<string> protectedNames, out List<uint> protectedIds))
				return Array.Empty<WoWItem>();

			var payload = new List<WoWItem>();
			foreach (WoWItem item in ObjectManager.Me.BagItems)
			{
				ItemInfo? info = item?.ItemInfo;
				if (info == null || info.SellPrice <= 0)
					continue;
				if ((qualityMask & (ItemQuality)(1 << (int)info.Quality)) == ItemQuality.None)
					continue;
				if (protectedIds.Contains(item.Entry))
					continue;
				if (!string.IsNullOrEmpty(item.Name) && protectedNames.Contains(item.Name.ToLower()))
					continue;
				payload.Add(item);
			}
			return payload.ToArray();
		}

		/// <summary>
		/// The quality mask + protection sets SellAllItems sells by. Extracted so the gate and the
		/// executor cannot drift into two policies. False when there is no profile to read a mask from.
		/// </summary>
		private static bool TryBuildSellFilter(bool verbose, out ItemQuality qualityMask, out List<string> protectedNames, out List<uint> protectedIds)
		{
			qualityMask = ItemQuality.None;
			protectedNames = new List<string>();
			protectedIds = new List<uint>();

			Profile? currentProfile = ProfileManager.CurrentProfile;
			if (currentProfile == null)
				return false;

			if (currentProfile.SellGrey)
				qualityMask |= ItemQuality.Poor;
			if (currentProfile.SellWhite)
				qualityMask |= ItemQuality.Common;
			if (currentProfile.SellGreen)
				qualityMask |= ItemQuality.Uncommon;
			if (currentProfile.SellBlue)
				qualityMask |= ItemQuality.Rare;
			if (currentProfile.SellPurple)
				qualityMask |= ItemQuality.Epic;

			protectedNames.AddRange(ProtectedItemsManager.GetAllItemNames());
			protectedIds.AddRange(ProtectedItemsManager.GetAllItemIds());

			// Never sell ammo: the quality mask would take white arrows/bullets with SellWhite, and
			// the hunter restock would just buy them back (a dry hunter is a melee hunter).
			foreach (WoWItem bagItem in ObjectManager.Me.BagItems)
			{
				var bagInfo = bagItem != null ? bagItem.ItemInfo : null;
				if (bagInfo != null && bagInfo.ItemClass == WoWItemClass.Projectile && !protectedIds.Contains(bagItem.Entry))
					protectedIds.Add(bagItem.Entry);
			}

			// Protect food and drink
			if (uint.TryParse(LevelbotSettings.Instance.FoodName, out uint foodId))
				protectedIds.Add(foodId);
			else
				protectedNames.Add(LevelbotSettings.Instance.FoodName.ToLower());

			if (uint.TryParse(LevelbotSettings.Instance.DrinkName, out uint drinkId))
				protectedIds.Add(drinkId);
			else
				protectedNames.Add(LevelbotSettings.Instance.DrinkName.ToLower());

			// Fire event for plugins to add exclusions
			if (OnVendorItems != null)
			{
				var args = new SellItemsEventArgs
				{
					NameExceptions = new List<string>(),
					IdExceptions = new List<uint>(),
					Verbose = verbose
				};

				foreach (Delegate handler in OnVendorItems.GetInvocationList())
				{
					try
					{
						handler.DynamicInvoke(args);
					}
					catch (Exception ex)
					{
						Logging.WriteException(ex);
						args.NameExceptions.Clear();
						args.IdExceptions.Clear();
						continue;
					}

					foreach (string name in args.NameExceptions)
					{
						if (!protectedNames.Contains(name))
							protectedNames.Add(name);
					}

					foreach (uint id in args.IdExceptions)
					{
						if (!protectedIds.Contains(id))
							protectedIds.Add(id);
					}

					args.NameExceptions.Clear();
					args.IdExceptions.Clear();
				}
			}
			return true;
		}

		/// <summary>
		/// Sells all items according to profile settings.
		/// </summary>
		public static void SellAllItems()
		{
			if (!TryBuildSellFilter(true, out ItemQuality qualityMask, out List<string> protectedNames, out List<uint> protectedIds))
				return;

			_merchantFrame.SellItemQualities(qualityMask, protectedNames, protectedIds);
			ForceSell = false;

			// Opportunistic ammo top-up while a merchant is already open (and selling just funded it) —
			// saves the hunter a dedicated ammo trip when the sell vendor stocks projectiles.
			BuyAmmoIfNeeded();
		}

		// Refill to a comfortable buffer (~1.5 WotLK stacks); one grind session burns 1-2k rounds.
		private const int AmmoRestockTarget = 1400;

		/// <summary>
		/// Buys the best usable projectile of the class the equipped ranged weapon consumes, up to
		/// AmmoRestockTarget, from the open merchant. No-op for non-hunters, full pouches, or
		/// merchants without matching ammo. Never a hard requirement — a barren vendor just logs.
		/// </summary>
		private static void BuyAmmoIfNeeded()
		{
			try
			{
				var needed = Consumable.NeededAmmoClass();
				if (needed == WoWItemProjectileClass.None)
					return;
				if (!_merchantFrame.IsVisible || _merchantFrame.MerchantNumItems <= 0)
					return;
				int have = Consumable.GetAmmoCount(needed);
				if (have >= AmmoRestockTarget)
					return;

				// Best = highest RequiredLevel we can use (ammo grades scale strictly by level).
				// carriesClass tracks whether the class is stocked AT ALL, so the two ways to come up
				// empty stay distinguishable below — they expire differently.
				int bestIndex = -1, bestReq = -1;
				bool carriesClass = false;
				ItemInfo bestInfo = null;
				foreach (var mi in _merchantFrame.GetAllMerchantItems())
				{
					ItemInfo info = ItemInfo.FromId(mi.ItemId);
					if (info == null || info.ItemClass != WoWItemClass.Projectile || info.ProjectileClass != needed)
						continue;
					carriesClass = true;
					if (info.RequiredLevel > StyxWoW.Me.Level || info.RequiredLevel <= bestReq)
						continue;
					bestReq = info.RequiredLevel;
					bestIndex = mi.Index;
					bestInfo = info;
				}
				if (bestIndex < 0)
				{
					// The open merchant window is the authority on stock — the AmmoVendor npcflag that
					// routed us here is only a UI hint (General Supplies NPCs carry it and sell no
					// projectiles). Record the refusal against the NPC or the ammo errand re-resolves
					// this same nearest vendor every tick: 164 POI round-trips in 28s, log 2026-07-20_1029.
					BanFromAmmoErrand(needed, carriesClass);
					return;
				}

				int stack = Math.Max(1, bestInfo.MaxStackSize);
				int stacks = (AmmoRestockTarget - have + stack - 1) / stack;
				int bought = 0;
				for (int i = 0; i < stacks; i++)
				{
					if (!_merchantFrame.BuyItem(bestIndex, stack))
						break;   // out of money — keep what we got
					bought++;
				}
				if (bought > 0)
					Logging.Write("Restocked ammo: {0} x{1} stack(s) of {2} (had {3} rounds).", bestInfo.Name, bought, stack, have);
			}
			catch (Exception ex)
			{
				Logging.WriteException(ex);
			}
		}

		/// <summary>
		/// Records that the open merchant can't serve an ammo restock, so the resolver moves to the
		/// next-nearest instead of re-picking this one. Scoped to the Ammo errand only — a barren
		/// General Supplies NPC still buys our loot, and GetClosestVendor(Sell) must keep finding it.
		/// Exhausting the candidates is the terminating case, not a failure: NeedToBuy already gates
		/// the ammo run on a non-null ammo vendor, so it simply stops asking.
		/// </summary>
		private static void BanFromAmmoErrand(WoWItemProjectileClass needed, bool carriesClass)
		{
			var vm = ProfileManager.CurrentProfile?.VendorManager;
			WoWUnit merchant = _merchantFrame.Merchant;
			Vendor vendor = merchant != null
				? new Vendor(merchant, Vendor.VendorType.Ammo)
				: BotPoi.Current?.AsVendor;

			if (vm == null || vendor == null)
			{
				Logging.WriteDebug("[Ammo] merchant stocks no usable {0}s ({1} items) — no vendor context to record it against.",
					needed, _merchantFrame.MerchantNumItems);
				return;
			}

			// Stock is static for the session, so "carries no {0}s at all" can't change under us — a TTL
			// would just re-walk us here on a timer. All-too-high-level IS transient (we out-level it),
			// so that one expires. A ranged-weapon swap invalidates both; LevelBot clears them on that edge.
			if (carriesClass)
				vm.Blacklist.Add(vendor, Vendor.VendorType.Ammo);
			else
				vm.Blacklist.AddPermanent(vendor, Vendor.VendorType.Ammo);

			Logging.Write(System.Drawing.Color.Orange,
				"[Ammo] {0} stocks no usable {1}s ({2} items, {3}) — skipping it for ammo runs; the resolver picks another.",
				vendor.Name, needed, _merchantFrame.MerchantNumItems,
				carriesClass ? "all above our level, will retry" : "carries none at all");
		}

		/// <summary>
		/// Buys items from vendor based on OnBuyItems event handlers and food/drink settings.
		/// Ported from HB 4.3.4.
		/// </summary>
		public static void BuyItems()
		{
			var itemsToBuy = new Dictionary<uint, int>();

			// Handle OnBuyItems event
			if (OnBuyItems != null)
			{
				var args = new BuyItemsEventArgs();
				foreach (Delegate handler in OnBuyItems.GetInvocationList())
				{
					try
					{
						handler.DynamicInvoke(args);
						foreach (var kvp in args.BuyItemsIds)
						{
							if (!itemsToBuy.ContainsKey(kvp.Key))
								itemsToBuy.Add(kvp.Key, kvp.Value);
						}
					}
					catch (Exception ex)
					{
						Logging.WriteException(ex);
						args.BuyItemsIds.Clear();
					}
				}
			}

			if (itemsToBuy.Count > 0)
			{
				foreach (var merchantItem in _merchantFrame.GetAllMerchantItems())
				{
					if (itemsToBuy.ContainsKey(merchantItem.ItemId))
						_merchantFrame.BuyItem(merchantItem.Index, itemsToBuy[merchantItem.ItemId]);
				}
			}

			// Hunter ammo restock — before the food branch's vendor-type early-return, so a run
			// routed to an Ammo-type vendor still buys (and any merchant stocking projectiles works).
			BuyAmmoIfNeeded();

			// Handle automatic food/drink buying based on settings (HB 4.3.4 logic)
			Vendor asVendor = BotPoi.Current.AsVendor;
			if (asVendor == null || (asVendor.Type != Vendor.VendorType.Food && asVendor.Type != Vendor.VendorType.Restock))
				return;

			if (!_merchantFrame.IsVisible || _merchantFrame.MerchantNumItems <= 0)
				return;

			bool usesMana = StyxWoW.Me.PowerType == WoWPowerType.Mana || StyxWoW.Me.Class == WoWClass.Druid;
			// Buy only the SHORTFALL: subtract usable food/drink already in bags so a restock run (often triggered
			// by running out of just ONE category) doesn't also buy a full stack of the other you're already
			// sitting on — the "bought 20 Snapvine Watermelon with 23 Mutton Chops in the bags" bug. 0 ⇒ skip it.
			int drinkAmount = Math.Max(0, CharacterSettings.Instance.DrinkAmount - Consumable.GetDrinkCount());
			int foodAmount = Math.Max(0, CharacterSettings.Instance.FoodAmount - Consumable.GetFoodCount());

			// Already stocked (a sibling category triggered the trip) → buy nothing, and DON'T fall through to the
			// "bought nothing ⇒ blacklist this vendor" path below, which would wrongly poison a fine vendor.
			int neededDrink = usesMana ? drinkAmount : 0;
			if (foodAmount <= 0 && neededDrink <= 0)
			{
				Logging.WriteDebug("[BuyItems] already at target food/drink — nothing to buy.");
				ForceBuy = false;
				BotPoi.Clear("Already stocked");
				return;
			}

			// Find + buy food/drink via Lua (our memory item-cache reader can't see vendor-only items here;
			// the game's tooltip can). Returns "drink~q|food~q|warm/total|diag". The client item-tooltip cache
			// is often cold for a second after the merchant opens — the tooltip is then just the name line and
			// nothing detects as food/water — so re-scan until it warms before deciding the vendor is barren.
			// This stays inside one BuyItems call (the Buy behavior clears the POI right after we return).
			string drinkPart = string.Empty, foodPart = string.Empty, warmInfo = string.Empty, diag = string.Empty;
			for (int attempt = 0; attempt < 6; attempt++)   // ~3s budget for the tooltip cache to warm
			{
				string bought = _merchantFrame.BuyBestFoodDrink(usesMana, foodAmount, drinkAmount);
				string[] parts = (bought ?? string.Empty).Split('|');
				drinkPart = parts.Length > 0 ? parts[0] : string.Empty;
				foodPart = parts.Length > 1 ? parts[1] : string.Empty;
				warmInfo = parts.Length > 2 ? parts[2] : string.Empty;
				diag = parts.Length > 3 ? parts[3] : string.Empty;
				if (drinkPart.Length > 0 || foodPart.Length > 0)
					break;                                  // bought something — done
				bool cold = warmInfo.StartsWith("0/") && !warmInfo.StartsWith("0/0");
				if (!cold)
					break;                                  // tooltips warm but genuinely no food/water here
				StyxWoW.Sleep(500);                         // cold cache → wait and re-scan (no purchase on a cold pass)
			}

			Logging.WriteDebug("[FoodScan] {0}: drink='{1}' food='{2}' warm={3} | {4}",
				BotPoi.Current.Name, drinkPart, foodPart, warmInfo, diag);

			// If the vendor sells neither food nor water, blacklist it so we don't keep returning.
			if (drinkPart.Length == 0 && foodPart.Length == 0)
			{
				Logging.Write("Vendor does not sell food or water ({0} items, warm {1}). Blacklisting it. [{2}]",
					_merchantFrame.MerchantNumItems, warmInfo, diag);
				ProfileManager.CurrentProfile?.VendorManager?.Blacklist.Add(BotPoi.Current.AsVendor);
				BotPoi.Clear("Blacklisted Vendor");
				return;
			}

			LogBought(drinkPart, drinkAmount);
			LogBought(foodPart, foodAmount);

			StyxWoW.Sleep(2000);
			_merchantFrame.Close();
			ForceBuy = false;
			BotPoi.Clear("Restocked");
		}

		// part is "Name~perBuyQuantity" from BuyBestFoodDrink; logs what we actually purchased so the
		// per-buy quantity (the 5x stacking) is visible and verifiable.
		private static void LogBought(string part, int desiredItems)
		{
			if (string.IsNullOrEmpty(part))
				return;
			string[] seg = part.Split('~');
			string name = seg[0];
			int q = seg.Length > 1 && int.TryParse(seg[1], out int qq) && qq > 0 ? qq : 1;
			int buys = (int)Math.Ceiling(desiredItems / (double)q);
			Logging.Write("Buying {0} ({1}/buy x {2} buys = {3} items)", name, q, buys, buys * q);
		}
	}
}



