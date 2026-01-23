using System;

namespace Styx
{
	/// <summary>
	/// NPC flags for WoW 3.3.5a (Build 12340).
	/// Read from UNIT_NPC_FLAGS descriptor.
	/// </summary>
	[Flags]
	public enum NpcFlags : uint
	{
		None = 0x0,
		Gossip = 0x1,
		QuestGiver = 0x2,
		Unk1 = 0x4,
		Unk2 = 0x8,
		Trainer = 0x10,
		TrainerClass = 0x20,
		TrainerProfession = 0x40,
		Vendor = 0x80,
		VendorAmmo = 0x100,
		VendorFood = 0x200,
		VendorPoison = 0x400,
		VendorReagent = 0x800,
		Repair = 0x1000,
		FlightMaster = 0x2000,
		SpiritHealer = 0x4000,
		SpiritGuide = 0x8000,
		Innkeeper = 0x10000,
		Banker = 0x20000,
		Petitioner = 0x40000,
		TabardDesigner = 0x80000,
		BattleMaster = 0x100000,
		Auctioneer = 0x200000,
		StableMaster = 0x400000,
		GuildBanker = 0x800000,
		SpellClick = 0x1000000,
		PlayerVehicle = 0x2000000,
		Mailbox = 0x4000000
	}
}
