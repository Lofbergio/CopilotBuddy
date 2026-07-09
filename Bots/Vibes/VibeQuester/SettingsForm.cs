using System;
using System.Drawing;
using System.Windows.Forms;
using Styx;
using Styx.Logic.Questing;
using Styx.UI;
using Styx.WoWInternals.WoWObjects;

namespace VibeQuester
{
    public class SettingsForm : Form
    {
        private readonly VibeQuesterSettings _settings;
        private readonly Action<string> _log;
        private NumericUpDown _numStartDist;
        private NumericUpDown _numStep;
        private NumericUpDown _numMaxDist;
        private NumericUpDown _numMaxQuests;
        private NumericUpDown _numMobOverLevel;
        private NumericUpDown _numAutoBlacklistMin;
        private CheckBox _chkAutoVendor;
        private CheckBox _chkAutoTrain;
        private CheckBox _chkSellGrey;
        private CheckBox _chkSellWhite;
        private CheckBox _chkSellGreen;
        private CheckBox _chkSellBlue;
        private TextBox _txtKeepList;
        private TextBox _txtBlacklist;

        public SettingsForm(VibeQuesterSettings settings, Action<string> log)
        {
            _settings = settings;
            _log = log;
            Text = "VibeQuester Settings";
            Size = new Size(350, 560);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;

            BuildForm();
            LoadSettings();
        }

        private void BuildForm()
        {
            int y = 12;
            int labelW = 140;
            int ctrlW = 80;

            void AddRow(string label, Control ctrl)
            {
                Label lbl = new Label
                {
                    Text = label,
                    Location = new Point(12, y + 3),
                    Size = new Size(labelW, 20)
                };
                ctrl.Location = new Point(160, y);
                ctrl.Size = new Size(ctrlW, 24);
                Controls.Add(lbl);
                Controls.Add(ctrl);
                y += 30;
            }

            _numStartDist = new NumericUpDown { Minimum = 50, Maximum = 2000 };
            AddRow("Scan start distance:", _numStartDist);

            _numStep = new NumericUpDown { Minimum = 50, Maximum = 2000 };
            AddRow("Scan step:", _numStep);

            _numMaxDist = new NumericUpDown { Minimum = 500, Maximum = 10000, Increment = 250 };
            AddRow("Scan max distance:", _numMaxDist);

            _numMaxQuests = new NumericUpDown { Minimum = 1, Maximum = 50 };
            AddRow("Max quests per profile:", _numMaxQuests);

            _numMobOverLevel = new NumericUpDown { Minimum = 0, Maximum = 20 };
            AddRow("Max mob over-level:", _numMobOverLevel);

            _numAutoBlacklistMin = new NumericUpDown { Minimum = 1, Maximum = 240 };
            AddRow("Auto-blacklist retry (min):", _numAutoBlacklistMin);

            _chkAutoVendor = new CheckBox { Text = "Auto-vendor (repair + sell)", Checked = true };
            _chkAutoVendor.Location = new Point(12, y);
            _chkAutoVendor.Size = new Size(310, 24);
            Controls.Add(_chkAutoVendor);
            y += 28;

            _chkAutoTrain = new CheckBox { Text = "Auto-train (class trainer)", Checked = true };
            _chkAutoTrain.Location = new Point(12, y);
            _chkAutoTrain.Size = new Size(310, 24);
            Controls.Add(_chkAutoTrain);
            y += 28;

            Label sellLabel = new Label
            {
                Text = "Sell by quality (unequipped, not quest/keep items):",
                Location = new Point(12, y + 3),
                Size = new Size(310, 20)
            };
            Controls.Add(sellLabel);
            y += 26;

            _chkSellGrey = new CheckBox { Text = "Sell greys", Checked = true };
            _chkSellGrey.Location = new Point(20, y);
            _chkSellGrey.Size = new Size(140, 24);
            Controls.Add(_chkSellGrey);

            _chkSellWhite = new CheckBox { Text = "Sell whites", Checked = false };
            _chkSellWhite.Location = new Point(160, y);
            _chkSellWhite.Size = new Size(140, 24);
            Controls.Add(_chkSellWhite);

            _chkSellGreen = new CheckBox { Text = "Sell greens", Checked = false };
            _chkSellGreen.Location = new Point(20, y + 24);
            _chkSellGreen.Size = new Size(140, 24);
            Controls.Add(_chkSellGreen);

            _chkSellBlue = new CheckBox { Text = "Sell blues", Checked = false };
            _chkSellBlue.Location = new Point(160, y + 24);
            _chkSellBlue.Size = new Size(140, 24);
            Controls.Add(_chkSellBlue);
            y += 56;

            Label keepLabel = new Label
            {
                Text = "Never sell (names or IDs, comma-separated):",
                Location = new Point(12, y + 3),
                Size = new Size(310, 20)
            };
            _txtKeepList = new TextBox
            {
                Location = new Point(12, y + 24),
                Size = new Size(310, 24)
            };
            Controls.Add(keepLabel);
            Controls.Add(_txtKeepList);
            y += 56;

            Label blacklistLabel = new Label
            {
                Text = "Quest blacklist (IDs):",
                Location = new Point(12, y + 3),
                Size = new Size(labelW, 20)
            };
            _txtBlacklist = new TextBox
            {
                Location = new Point(12, y + 24),
                Size = new Size(310, 24)
            };
            Controls.Add(blacklistLabel);
            Controls.Add(_txtBlacklist);
            y += 56;

            Button btnAbandon = new Button
            {
                Text = "Abandon Old Quests",
                Location = new Point(12, y),
                Size = new Size(310, 30)
            };
            btnAbandon.Click += BtnAbandon_Click;
            Controls.Add(btnAbandon);
            y += 40;

            Button btnSave = new Button
            {
                Text = "Save",
                Location = new Point(12, y),
                Size = new Size(100, 26)
            };
            btnSave.Click += (_, _) => { SaveSettings(); Close(); };
            Controls.Add(btnSave);

            Button btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(120, y),
                Size = new Size(100, 26)
            };
            btnCancel.Click += (_, _) => Close();
            Controls.Add(btnCancel);

