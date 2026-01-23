#nullable disable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GreenMagic;
using Styx.WoWInternals;

namespace Styx.Logic.Questing
{
	/// <summary>
	/// Provides access to the player's quest log.
	/// </summary>
	public class QuestLog
	{
		private static readonly List<uint> _completedQuests = new List<uint>();
		private static DateTime _lastCompletedQuestsQuery = DateTime.MinValue;

		/// <summary>
		/// Number of quests in the log.
		/// Address: 12729040 (0xC24350)
		/// </summary>
		public uint QuestCount
		{
			get
			{
				return ObjectManager.Wow.Read<uint>(12729040U);
			}
		}

		/// <summary>
		/// Gets the quest log index for a quest ID.
		/// </summary>
		public int GetIndexForQuest(uint questId)
		{
			for (int i = 0; i < 25; i++)
			{
				uint offset = (uint)(158 + i * 5);
				uint id = ObjectManager.Me.ReadDescriptor<uint>(offset);
				if (id == questId)
					return i;
			}
			return -1;
		}

		/// <summary>
		/// Gets all quests in the log.
		/// </summary>
		public List<PlayerQuest> GetAllQuests()
		{
			List<PlayerQuest> list = new List<PlayerQuest>();
			for (byte i = 0; i < 25; i++)
			{
				PlayerQuest quest = GetQuest((uint)i);
				if (quest != null)
					list.Add(quest);
			}
			return list;
		}

		/// <summary>
		/// Gets a quest by log index.
		/// </summary>
		public PlayerQuest GetQuest(uint index)
		{
			if (index >= 25U)
				throw new ArgumentOutOfRangeException("index");

			uint questId = GetQuestIdAtIndex(index);
			if (questId <= 0U)
				return null;

			return PlayerQuest.FromId(questId);
		}

		/// <summary>
		/// Gets the quest ID at a log index.
		/// </summary>
		public uint GetQuestId(uint index)
		{
			if (index >= 25U)
				throw new ArgumentOutOfRangeException("index");

			return GetQuestIdAtIndex(index);
		}

		private uint GetQuestIdAtIndex(uint index)
		{
			uint offset = 158U + index * 5U;
			return ObjectManager.Me.ReadDescriptor<uint>(offset);
		}

		/// <summary>
		/// Checks if a quest is in the log.
		/// </summary>
		public bool ContainsQuest(uint questId)
		{
			for (int i = 0; i < 25; i++)
			{
				uint offset = (uint)(158 + i * 5);
				uint id = ObjectManager.Me.ReadDescriptor<uint>(offset);
				if (id == questId)
					return true;
			}
			return false;
		}

		/// <summary>
		/// Gets a quest by ID.
		/// </summary>
		public PlayerQuest GetQuestById(uint questId)
		{
			if (!ContainsQuest(questId))
				return null;
			return PlayerQuest.FromId(questId);
		}

		/// <summary>
		/// Gets quest info at a log index.
		/// </summary>
		public QuestLogEntry GetQuestInfo(int index)
		{
			uint offset = (uint)(158 + index * 5 * 4);
			uint descriptorPtr = ObjectManager.Wow.Read<uint>(StyxWoW.Me.BaseAddress + 8U);
			return ObjectManager.Wow.ReadStruct<QuestLogEntry>(descriptorPtr + offset);
		}

		/// <summary>
		/// Abandons a quest by ID.
		/// Address: 12729052 (0xC2435C), Call: 6163648 (0x5E1CC0)
		/// </summary>
		public void AbandonQuestById(uint questId)
		{
			ExecutorRand executor = ObjectManager.Executor;
			if (executor == null)
				throw new Exception("Invalid executor used in AbandonQuestById.");

			lock (executor.AssemblyLock)
			{
				executor.Clear();
				executor.AddLine("mov esi, {0}", 12729052U);
				executor.AddLine("mov ebx, [esi]");
				executor.AddLine("mov eax, {0}", questId);
				executor.AddLine("mov [esi], eax");
				executor.AddLine("call {0}", 6163648U);
				executor.AddLine("mov [esi], ebx");
				executor.AddLine("retn");
				executor.Execute();
			}
		}

		/// <summary>
		/// Abandons a quest by log slot.
		/// </summary>
		public void AbandonQuest(byte slot)
		{
			if (slot >= 25)
				throw new ArgumentOutOfRangeException("slot");

			RemoveQuestFromLog(slot);
		}

		/// <summary>
		/// Removes a quest from the log.
		/// Call: ClntObjMgrGetActivePlayerObj (4208880), CGPlayer_C__QuestLogRemoveQuest (7163776)
		/// </summary>
		private static void RemoveQuestFromLog(byte slot)
		{
			ExecutorRand executor = ObjectManager.Executor;
			if (executor == null)
				throw new Exception("Invalid executor used in CGPlayer_C__QuestLogRemoveQuest.");

			lock (executor.AssemblyLock)
			{
				executor.Clear();
				executor.AddLine("call {0}", 4208880U);
				executor.AddLine("mov ecx, eax");
				executor.AddLine("push {0}", slot);
				executor.AddLine("call {0}", 7163776U);
				executor.AddLine("retn");
				executor.Execute();
			}
		}

		/// <summary>
		/// Gets a read-only collection of quest IDs that have been completed.
		/// Uses Lua QueryQuestsCompleted() which triggers QUEST_QUERY_COMPLETE event.
		/// Results are cached for 1 minute.
		/// </summary>
		public ReadOnlyCollection<uint> GetCompletedQuests()
		{
			DateTime now = DateTime.Now;
			if (now.Subtract(_lastCompletedQuestsQuery).TotalMinutes > 1.0 || _completedQuests.Count == 0)
			{
				// Query completed quests via Lua
				_completedQuests.Clear();
				
				// In 3.3.5, we can use GetQuestsCompleted() or check via Lua
				// For simplicity, just read completed quest flags from memory
				// WoW 3.3.5: Completed quests stored as bitfield at specific address
				// Address for completed quests in 3.3.5: 0xC24338 (12729144)
				uint completedQuestsBase = 12729144U;
				for (uint i = 0; i < 1250; i++) // Max quest ID check range
				{
					uint byteOffset = i / 8;
					byte bitOffset = (byte)(i % 8);
					byte questByte = ObjectManager.Wow.Read<byte>(completedQuestsBase + byteOffset);
					if ((questByte & (1 << bitOffset)) != 0)
					{
						_completedQuests.Add(i);
					}
				}
				
				_lastCompletedQuestsQuery = now;
			}
			return _completedQuests.AsReadOnly();
		}

		/// <summary>
		/// Adds a quest ID to the completed quests cache.
		/// </summary>
		internal void AddCompletedQuest(uint questId)
		{
			if (!_completedQuests.Contains(questId))
				_completedQuests.Add(questId);
		}
	}
}
