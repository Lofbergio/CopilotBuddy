using System;
using System.Text;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;

namespace Styx.Logic.Questing
{
	public class Quest
	{
		protected Quest(WoWCache.QuestCacheEntry entry)
		{
			InternalInfo = entry;
			_name = ToUTF8String(entry.Name);
			_description = ToUTF8String(entry.Description);
			_subDescription = ToUTF8String(entry.SubDescription);
			_completionText = ToUTF8String(entry.CompletionText);
			_objectiveText = ToUTF8String(entry.ObjectiveText);
			_objectives = new string[4];
			for (int i = 0; i < 4; i++)
			{
				_objectives[i] = ToUTF8String(entry.ObjectiveTexts, i * 256, 256);
			}
		}

		private static string ToUTF8String(byte[] bytes)
		{
			if (bytes == null) return string.Empty;
			int length = Array.IndexOf(bytes, (byte)0);
			if (length < 0) length = bytes.Length;
			return Encoding.UTF8.GetString(bytes, 0, length);
		}

		private static string ToUTF8String(byte[] bytes, int offset, int maxLength)
		{
			if (bytes == null || offset >= bytes.Length) return string.Empty;
			int length = 0;
			for (int i = offset; i < bytes.Length && i < offset + maxLength && bytes[i] != 0; i++)
				length++;
			return Encoding.UTF8.GetString(bytes, offset, length);
		}

		private string _name;
		private string _description;
		private string _subDescription;
		private string _completionText;
		private string _objectiveText;
		private string[] _objectives;

		public uint Id => InternalInfo.Id;

		public WoWCache.QuestCacheEntry InternalInfo { get; private set; }

		public string Name => _name;

		public string Description => _description;

		public string SubDescription => _subDescription;

		public string CompletionText => _completionText;

		public string ObjectiveText => _objectiveText;

		public string[] Objectives => _objectives;

		public int[] CollectItemIds => InternalInfo.CollectItemId;

		public int[] CollectItemCounts => InternalInfo.CollectItemCount;

		public int[] CollectIntermediateItemIds => InternalInfo.IntermediateItemId;

		public int[] CollectIntermediateItemCounts => InternalInfo.IntermediateItemCount;

		public int[] NormalObjectiveIDs => InternalInfo.ObjectiveId;

		public int[] NormalObjectiveRequiredCounts => InternalInfo.ObjectiveRequiredCount;

		public int Level => (int)InternalInfo.Level;

		public int RequiredLevel => (int)InternalInfo.RequiredLevel;

		public uint SuggestedPlayers => InternalInfo.SuggestedPlayers;

		public uint FriendlyFactionId => InternalInfo.FriendlyFactionId;

		public uint FriendlyFactionAmount => InternalInfo.FriendlyFactionAmount;

		public uint HostileFactionId => InternalInfo.HostileFactionId;

		public uint HostileFactionAmount => InternalInfo.HostileFactionAmount;

		public uint NextQuestId => InternalInfo.NextQuestId;

		public uint RewardMoney => InternalInfo.RewardMoney;

		public uint RewardMoneyCompensation => InternalInfo.RewardMoneyCompensation;

		public uint RewardSpellId => InternalInfo.RewardSpellId;

		public int[] RewardItemIds => InternalInfo.RewardItem;

		public int[] RewardItemCounts => InternalInfo.RewardItemCount;

		public override string ToString() => Name;

		public static Quest FromId(uint id)
		{
			if (id == 0U)
				return null;
			WoWCache.InfoBlock infoBlockById = StyxWoW.Cache[CacheDb.Quest].GetInfoBlockById(id);
			return infoBlockById != null ? new Quest(infoBlockById.Quest) : null;
		}

		public int[] RewardChoiceItemIds => InternalInfo.RewardChoiceItem;

		public int[] RewardChoiceItemCounts => InternalInfo.RewardChoiceItemCount;

		public uint PointMapId => InternalInfo.PointMapId;

		public float PointX => InternalInfo.PointX;

		public float PointY => InternalInfo.PointY;

		public uint PointOptional => InternalInfo.PointOptional;

		public WoWCache.QuestFlags Flags => InternalInfo.Flags;

		public uint RewardTitleId => InternalInfo.RewardTitleId;

		public uint RequiredPlayersKilled => InternalInfo.RequiredPlayersKilled;

		public uint RewardTalentPoints => InternalInfo.RewardTalentPoints;

		public uint RewardArenaPoints => InternalInfo.RewardArenaPoints;

		private System.Collections.Generic.List<QuestObjective>? _objectives_list;

