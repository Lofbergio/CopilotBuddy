using System;

namespace Styx
{
	[Flags]
	public enum UnitNPCFlags
	{
		None = 0,
		Gossip = 1,
		Questgiver = 2,
		AnyTrainer = 96,
		Unk1 = 4,
		Unk2 = 8,
		Trainer = 16,
		ClassTrainer = 32,
		ProfessionTrainer = 64,
		Vendor = 128,
		AmmoVendor = 256,
		FoodVendor = 512,
		PoisionVendor = 1024,
		ReagentVendor = 2048,
		AnyVendor = 3968,
		Repair = 4096,
		Flightmaster = 8192,
		Spirithealer = 16384,
		Spiritguide = 32768,
		Innkeeper = 65536,
		Banker = 131072,
		Petitioner = 262144,
		TarbardDesigner = 524288,
		Battlemaster = 1048576,
		Auctioneer = 2097152,
		Stablemaster = 4194304,
		GuildBanker = 8388608,
		SpellClick = 16777216,
		Guard = 268435456
	}
}