            // Shared Styx.UI skin. Legacy form: it keeps its stock CheckBox/NumericUpDown controls (Apply themes
            // them and deliberately does NOT flat-style the checkboxes, which would hide the tick). Ordering rule:
            // generic theme first, accents after.
            Theme.Apply(this);
            Theme.StyleAccentButton(btnSave);
        }

        private void LoadSettings()
        {
            _numStartDist.Value = _settings.ScanStartDistance;
            _numStep.Value = _settings.ScanStep;
            _numMaxDist.Value = _settings.ScanMaxDistance;
            _numMaxQuests.Value = _settings.MaxQuestsPerProfile;
            _numMobOverLevel.Value = _settings.MaxMobOverLevel;
            _numAutoBlacklistMin.Value = Math.Min(240, Math.Max(1, _settings.AutoBlacklistMinutes));
            _chkAutoVendor.Checked = _settings.EnableAutoVendor;
            _chkAutoTrain.Checked = _settings.EnableAutoTrain;
            _chkSellGrey.Checked = _settings.SellGrey;
            _chkSellWhite.Checked = _settings.SellWhite;
            _chkSellGreen.Checked = _settings.SellGreen;
            _chkSellBlue.Checked = _settings.SellBlue;
            _txtKeepList.Text = _settings.SellKeepList;
            _txtBlacklist.Text = _settings.BlacklistText;
        }

        private void SaveSettings()
        {
            _settings.ScanStartDistance = (int)_numStartDist.Value;
            _settings.ScanStep = (int)_numStep.Value;
            _settings.ScanMaxDistance = (int)_numMaxDist.Value;
            _settings.MaxQuestsPerProfile = (int)_numMaxQuests.Value;
            _settings.MaxMobOverLevel = (int)_numMobOverLevel.Value;
            _settings.AutoBlacklistMinutes = (int)_numAutoBlacklistMin.Value;
            _settings.EnableAutoVendor = _chkAutoVendor.Checked;
            _settings.EnableAutoTrain = _chkAutoTrain.Checked;
            _settings.SellGrey = _chkSellGrey.Checked;
            _settings.SellWhite = _chkSellWhite.Checked;
            _settings.SellGreen = _chkSellGreen.Checked;
            _settings.SellBlue = _chkSellBlue.Checked;
            _settings.SellKeepList = _txtKeepList.Text;
            _settings.BlacklistText = _txtBlacklist.Text;
            _settings.Save();   // persist via CB Settings base → Settings/VibeQuesterSettings.xml
        }

        private void BtnAbandon_Click(object sender, EventArgs e)
        {
            if (!StyxWoW.IsInGame || StyxWoW.Me == null)
            {
                MessageBox.Show("You must be in game.", "Abandon Quests", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LocalPlayer me = StyxWoW.Me;
            QuestLog questLog = me.QuestLog;
            int maxLevel = me.Level + 3;
            int abandoned = 0;

            foreach (PlayerQuest pq in questLog.GetAllQuests())
            {
                if (pq.IsCompleted) continue;

                if (pq.Level > maxLevel)
                {
                    questLog.AbandonQuestById(pq.Id);
                    abandoned++;
                }
            }

            _log?.Invoke($"Abandoned {abandoned} over-level quests.");
            MessageBox.Show($"Abandoned {abandoned} quest(s).", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