		/// <summary>
		/// Gets all objectives for this quest.
		/// </summary>
		public System.Collections.Generic.List<QuestObjective> GetObjectives()
		{
			if (_objectives_list == null)
			{
				_objectives_list = new System.Collections.Generic.List<QuestObjective>();
				int num = 0;

				// Normal objectives (Kill mobs / Use game objects)
				for (int i = 0; i < NormalObjectiveIDs.Length; i++)
				{
					if (((long)NormalObjectiveIDs[i] & 0x80000000L) != 0L)
					{
						// High bit set = GameObject
						int id = (int)((long)NormalObjectiveIDs[i] & int.MaxValue);
						_objectives_list.Add(new QuestObjective(num++, id, NormalObjectiveRequiredCounts[i], null, QuestObjectiveType.UseGameObject));
					}
					else if (NormalObjectiveIDs[i] != 0)
					{
						_objectives_list.Add(new QuestObjective(num++, NormalObjectiveIDs[i], NormalObjectiveRequiredCounts[i], null, QuestObjectiveType.KillMob));
					}
				}

				// Collect items
				for (int i = 0; i < CollectItemIds.Length; i++)
				{
					if (CollectItemIds[i] != 0)
						_objectives_list.Add(new QuestObjective(num++, CollectItemIds[i], CollectItemCounts[i], null, QuestObjectiveType.CollectItem));
				}

				// Collect intermediate items
				for (int i = 0; i < CollectIntermediateItemIds.Length; i++)
				{
					if (CollectIntermediateItemIds[i] != 0)
						_objectives_list.Add(new QuestObjective(num++, CollectIntermediateItemIds[i], CollectIntermediateItemCounts[i], null, QuestObjectiveType.CollectIntermediateItem));
				}

				// Special objectives (text)
				for (int i = 0; i < Objectives.Length; i++)
				{
					if (!string.IsNullOrEmpty(Objectives[i]))
						_objectives_list.Add(new QuestObjective(num++, 0, 0, Objectives[i], QuestObjectiveType.Special));
				}

				// Faction objectives
				_objectives_list.Add(new QuestObjective(num++, (int)FriendlyFactionId, (int)FriendlyFactionAmount, null, QuestObjectiveType.AcquireFactionValue));
				_objectives_list.Add(new QuestObjective(num++, (int)HostileFactionId, (int)HostileFactionAmount, null, QuestObjectiveType.AcquireFactionValue));
			}
			return _objectives_list;
		}

		/// <summary>
		/// STUB-03: Gets descriptor data for this quest from the player's quest log.
		/// Reads the 25 quest log slots in the player descriptor table.
		/// Layout per slot (5 descriptor indices each, starting at 0x9E):
		///   +0: Quest ID (uint)
		///   +1: State flags (uint → WoWDescriptorQuestFlags)
		///   +2: Objective counts low (2 packed ushorts: objectives 0,1)
		///   +3: Objective counts high (2 packed ushorts: objectives 2,3)
		///   +4: Timer (uint, seconds before failed)
		/// </summary>
		public bool GetData(out QuestDescriptorData data)
		{
			data = default;

			var me = StyxWoW.Me;
			if (me == null || me.BaseAddress == 0U)
				return false;

			const uint QUEST_LOG_BASE = 0x9E; // PLAYER_QUEST_LOG_1_1 (absolute descriptor index)
			const int QUEST_LOG_SLOTS = 25;
			const int FIELDS_PER_SLOT = 5;

			uint questId = Id;

			for (int slot = 0; slot < QUEST_LOG_SLOTS; slot++)
			{
				uint slotBase = QUEST_LOG_BASE + (uint)(slot * FIELDS_PER_SLOT);
				uint slotQuestId = me.ReadDescriptor<uint>(slotBase);

				if (slotQuestId != questId)
					continue;

				// Found the quest in this slot — read all fields
				data.Id = slotQuestId;
				data.Flags = (WoWDescriptorQuestFlags)me.ReadDescriptor<uint>(slotBase + 1);

				uint objectivesLow = me.ReadDescriptor<uint>(slotBase + 2);
				uint objectivesHigh = me.ReadDescriptor<uint>(slotBase + 3);

				data.ObjectivesDone = new ushort[4];
				data.ObjectivesDone[0] = (ushort)(objectivesLow & 0xFFFF);
				data.ObjectivesDone[1] = (ushort)((objectivesLow >> 16) & 0xFFFF);
				data.ObjectivesDone[2] = (ushort)(objectivesHigh & 0xFFFF);
				data.ObjectivesDone[3] = (ushort)((objectivesHigh >> 16) & 0xFFFF);

				data.SecondsBeforeFailed = me.ReadDescriptor<uint>(slotBase + 4);

				return true;
			}

			return false;
		}

		/// <summary>
		/// Types of quest objectives.
		/// </summary>
		public enum QuestObjectiveType
		{
			CollectIntermediateItem,
			CollectItem,
			KillMob,
			UseGameObject,
			AcquireFactionValue,
			Special,
		}

		/// <summary>
		/// Represents a single quest objective.
		/// </summary>
		public struct QuestObjective
		{
			public int Count;
			public int ID;
			public int Index;
			public string? Objective;
			public QuestObjectiveType Type;

			public QuestObjective(int index, int id, int count, string? objective, QuestObjectiveType type)
			{
				Index = index;
				ID = id;
				Count = count;
				Objective = objective;
				Type = type;
			}

			public bool IsEmpty
			{
				get
				{
					return Type == QuestObjectiveType.Special 
						? string.IsNullOrEmpty(Objective) 
						: ID == 0;
				}
			}
		}
	}
}
