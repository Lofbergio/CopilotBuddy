using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Forms.Integration;
using Bots.DungeonBuddy;
using Bots.DungeonBuddy.Helpers;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals.WoWObjects;
using Styx.WoWInternals.DBC;
using Bots.DungeonBuddy.Enums;
using Styx;
using Styx.Helpers;
using Styx.WoWInternals;
using wf = System.Windows.Forms;

namespace Bots.DungeonBuddy.Forms
{
    public partial class FormConfig : Window
    {
        private bool _unchecking;
        private bool _forcedCheck;

        public FormConfig()
        {
            InitializeComponent();

            // connect WindowsForms PropertyGrid usage
            propertyGridMain.SelectedObject = DungeonBuddySettings.Instance;
        }

        private void FormConfig_Load(object sender, RoutedEventArgs e)
        {
            propertyGridMain.SelectedObject = DungeonBuddySettings.Instance;
            cbShowAll.IsChecked = false;
            btnToggleMovement.Content = (ScriptHelpers.MovementEnabled ? "Disable Movement" : "Enable Movement");
            // populate trees according to original logic when tab selected
            tabControlMain_SelectedIndexChanged(null, null);
        }

        private void FormConfig_FormClosing(object sender, CancelEventArgs e)
        {
            DungeonBuddySettings.Instance.Save();
        }

        private static void GenerateTreeView(wf.TreeView treeView, bool debug, IEnumerable<LfgDungeons> dungeons)
        {
            var list = (from d in dungeons
                        orderby d.ExpansionId descending, d.Difficulty descending, d.MinLevel, d.Name
                        group d by d.ExpansionId).ToList();

            foreach (var grouping in list)
            {
                var enumerable = from d in grouping
                                 group d by d.Difficulty;
                string text;
                switch (grouping.Key)
                {
                    case 0U: text = "Classic"; break;
                    case 1U: text = "Burning Crusade"; break;
                    case 2U: text = "Wrath of the Lich King"; break;
                    case 3U: text = "Cataclysm"; break;
                    default: text = "Default"; break;
                }
                foreach (var grouping2 in enumerable)
                {
                    string text2 = (grouping2.Key == 0U) ? "Normal" : ((grouping2.Key == 1U) ? "Heroic" : "Default");
                    string name = text + " " + text2;
                    var parent = treeView.Nodes.Add(name, name);

                    foreach (var lfg in grouping2)
                    {
                        string display = (lfg.Difficulty == 0U) ? lfg.Name : (lfg.Name + " [Heroic]");
                        var node = parent.Nodes.Add(display, display);
                        node.Tag = lfg.Id;
                    }

                    if (debug)
                    {
                        // optionally add debug child nodes (map id)
                        foreach (wf.TreeNode child in parent.Nodes)
                        {
                            // nothing extra here for now
                        }
                    }

                    if (treeView.CheckBoxes)
                    {
                        foreach (wf.TreeNode node in parent.Nodes)
                        {
                            if (DungeonBuddySettings.Instance.SelectedDungeonIds.Contains(Convert.ToUInt32(node.Tag)))
                                node.Checked = true;
                        }
                    }
                }
            }
        }

        private void tabControlMain_SelectedIndexChanged(object sender, RoutedEventArgs e)
        {
            // clear and (re)generate treeviews similar to original behavior
            if (twDungeonSelection != null)
                twDungeonSelection.Nodes.Clear();
            if (twDebug != null)
                twDebug.Nodes.Clear();

            if (DungeonBuddySettings.Instance.QueueType == QueueType.Specific || DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm)
            {
                ToggleControlForSelection(true);
                // WotLK LfgDungeons.dbc TypeId: 1=Dungeon, 2=Raid, 4=Zone, 6=Random
                // Only show actual dungeons and random entries — zones/raids/holidays excluded
                var all = Styx.WoWInternals.DBC.LfgDungeons.All.Values
                    .Where(d => (d.TypeId == 1 || d.TypeId == 6) && !d.IsHolidayEvent);
                var playerLevel = StyxWoW.Me.Level;
                var dungeons = (cbShowAll.IsChecked == true)
                    ? all
                    : all.Where(d => d.MinLevel <= playerLevel && d.MaxLevel >= playerLevel);
                GenerateTreeView(twDungeonSelection, false, dungeons);
                GenerateTreeView(twDebug, true, all);
            }
            else
            {
                ToggleControlForSelection(false);
            }

            // boss tree
            if (twBossTree != null)
            {
                twBossTree.Nodes.Clear();
                foreach (var boss in BossManager.Bosses)
                {
                    if (!boss.IsOptional)
                    {
                        var node = new wf.TreeNode(boss.Name) { Checked = !boss.IsDead };
                        twBossTree.Nodes.Add(node);
                    }
                }
            }
        }

        private void ToggleControlForSelection(bool value)
        {
            lblWarningText.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            // host visibility handled by WPF wrapper; nothing else needed here
        }

        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (twDungeonSelection == null) return;
            foreach (wf.TreeNode node in twDungeonSelection.Nodes)
                node.Checked = true;
        }

        private void btnUnselectAll_Click(object sender, RoutedEventArgs e)
        {
            if (twDungeonSelection == null) return;
            foreach (wf.TreeNode node in twDungeonSelection.Nodes)
                node.Checked = false;
        }

        private void twDungeonSelection_BeforeCheck(object sender, wf.TreeViewCancelEventArgs e)
        {
            // keep for compatibility - original left empty
        }

