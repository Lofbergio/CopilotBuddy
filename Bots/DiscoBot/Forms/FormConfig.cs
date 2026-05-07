using System;
using System.ComponentModel;
using System.Windows.Forms;
using Bots.Grind;
using Levelbot;

namespace PartyBot.Forms
{
	public partial class FormConfig : Form
	{
		public FormConfig()
		{
			InitializeComponent();
		}

		private void FormConfig_Load(object sender, EventArgs e)
		{
			LoadSettings();
			HookEvents();
		}

		private void LoadSettings()
		{
			PartyBotSettings instance = PartyBotSettings.Instance;
			if (instance.FollowDistance >= (int)nudFollowDistance.Minimum && instance.FollowDistance <= (int)nudFollowDistance.Maximum)
				nudFollowDistance.Value = instance.FollowDistance;
			cbLootInDungeons.Checked = instance.LootInDungeons;
			cbWaitForRessInDungeons.Checked = instance.WaitForRessInDungeons;
			cbAcceptBattlefieldPorts.Checked = instance.AcceptBattlefieldPorts;
			cbAcceptDungeonInvites.Checked = instance.AcceptDungeonInvites;
			cbAcceptGroupInvitesFromLeader.Checked = instance.AcceptGroupInvitesFromLeader;
			cbAutoAcceptSharedQuests.Checked = instance.AutoAcceptSharedQuests;
			cbDoNothing.Checked = instance.DoNothing;
		}

		private void HookEvents()
		{
			PartyBotSettings s = PartyBotSettings.Instance;
			nudFollowDistance.ValueChanged += (sender, e) => s.FollowDistance = (int)nudFollowDistance.Value;
			cbLootInDungeons.CheckedChanged += (sender, e) => s.LootInDungeons = cbLootInDungeons.Checked;
			cbWaitForRessInDungeons.CheckedChanged += (sender, e) => s.WaitForRessInDungeons = cbWaitForRessInDungeons.Checked;
			cbAcceptBattlefieldPorts.CheckedChanged += (sender, e) => s.AcceptBattlefieldPorts = cbAcceptBattlefieldPorts.Checked;
			cbAcceptDungeonInvites.CheckedChanged += (sender, e) => s.AcceptDungeonInvites = cbAcceptDungeonInvites.Checked;
			cbAcceptGroupInvitesFromLeader.CheckedChanged += (sender, e) => s.AcceptGroupInvitesFromLeader = cbAcceptGroupInvitesFromLeader.Checked;
			cbAutoAcceptSharedQuests.CheckedChanged += (sender, e) => s.AutoAcceptSharedQuests = cbAutoAcceptSharedQuests.Checked;
			cbDoNothing.CheckedChanged += (sender, e) => s.DoNothing = cbDoNothing.Checked;
			btnSaveAndClose.Click += btnSaveAndClose_Click;
			btnLevelbotSettings.Click += (sender, e) =>
			{
				FormLevelbotSettings form = new FormLevelbotSettings();
				form.ShowDialog();
			};
		}

		private void btnSaveAndClose_Click(object sender, EventArgs e)
		{
			PartyBotSettings.Instance.Save();
			Close();
		}

		// Designer-generated controls
		private IContainer? components;
		private SplitContainer splitContainer1 = null!;
		private CheckBox cbDoNothing = null!;
		private CheckBox cbAutoAcceptSharedQuests = null!;
		private CheckBox cbWaitForRessInDungeons = null!;
		private CheckBox cbLootInDungeons = null!;
		private NumericUpDown nudFollowDistance = null!;
		private CheckBox cbAcceptGroupInvitesFromLeader = null!;
		private Label label1 = null!;
		private CheckBox cbAcceptBattlefieldPorts = null!;
		private CheckBox cbAcceptDungeonInvites = null!;
		private Button btnLevelbotSettings = null!;
		private Button btnSaveAndClose = null!;

		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null)
				components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			splitContainer1 = new SplitContainer();
			cbDoNothing = new CheckBox();
			cbAutoAcceptSharedQuests = new CheckBox();
			cbWaitForRessInDungeons = new CheckBox();
			cbLootInDungeons = new CheckBox();
			nudFollowDistance = new NumericUpDown();
			cbAcceptGroupInvitesFromLeader = new CheckBox();
			label1 = new Label();
			cbAcceptBattlefieldPorts = new CheckBox();
			cbAcceptDungeonInvites = new CheckBox();
			btnLevelbotSettings = new Button();
			btnSaveAndClose = new Button();

