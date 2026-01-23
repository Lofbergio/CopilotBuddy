using System;

namespace Styx
{
	[Flags]
	public enum PulseFlags : uint
	{
		Objects = 1U,
		Plugins = 2U,
		BotEvents = 8U,
		WoWChat = 16U,
		Targeting = 32U,
		Looting = 64U,
		InfoPanel = 128U,
		Lua = 256U,
		All = 507U,
		[Obsolete("Questing no longer has a pulsator - it has been removed. Use All instead of this.")]
		AllExceptQuesting = 507U
	}
}