        private void twDungeonSelection_AfterCheck(object sender, wf.TreeViewEventArgs e)
        {
            if (_unchecking) return;

            if (e.Node.GetNodeCount(false) > 0 && DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm)
            {
                _unchecking = true;
                e.Node.Checked = false;
                _unchecking = false;
                return;
            }

            if (e.Node.GetNodeCount(false) > 0 && DungeonBuddySettings.Instance.QueueType != QueueType.SoloFarm)
            {
                if (!_forcedCheck)
                {
                    foreach (wf.TreeNode child in e.Node.Nodes)
                    {
                        if (child.Checked != e.Node.Checked)
                            child.Checked = e.Node.Checked;
                    }
                }
                return;
            }

            var list = DungeonBuddySettings.Instance.SelectedDungeonIds.ToList();
            uint num = Convert.ToUInt32(e.Node.Tag ?? 0U);
            if (e.Node.Checked)
            {
                _unchecking = true;
                if (DungeonBuddySettings.Instance.QueueType == QueueType.SoloFarm)
                {
                    list.Clear();
                    foreach (wf.TreeNode tn in twDungeonSelection.Nodes)
                    {
                        if (tn.GetNodeCount(false) > 0)
                        {
                            foreach (wf.TreeNode child in tn.Nodes)
                                child.Checked = false;
                        }
                        tn.Checked = false;
                    }
                    e.Node.Checked = true;
                }
                _unchecking = false;
                if (!list.Contains(num)) list.Add(num);
                if (e.Node.Parent != null && e.Node.Parent.Nodes.Cast<wf.TreeNode>().All(n => n.Checked))
                    e.Node.Parent.Checked = true;
            }
            else
            {
                if (list.Contains(num)) list.Remove(num);
                if (e.Node.Parent != null && e.Node.Parent.Checked)
                {
                    _forcedCheck = true;
                    e.Node.Parent.Checked = false;
                }
            }

            _forcedCheck = false;
            DungeonBuddySettings.Instance.SelectedDungeonIds = list.ToArray();
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            ObjectManager.Update();
            Logging.Write("MapId:{0}", new object[] { StyxWoW.Me.MapId });
            Logging.Write("LfgDungeon Id:{0}", new object[] { (DungeonManager.CurrentDungeon?.DungeonId ?? 0U) });
            foreach (var obj in ObjectManager.GetObjectsOfType<WoWDynamicObject>())
            {
                Logging.Write("{0} Entry:{1} Radius:{2}", new object[] { obj.Name, obj.Entry, obj.Radius });
            }
            if (StyxWoW.Me.GotTarget)
            {
                Logging.Write("<Hotspot X=\"{0}\" Y=\"{1}\" Z=\"{2}\" />", new object[] { StyxWoW.Me.CurrentTarget.X, StyxWoW.Me.CurrentTarget.Y, StyxWoW.Me.CurrentTarget.Z });
                Logging.Write("<Boss isFinal=\"false\" entry=\"{0}\" name=\"{1}\" killOrder=\"1\" optional=\"false\" X=\"{2}\" Y=\"{3}\" Z=\"{4}\"/>", new object[] { StyxWoW.Me.CurrentTarget.Entry, StyxWoW.Me.CurrentTarget.Name, StyxWoW.Me.CurrentTarget.Location.X, StyxWoW.Me.CurrentTarget.Location.Y, StyxWoW.Me.CurrentTarget.Location.Z });
            }
                foreach (var boss in BossManager.Bosses)
                {
                    Logging.Write("{0} - IsDead: {1}", new object[] { boss.Name, boss.IsDead });
                }
        }

        private void loadProfile_click(object sender, RoutedEventArgs e)
        {
            var ofd = new System.Windows.Forms.OpenFileDialog
            {
                Filter = "Profile files (*.xml)|*.xml|All files (*.*)|*.*",
                InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Default Profiles", "DungeonBuddy")
            };
            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FormConfig.LoadProfile(ofd.FileName);
            }
        }

        public static void LoadProfile(string path)
        {
            // HB 4.3.4: Profile.Load(path) → ProfileManager.Load(profile)
            Bots.DungeonBuddy.Profiles.ProfileManager.LoadFromPath(path);
        }

        private void cbShowAll_CheckedChanged(object sender, RoutedEventArgs e)
        {
            tabControlMain_SelectedIndexChanged(null, null);
        }

        private void dbgButton2_Click(object sender, RoutedEventArgs e)
        {
            foreach (var boss in BossManager.Bosses)
            {
                Logging.Write("{0} - IsDead: {1}", new object[] { boss.Name, boss.IsDead });
            }
        }

        private void debugBtn3_Click(object sender, RoutedEventArgs e)
        {
            // placeholder for original debug action
            Logging.Write("debugBtn3 pressed");
        }

        private void RecompileScriptsButton_Click(object sender, RoutedEventArgs e)
        {
            DungeonManager.ReloadDungeons();
        }

        private void btnToggleMovement_Click(object sender, RoutedEventArgs e)
        {
            // toggle movement via ScriptHelpers as original
            ScriptHelpers.ToggleMovement();
            btnToggleMovement.Content = (ScriptHelpers.MovementEnabled ? "Disable Movement" : "Enable Movement");
        }

        private void twBossTree_AfterCheck(object sender, wf.TreeViewEventArgs e)
        {
            // HB 4.3.4: BossManager.BossEncounters.FirstOrDefault(b => b.Name == node.Text) → Reset/MarkAsDead
            if (e.Node.Checked)
                BossManager.ResetBoss(e.Node.Text);
            else
                BossManager.MarkBossDead(e.Node.Text);
        }
    }
}
