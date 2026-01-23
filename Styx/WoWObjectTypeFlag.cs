using System;

namespace Styx
{
	[Flags]
	public enum WoWObjectTypeFlag
	{
		Object = 1,
		Item = 2,
		Container = 4,
		Unit = 8,
		Player = 16,
		GameObject = 32,
		DynamicObject = 64,
		Corpse = 128,
		AiGroup = 256,
		AreaTrigger = 512
	}
}