			splitContainer1.Panel1.SuspendLayout();
			splitContainer1.Panel2.SuspendLayout();
			splitContainer1.SuspendLayout();
			((ISupportInitialize)nudFollowDistance).BeginInit();
			SuspendLayout();

			// splitContainer1
			splitContainer1.Dock = DockStyle.Fill;
			splitContainer1.Location = new System.Drawing.Point(0, 0);
			splitContainer1.Name = "splitContainer1";
			splitContainer1.Size = new System.Drawing.Size(480, 280);
			splitContainer1.SplitterDistance = 300;
			splitContainer1.TabIndex = 0;
			splitContainer1.Panel1.Controls.Add(cbDoNothing);
			splitContainer1.Panel1.Controls.Add(cbAutoAcceptSharedQuests);
			splitContainer1.Panel1.Controls.Add(cbWaitForRessInDungeons);
			splitContainer1.Panel1.Controls.Add(cbLootInDungeons);
			splitContainer1.Panel1.Controls.Add(nudFollowDistance);
			splitContainer1.Panel1.Controls.Add(cbAcceptGroupInvitesFromLeader);
			splitContainer1.Panel1.Controls.Add(label1);
			splitContainer1.Panel1.Controls.Add(cbAcceptDungeonInvites);
			splitContainer1.Panel2.Controls.Add(btnLevelbotSettings);
			splitContainer1.Panel2.Controls.Add(btnSaveAndClose);
			splitContainer1.Panel2.Controls.Add(cbAcceptBattlefieldPorts);

			// label1
			label1.AutoSize = true;
			label1.Location = new System.Drawing.Point(3, 6);
			label1.Name = "label1";
			label1.Size = new System.Drawing.Size(87, 13);
			label1.TabIndex = 0;
			label1.Text = "Follow Distance:";

			// nudFollowDistance
			nudFollowDistance.Location = new System.Drawing.Point(96, 3);
			nudFollowDistance.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
			nudFollowDistance.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudFollowDistance.Name = "nudFollowDistance";
			nudFollowDistance.Size = new System.Drawing.Size(50, 20);
			nudFollowDistance.TabIndex = 1;
			nudFollowDistance.Value = new decimal(new int[] { 5, 0, 0, 0 });

			// cbLootInDungeons
			cbLootInDungeons.AutoSize = true;
			cbLootInDungeons.Location = new System.Drawing.Point(3, 30);
			cbLootInDungeons.Name = "cbLootInDungeons";
			cbLootInDungeons.Size = new System.Drawing.Size(107, 17);
			cbLootInDungeons.TabIndex = 2;
			cbLootInDungeons.Text = "Loot In Dungeons";
			cbLootInDungeons.UseVisualStyleBackColor = true;

			// cbWaitForRessInDungeons
			cbWaitForRessInDungeons.AutoSize = true;
			cbWaitForRessInDungeons.Location = new System.Drawing.Point(3, 53);
			cbWaitForRessInDungeons.Name = "cbWaitForRessInDungeons";
			cbWaitForRessInDungeons.Size = new System.Drawing.Size(152, 17);
			cbWaitForRessInDungeons.TabIndex = 3;
			cbWaitForRessInDungeons.Text = "Wait For Ress In Dungeons";
			cbWaitForRessInDungeons.UseVisualStyleBackColor = true;

			// cbAcceptGroupInvitesFromLeader
			cbAcceptGroupInvitesFromLeader.AutoSize = true;
			cbAcceptGroupInvitesFromLeader.Location = new System.Drawing.Point(3, 76);
			cbAcceptGroupInvitesFromLeader.Name = "cbAcceptGroupInvitesFromLeader";
			cbAcceptGroupInvitesFromLeader.Size = new System.Drawing.Size(188, 17);
			cbAcceptGroupInvitesFromLeader.TabIndex = 4;
			cbAcceptGroupInvitesFromLeader.Text = "Accept Group Invites From Leader";
			cbAcceptGroupInvitesFromLeader.UseVisualStyleBackColor = true;

