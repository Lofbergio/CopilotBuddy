#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using GreenMagic;
using Styx.Logic.Questing;
using Styx.WoWInternals;

namespace Styx.Logic.Inventory.Frames.Quest
{
    public class QuestFrame : Frame
    {
        public static readonly QuestFrame Instance;
        private static Predicate<uint> predicate_0;
        private static Predicate<uint> predicate_1;

        public QuestFrame() : base("QuestFrame")
        {
        }

        static QuestFrame()
        {
            QuestFrame.Instance = new QuestFrame();
        }

        public void AcceptQuest()
        {
            Lua.DoString("AcceptQuest() if QuestFrameAcceptButton:IsVisible() then QuestFrameAcceptButton:Click() end");
        }

        public void ClickContinue() => Lua.DoString("QuestFrameCompleteButton:Click()");

        public void CompleteQuest()
        {
            Lua.DoString("CompleteQuest() if QuestFrameCompleteQuestButton:IsVisible() or QuestFrameCompleteButton:IsVisible() then QuestFrameCompleteQuestButton:Click() QuestFrameCompleteButton:Click() end QuestFrameCompleteQuestButton:Click() QuestFrameCompleteButton:Click()");
        }

        public void DeclineQuest()
        {
            Lua.DoString("DeclineQuest()");
        }

        public void SelectQuestReward(int index)
        {
            string format = "QuestInfoItem{0}:Click()";
            object[] array = new object[]
            {
                index + 1
            };
            Lua.DoString(format, array);
        }

        public new void Hide()
        {
            this.Close();
        }

        public void Close()
        {
            Lua.DoString("CloseQuest()");
        }

        public uint CurrentShownQuestId
        {
            get
            {
                return ObjectManager.Wow.Read<uint>(12637788U);
            }
        }

        public Styx.Logic.Questing.Quest CurrentShownQuest
        {
            get
            {
                return Styx.Logic.Questing.Quest.FromId(this.CurrentShownQuestId);
            }
        }

        public List<uint> Quests
        {
            get
            {
                List<uint> list = new List<uint>();
                uint num = 13227856U;
                Memory wow = ObjectManager.Wow;
                for (uint num2 = wow.Read<uint>(13227856U); num2 != 0U; num2 = ObjectManager.Wow.Read<uint>(num))
                {
                    list.Add(num2);
                    num += 4U;
                }
                return list;
            }
        }

        public List<uint> AvailableQuests
        {
            get
            {
                List<uint> quests = this.Quests;
                if (QuestFrame.predicate_0 == null)
                {
                    QuestFrame.predicate_0 = new Predicate<uint>(IsNotInQuestLog);
                }
                return quests.FindAll(QuestFrame.predicate_0);
            }
        }

        public List<uint> ActiveQuests
        {
            get
            {
                List<uint> quests = this.Quests;
                if (QuestFrame.predicate_1 == null)
                {
                    QuestFrame.predicate_1 = new Predicate<uint>(IsInQuestLog);
                }
                return quests.FindAll(QuestFrame.predicate_1);
            }
        }

        private static bool IsNotInQuestLog(uint ret)
        {
            return !StyxWoW.Me.QuestLog.ContainsQuest(ret);
        }

        private static bool IsInQuestLog(uint ret)
        {
            return StyxWoW.Me.QuestLog.ContainsQuest(ret);
        }
    }
}
