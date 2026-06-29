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
				NeedClassTraining = false;
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
		public static void MailAllItems()
		{
			var items = new List<WoWItem>();
			items.AddRange(InventoryManager.GetItemsToMail());

			if (OnMailItems != null)
			{
				var args = new MailItemsEventArgs { AdditionalItems = new List<WoWItem>() };

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

			_mailFrame.SendMailWithManyAttachments(LevelbotSettings.Instance.MailRecipient, 0, items.ToArray());
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
		}

		/// <summary>
		/// Sells all items according to profile settings.
		/// </summary>
		public static void SellAllItems()
		{
			ItemQuality qualityMask = ItemQuality.None;
			Profile? currentProfile = ProfileManager.CurrentProfile;

			if (currentProfile == null)
				return;

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

			var protectedNames = new List<string>();
			var protectedIds = new List<uint>();

			protectedNames.AddRange(ProtectedItemsManager.GetAllItemNames());
			protectedIds.AddRange(ProtectedItemsManager.GetAllItemIds());

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
					IdExceptions = new List<uint>()
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

				protectedNames.AddRange(args.NameExceptions);
				protectedIds.AddRange(args.IdExceptions);
			}

			_merchantFrame.SellItemQualities(qualityMask, protectedNames, protectedIds);
			ForceSell = false;
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

			// Handle automatic food/drink buying based on settings (HB 4.3.4 logic)
			Vendor asVendor = BotPoi.Current.AsVendor;
			if (asVendor == null || (asVendor.Type != Vendor.VendorType.Food && asVendor.Type != Vendor.VendorType.Restock))
				return;

			if (!_merchantFrame.IsVisible || _merchantFrame.MerchantNumItems <= 0)
				return;

			bool usesMana = StyxWoW.Me.PowerType == WoWPowerType.Mana || StyxWoW.Me.Class == WoWClass.Druid;
			int drinkAmount = CharacterSettings.Instance.DrinkAmount;
			int foodAmount = CharacterSettings.Instance.FoodAmount;

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