			// cbAcceptDungeonInvites
			cbAcceptDungeonInvites.AutoSize = true;
			cbAcceptDungeonInvites.Location = new System.Drawing.Point(3, 99);
			cbAcceptDungeonInvites.Name = "cbAcceptDungeonInvites";
			cbAcceptDungeonInvites.Size = new System.Drawing.Size(145, 17);
			cbAcceptDungeonInvites.TabIndex = 5;
			cbAcceptDungeonInvites.Text = "Accept Dungeon Invites";
			cbAcceptDungeonInvites.UseVisualStyleBackColor = true;

			// cbAutoAcceptSharedQuests
			cbAutoAcceptSharedQuests.AutoSize = true;
			cbAutoAcceptSharedQuests.Location = new System.Drawing.Point(3, 122);
			cbAutoAcceptSharedQuests.Name = "cbAutoAcceptSharedQuests";
			cbAutoAcceptSharedQuests.Size = new System.Drawing.Size(162, 17);
			cbAutoAcceptSharedQuests.TabIndex = 6;
			cbAutoAcceptSharedQuests.Text = "Auto Accept Shared Quests";
			cbAutoAcceptSharedQuests.UseVisualStyleBackColor = true;

			// cbDoNothing
			cbDoNothing.AutoSize = true;
			cbDoNothing.Location = new System.Drawing.Point(3, 145);
			cbDoNothing.Name = "cbDoNothing";
			cbDoNothing.Size = new System.Drawing.Size(124, 17);
			cbDoNothing.TabIndex = 7;
			cbDoNothing.Text = "Do Nothing (Leader)";
			cbDoNothing.UseVisualStyleBackColor = true;

			// cbAcceptBattlefieldPorts
			cbAcceptBattlefieldPorts.AutoSize = true;
			cbAcceptBattlefieldPorts.Location = new System.Drawing.Point(3, 6);
			cbAcceptBattlefieldPorts.Name = "cbAcceptBattlefieldPorts";
			cbAcceptBattlefieldPorts.Size = new System.Drawing.Size(140, 17);
			cbAcceptBattlefieldPorts.TabIndex = 0;
			cbAcceptBattlefieldPorts.Text = "Accept Battlefield Ports";
			cbAcceptBattlefieldPorts.UseVisualStyleBackColor = true;

			// btnLevelbotSettings
			btnLevelbotSettings.Location = new System.Drawing.Point(3, 30);
			btnLevelbotSettings.Name = "btnLevelbotSettings";
			btnLevelbotSettings.Size = new System.Drawing.Size(160, 23);
			btnLevelbotSettings.TabIndex = 1;
			btnLevelbotSettings.Text = "Levelbot Settings...";
			btnLevelbotSettings.UseVisualStyleBackColor = true;

			// btnSaveAndClose
			btnSaveAndClose.Location = new System.Drawing.Point(3, 59);
			btnSaveAndClose.Name = "btnSaveAndClose";
			btnSaveAndClose.Size = new System.Drawing.Size(160, 23);
			btnSaveAndClose.TabIndex = 2;
			btnSaveAndClose.Text = "Save && Close";
			btnSaveAndClose.UseVisualStyleBackColor = true;

			// FormConfig
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new System.Drawing.Size(480, 280);
			Controls.Add(splitContainer1);
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			MinimizeBox = false;
			Name = "FormConfig";
			Text = "PartyBot Configuration";
			Load += new EventHandler(FormConfig_Load);

			splitContainer1.Panel1.ResumeLayout(false);
			splitContainer1.Panel1.PerformLayout();
			splitContainer1.Panel2.ResumeLayout(false);
			splitContainer1.Panel2.PerformLayout();
			splitContainer1.ResumeLayout(false);
			((ISupportInitialize)nudFollowDistance).EndInit();
			ResumeLayout(false);
		}
	}
}
