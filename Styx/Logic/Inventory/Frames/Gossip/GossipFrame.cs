#nullable disable

using System;
using System.Collections.Generic;
using GreenMagic;
using Styx.Patchables;
using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.Gossip
{
    /// <summary>
    /// Handles the gossip/dialog frame with NPCs.
    /// </summary>
    public class GossipFrame : Frame
    {
        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static readonly GossipFrame Instance = new GossipFrame();

        public GossipFrame() : base("GossipFrame")
        {
        }

        /// <summary>
        /// Gets available quest info at index.
        /// Address: 5809760 (0x58A660) - CGQuestInfo_C__GetAvailableQuestInfoFromIndex
        /// </summary>
        private static GossipQuestEntry GetAvailableQuestInfo(int index)
        {
            ExecutorRand executor = ObjectManager.Executor;
            if (executor == null) return null;

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine("push {0}", index);
                executor.AddLine("call {0}", (uint)GlobalOffsets.CGQuestInfo_C__GetAvailableQuestInfoFromIndex);
                executor.AddLine("add esp, 4");
                executor.AddLine("retn");
                executor.Execute();
            }

            uint ptr = executor.Memory.Read<uint>(executor.ReturnPointer);
            if (ptr != 0U)
                return new GossipQuestEntry(ptr, index);
            return null;
        }

        /// <summary>
        /// Gets active quest info at index.
        /// Address: 5810000 (0x58A750) - CGQuestInfo_C__GetActiveQuestFromIndex
        /// </summary>
        private static GossipQuestEntry GetActiveQuestInfo(int index)
        {
            ExecutorRand executor = ObjectManager.Executor;
            if (executor == null) return null;

            lock (executor.AssemblyLock)
            {
                executor.Clear();
                executor.AddLine("push {0}", index);
                executor.AddLine("call {0}", (uint)GlobalOffsets.CGQuestInfo_C__GetActiveQuestFromIndex);
                executor.AddLine("add esp, 4");
                executor.AddLine("retn");
                executor.Execute();
            }

            uint ptr = executor.Memory.Read<uint>(executor.ReturnPointer);
            if (ptr != 0U)
                return new GossipQuestEntry(ptr, index);
            return null;
        }

        /// <summary>
        /// Closes the gossip frame.
        /// </summary>
        public void Close()
        {
            Lua.DoString("CloseGossip()");
        }

        /// <summary>
        /// Gets list of active (turn-in) quests from the NPC.
        /// </summary>
        public List<GossipQuestEntry> ActiveQuests
        {
            get
            {
                if (!base.IsVisible)
                    return null;

                List<GossipQuestEntry> list = new List<GossipQuestEntry>();
                for (int i = 0; i < 10; i++)
                {
                    try
                    {
                        GossipQuestEntry entry = GetActiveQuestInfo(i);
                        if (entry != null)
                            list.Add(entry);
                    }
                    catch
                    {
                        break;
                    }
                }
                return list;
            }
        }

        /// <summary>
        /// Gets list of available quests from the NPC.
        /// </summary>
        public List<GossipQuestEntry> AvailableQuests
        {
            get
            {
                List<GossipQuestEntry> list = new List<GossipQuestEntry>();
                if (!base.IsVisible)
                    return list;

                // CGQuestInfo_C__GetAvailableQuestInfoFromIndex returns non-null for ALL indices 0-9
                // even when there are fewer real quests (pre-allocated slots). Cap the loop to the
                // actual quest count from Lua to avoid reading garbage entries.
                int count = Lua.GetReturnVal<int>("return GetNumGossipAvailableQuests()", 0U);
                int limit = Math.Min(count, 10);

                for (int i = 0; i < limit; i++)
                {
                    try
                    {
                        GossipQuestEntry entry = GetAvailableQuestInfo(i);
                        if (entry != null)
                            list.Add(entry);
                    }
                    catch
                    {
                        break;
                    }
                }
                return list;
            }
        }

        /// <summary>
        /// Gets list of gossip options.
        /// </summary>
        public List<GossipEntry> GossipOptionEntries
        {
            get
            {
                List<string> options = Lua.GetReturnValues("return GetGossipOptions()");
                if (options == null)
                    return null;
                return ParseGossipOptions(options.ToArray());
            }
        }

        /// <summary>
        /// Gets the gossip text.
        /// </summary>
        public string GossipText
        {
            get { return Lua.GetReturnVal<string>("return GetGossipText()", 0U); }
        }

        /// <summary>
        /// Selects an active quest by index.
        /// </summary>
        public void SelectActiveQuest(int index)
        {
            Lua.DoString(string.Format("SelectActiveQuest({0}) SelectGossipActiveQuest({0})", index + 1));
        }

        /// <summary>
        /// Selects a gossip option by index.
        /// </summary>
        public void SelectGossipOption(int index)
        {
            Lua.DoString(string.Format("SelectGossipOption({0})", index + 1), "CopilotBuddy.lua");
        }

        /// <summary>
        /// Selects an available quest by index.
        /// </summary>
        public void SelectAvailableQuest(int index)
        {
            Lua.DoString(string.Format("SelectAvailableQuest({0}) SelectGossipAvailableQuest({0})", index + 1), "CopilotBuddy.lua");
        }

        /// <summary>
        /// Hides the gossip frame.
        /// </summary>
        public new void Hide()
        {
            Close();
        }

        private static List<GossipEntry> ParseGossipOptions(string[] gossips)
        {
            if (gossips == null || gossips.Length < 2)
                return null;

            List<GossipEntry> list = new List<GossipEntry>();
            for (int i = 0; i < gossips.Length / 2; i++)
            {
                GossipEntry.GossipEntryType entryType;
                try
                {
                    entryType = (GossipEntry.GossipEntryType)Enum.Parse(
                        typeof(GossipEntry.GossipEntryType), gossips[i * 2 + 1], true);
                }
                catch
                {
                    entryType = GossipEntry.GossipEntryType.Unknown;
                }

                list.Add(new GossipEntry
                {
                    Text = gossips[i * 2],
                    Type = entryType,
                    Index = i
                });
            }
            return list;
        }
    }
}
