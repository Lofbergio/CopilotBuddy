#nullable disable
using System;
using GreenMagic;
using Styx.Helpers;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Represents a quest in the player's quest log.
	/// </summary>
	public class PlayerQuest : Quest
	{
		protected PlayerQuest(WoWCache.QuestCacheEntry entry) : base(entry)
		{
		}

		/// <summary>
		/// Whether the quest is completed.
		/// </summary>
		public bool IsCompleted
		{
			get
			{
				WoWDescriptorQuest data;
				if (GetData(out data) && (data.Flags & WoWDescriptorQuestFlags.Completed) != 0)
					return true;
				return IsQuestCompletedNative();
			}
		}

		/// <summary>
		/// Whether the quest has failed.
		/// </summary>
		public bool IsFailed
		{
			get
			{
				WoWDescriptorQuest data;
				if (GetData(out data))
					return (data.Flags & WoWDescriptorQuestFlags.Failed) != 0;
				return false;
			}
		}

		/// <summary>
		/// Gets completion info for this quest.
		/// Address: 12729088 (0xC24380)
		/// </summary>
		public WoWQuestCompletionInfo GetCompletionInfo()
		{
			WoWQuestCompletionInfo info;
			GetCompletionInfo(out info);
			return info;
		}

		/// <summary>
		/// Gets completion info for this quest.
		/// </summary>
		public bool GetCompletionInfo(out WoWQuestCompletionInfo info)
		{
			WoWQuestCompletionInfo[] array = ObjectManager.Wow.ReadStructArray<WoWQuestCompletionInfo>(12729088U, 25);
			for (int i = 0; i < 25; i++)
			{
				if ((long)array[i].QuestID == (long)Id)
				{
					info = array[i];
					return true;
				}
			}
			info = default(WoWQuestCompletionInfo);
			return false;
		}

		/// <summary>
		/// Gets quest data from player descriptor.
		/// Offset: BaseAddress + 8 -> descriptor + 632
		/// </summary>
		public bool GetData(out WoWDescriptorQuest data)
		{
			WoWDescriptorQuest[] quests = GetAllQuestData();
			int index = FindQuestIndex(quests);
			if (index >= 0 && index < quests.Length)
			{
				data = quests[index];
				return true;
			}
			data = default(WoWDescriptorQuest);
			return false;
		}

		private int FindQuestIndex(WoWDescriptorQuest[] quests)
		{
			for (int i = 0; i < quests.Length; i++)
			{
				if (quests[i].Id == Id)
					return i;
			}
			return -1;
		}

		private static WoWDescriptorQuest[] GetAllQuestData()
		{
			uint descriptorPtr = ObjectManager.Wow.Read<uint>(ObjectManager.Me.BaseAddress + 8U);
			return ObjectManager.Wow.ReadStructArray<WoWDescriptorQuest>(descriptorPtr + 632U, 25);
		}

		/// <summary>
		/// Native call to check if quest is completed.
		/// Uses CGQuestLog__GetLuaQuestIndexByID (6155952) and CGQuestLog__IsQuestCompleted (6164128/6164656)
		/// </summary>
		private bool IsQuestCompletedNative()
		{
			ExecutorRand executor = ObjectManager.Executor;
			if (executor == null)
				return false;

			try
			{
				lock (executor.AssemblyLock)
				{
					executor.Clear();
					
					// Get Lua quest index: push questId, call GetLuaQuestIndexByID
					executor.AddLine("push {0}", Id);
					executor.AddLine("call {0}", 6155952U);
					executor.AddLine("add esp, 4");
					executor.AddLine("mov esi, eax");
					executor.AddLine("sub esi, 1");
					
					// Check if completed (first method)
					executor.AddLine("push 1");
					executor.AddLine("push esi");
					executor.AddLine("call {0}", 6164128U);
					executor.AddLine("add esp, 8");
					executor.AddLine("test eax, eax");
					executor.AddLine("jnz @IsCompleted");
					
					// Check if completed (second method)
					executor.AddLine("push esi");
					executor.AddLine("call {0}", 6164656U);
					executor.AddLine("add esp, 4");
					executor.AddLine("test eax, eax");
					executor.AddLine("jz @IsNotCompleted");
					
					executor.AddLine("@IsCompleted:");
					executor.AddLine("mov eax, 1");
					executor.AddLine("retn");
					
					executor.AddLine("@IsNotCompleted:");
					executor.AddLine("mov eax, 0");
					executor.AddLine("retn");
					
					executor.Execute();
					return executor.Memory.Read<bool>(executor.ReturnPointer);
				}
			}
			catch (Exception ex)
			{
				Logging.WriteDebug("Exception in CGQuestLog__IsQuestCompleted: {0}", ex.Message);
				return false;
			}
		}

		/// <summary>
		/// Creates a PlayerQuest from a quest ID.
		/// </summary>
		internal static new PlayerQuest FromId(uint id)
		{
			WoWCache.InfoBlock infoBlock = StyxWoW.Cache[CacheDb.Quest].GetInfoBlockById(id);
			if (infoBlock != null)
				return new PlayerQuest(infoBlock.Quest);
			return null;
		}
	}
}
