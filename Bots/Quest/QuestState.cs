// QuestState.cs - Global quest state management
// Ported from HB 4.3.4

using System.Collections.Generic;
using Styx;
using Styx.Logic.AreaManagement;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Bots.Quest
{
    /// <summary>
    /// Singleton managing the global quest bot state.
    /// </summary>
    public class QuestState
    {
        /// <summary>
        /// Global singleton instance.
        /// </summary>
        public static readonly QuestState Instance = new QuestState();

        public QuestState()
        {
            Order = new QuestOrder.QuestOrder();
            BotEvents.Profile.OnNewProfileLoaded += OnNewProfileLoaded;
        }

        private void OnNewProfileLoaded(BotEvents.Profile.NewProfileLoadedEventArgs args)
        {
            if (args.OldProfile?.QuestOrder == args.NewProfile?.QuestOrder)
                return;
            
            InitializeFromProfile(args.NewProfile);
        }

        /// <summary>
        /// Initialize quest state from a profile.
        /// </summary>
        internal void InitializeFromProfile(Profile profile)
        {
            if (profile == null)
            {
                Order.Nodes = new OrderNodeCollection();
                return;
            }

            Order.Nodes = new OrderNodeCollection(profile.QuestOrder?.Count ?? 0);
            
            if (profile.QuestOrder != null)
            {
                Order.Nodes.AddRange(profile.QuestOrder);
                Order.Nodes.IgnoreCheckpoints = profile.QuestOrder.IgnoreCheckpoints;
            }

            if (Order.Nodes.Count > 0)
            {
                ObjectManager.Update();
                Order.UpdateNodes();
                Order.CurrentBehavior = null;
            }
        }

        /// <summary>
        /// The current quest order being executed.
        /// </summary>
        public QuestOrder.QuestOrder Order { get; private set; }

        /// <summary>
        /// Current vendors for the quest area.
        /// </summary>
        public List<Vendor> CurrentVendors { get; set; }

        /// <summary>
        /// Current mailboxes for the quest area.
        /// </summary>
        public List<Mailbox> CurrentMailboxes { get; set; }

        /// <summary>
        /// Current grind area for the quest.
        /// </summary>
        public GrindArea CurrentGrindArea { get; set; }
    }
}
