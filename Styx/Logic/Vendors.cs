using System;
using System.Collections.Generic;
using System.Linq;
using Styx.Helpers;
using Styx.Logic.Inventory;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.MailBox;
using Styx.Logic.Inventory.Frames.Merchant;
using Styx.Logic.Inventory.Frames.Trainer;
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
		}

		/// <summary>
		/// Gets the nearest flight master with taxi available.
		/// </summary>
		public static WoWUnit? NearestFlightMerchant
		{
			get
			{
				return ObjectManager.GetObjectsOfType<WoWUnit>()
					.Where(u => u.IsFlightMaster && u.InteractType == WoWInteractType.TaxiPathAvailable)
					.OrderBy(u => u.Distance)
					.FirstOrDefault();
			}
		}

		private static void OnLevelUp(BotEvents.Player.LevelUpEventArgs args)
		{
			if (LevelbotSettings.Instance.TrainNewSkills)
			{
				NeedClassTraining = args.NewLevel % 2 == 0;
			}
		}

		/// <summary>
		/// Trains all available skills at the current trainer.
		/// </summary>
		public static void TrainSkills()
		{
			_trainerFrame.BuyAll();
			NeedClassTraining = false;
			ForceTrainer = false;
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
		/// Buys items from vendor based on OnBuyItems event handlers.
		/// </summary>
		public static void BuyItems()
		{
			var itemsToBuy = new Dictionary<uint, int>();

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
		}
	}
}



