using System;
using System.Collections.Generic;
using System.Threading;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Provides quest-related functionality including completed quest tracking.
	/// </summary>
	public static class Questing
	{
		// Completed-quest linked-list head (3.3.5a 12340). VERIFIED live 2026-07-12 by OffsetDoctor:
		// walking 0xACFDF4 yielded the character's 1 completed quest; the old 0xAD0DF4 (a 0x1000 typo,
		// its "11337204" comment already pointed at 0xACFDF4) walked into garbage. Matches StyxWoW.cs:270.
		private const uint CompletedQuestsPtr = 0xACFDF4; // 11337204

		// CGQuestLog__GetQuestIDByIndex function address (3.3.5a 12340)
		private const uint CGQuestLogGetQuestIDByIndex = 0x5E3E40; // 6174784

		/// <summary>
		/// Gets all completed quest IDs for the current character.
		/// </summary>
		/// <returns>HashSet of completed quest IDs, or null if failed.</returns>
		public static HashSet<uint>? GetCompletedQuestIDs()
		{
			var wow = ObjectManager.Wow;
			if (wow == null)
				return null;

			uint ptr = wow.Read<uint>(CompletedQuestsPtr);

			// If not ready, query and wait
			if (ptr == 0 || (ptr & 1) != 0)
			{
				QueryQuestsCompleted();
				StyxWoW.Sleep(100);

				ptr = wow.Read<uint>(CompletedQuestsPtr);

				if (ptr == 0 || (ptr & 1) != 0)
				{
					int startTime = Environment.TickCount;
					int elapsed;

					while ((elapsed = Environment.TickCount - startTime) < 3000)
					{
						if (ptr != 0 && (ptr & 1) == 0)
							break;

						StyxWoW.Sleep(100);
						ptr = wow.Read<uint>(CompletedQuestsPtr);
					}

					if (elapsed >= 3000)
						return null;
				}
			}

			// Read completed quests from linked list
			var completedQuests = new HashSet<uint>();

			while (ptr != 0 && (ptr & 1) == 0)
			{
				uint questId = wow.Read<uint>(ptr + 8);
				if (!completedQuests.Contains(questId))
				{
					completedQuests.Add(questId);
				}

				ptr = wow.Read<uint>(ptr + 4);
			}

			return completedQuests;
		}

		/// <summary>
		/// Queries the server for completed quests.
		/// </summary>
		public static void QueryQuestsCompleted()
		{
			Lua.DoString("QueryQuestsCompleted()");
		}

		/// <summary>
		/// Gets quest ID by index in quest log using native function.
		/// </summary>
		internal static int GetQuestIDByIndex(uint index)
		{
			var executor = ObjectManager.Executor;
			if (executor == null)
				throw new Exception("Invalid executor");

			lock (executor.AssemblyLock)
			{
				executor.Clear();
				executor.AddLine("push {0}", index);
				executor.AddLine("call {0}", CGQuestLogGetQuestIDByIndex);
				executor.AddLine("add esp, 0x4");
				executor.AddLine("retn");
				executor.Execute();

				return executor.Memory.Read<int>(executor.ReturnPointer);
			}
		}
	}
}
