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
			cbIsLeader.Checked = instance.IsLeader;
			cbLeaderAutoRollGreed.Checked = instance.LeaderAutoRollGreed;
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
			cbIsLeader.CheckedChanged += (sender, e) => s.IsLeader = cbIsLeader.Checked;
			cbLeaderAutoRollGreed.CheckedChanged += (sender, e) => s.LeaderAutoRollGreed = cbLeaderAutoRollGreed.Checked;
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
		private CheckBox cbIsLeader = null!;
		private CheckBox cbLeaderAutoRollGreed = null!;
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
			cbIsLeader = new CheckBox();
			cbLeaderAutoRollGreed = new CheckBox();
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

			((ISupportInitialize)nudFollowDistance).BeginInit();
			SuspendLayout();

			// label1 — "Follow Distance:" label
			label1.AutoSize = true;
			label1.Location = new System.Drawing.Point(12, 14);
			label1.Name = "label1";
			label1.TabIndex = 0;
			label1.Text = "Follow Distance:";

			// nudFollowDistance
			nudFollowDistance.Location = new System.Drawing.Point(120, 11);
			nudFollowDistance.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
			nudFollowDistance.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
			nudFollowDistance.Name = "nudFollowDistance";
			nudFollowDistance.Size = new System.Drawing.Size(55, 20);
			nudFollowDistance.TabIndex = 1;
			nudFollowDistance.Value = new decimal(new int[] { 5, 0, 0, 0 });

			// cbLootInDungeons
			cbLootInDungeons.AutoSize = true;
			cbLootInDungeons.Location = new System.Drawing.Point(12, 42);
			cbLootInDungeons.Name = "cbLootInDungeons";
			cbLootInDungeons.TabIndex = 2;
			cbLootInDungeons.Text = "Loot In Dungeons";
			cbLootInDungeons.UseVisualStyleBackColor = true;

			// cbWaitForRessInDungeons
			cbWaitForRessInDungeons.AutoSize = true;
			cbWaitForRessInDungeons.Location = new System.Drawing.Point(12, 68);
			cbWaitForRessInDungeons.Name = "cbWaitForRessInDungeons";
			cbWaitForRessInDungeons.TabIndex = 3;
			cbWaitForRessInDungeons.Text = "Wait For Ress In Dungeons";
			cbWaitForRessInDungeons.UseVisualStyleBackColor = true;

			// cbAcceptBattlefieldPorts — moved next to other checkboxes
			cbAcceptBattlefieldPorts.AutoSize = true;
			cbAcceptBattlefieldPorts.Location = new System.Drawing.Point(12, 94);
			cbAcceptBattlefieldPorts.Name = "cbAcceptBattlefieldPorts";
			cbAcceptBattlefieldPorts.TabIndex = 4;
			cbAcceptBattlefieldPorts.Text = "Accept Battlefield Ports";
			cbAcceptBattlefieldPorts.UseVisualStyleBackColor = true;

			// cbAcceptGroupInvitesFromLeader
			cbAcceptGroupInvitesFromLeader.AutoSize = true;
			cbAcceptGroupInvitesFromLeader.Location = new System.Drawing.Point(12, 120);
			cbAcceptGroupInvitesFromLeader.Name = "cbAcceptGroupInvitesFromLeader";
			cbAcceptGroupInvitesFromLeader.TabIndex = 5;
			cbAcceptGroupInvitesFromLeader.Text = "Accept Group Invites From Leader";
			cbAcceptGroupInvitesFromLeader.UseVisualStyleBackColor = true;

			// cbAcceptDungeonInvites
			cbAcceptDungeonInvites.AutoSize = true;
			cbAcceptDungeonInvites.Location = new System.Drawing.Point(12, 146);
			cbAcceptDungeonInvites.Name = "cbAcceptDungeonInvites";
			cbAcceptDungeonInvites.TabIndex = 6;
			cbAcceptDungeonInvites.Text = "Accept Dungeon Invites";
			cbAcceptDungeonInvites.UseVisualStyleBackColor = true;

			// cbAutoAcceptSharedQuests
			cbAutoAcceptSharedQuests.AutoSize = true;
			cbAutoAcceptSharedQuests.Location = new System.Drawing.Point(12, 172);
			cbAutoAcceptSharedQuests.Name = "cbAutoAcceptSharedQuests";
			cbAutoAcceptSharedQuests.TabIndex = 7;
			cbAutoAcceptSharedQuests.Text = "Auto Accept Shared Quests";
			cbAutoAcceptSharedQuests.UseVisualStyleBackColor = true;

			// cbDoNothing
			cbDoNothing.AutoSize = true;
			cbDoNothing.Location = new System.Drawing.Point(12, 198);
			cbDoNothing.Name = "cbDoNothing";
			cbDoNothing.TabIndex = 8;
			cbDoNothing.Text = "Do Nothing (idle)";
			cbDoNothing.UseVisualStyleBackColor = true;

			// cbIsLeader
			cbIsLeader.AutoSize = true;
			cbIsLeader.Location = new System.Drawing.Point(12, 224);
			cbIsLeader.Name = "cbIsLeader";
			cbIsLeader.TabIndex = 9;
			cbIsLeader.Text = "This is the Leader (broadcast + idle)";
			cbIsLeader.UseVisualStyleBackColor = true;

			// cbLeaderAutoRollGreed
			cbLeaderAutoRollGreed.AutoSize = true;
			cbLeaderAutoRollGreed.Location = new System.Drawing.Point(12, 250);
			cbLeaderAutoRollGreed.Name = "cbLeaderAutoRollGreed";
			cbLeaderAutoRollGreed.TabIndex = 10;
			cbLeaderAutoRollGreed.Text = "Leader: auto-Greed loot (off if using AutoEquip2)";
			cbLeaderAutoRollGreed.UseVisualStyleBackColor = true;

			// btnLevelbotSettings
			btnLevelbotSettings.Location = new System.Drawing.Point(12, 288);
			btnLevelbotSettings.Name = "btnLevelbotSettings";
			btnLevelbotSettings.Size = new System.Drawing.Size(150, 27);
			btnLevelbotSettings.TabIndex = 11;
			btnLevelbotSettings.Text = "Levelbot Settings...";
			btnLevelbotSettings.UseVisualStyleBackColor = true;

			// btnSaveAndClose
			btnSaveAndClose.Location = new System.Drawing.Point(170, 288);
			btnSaveAndClose.Name = "btnSaveAndClose";
			btnSaveAndClose.Size = new System.Drawing.Size(110, 27);
			btnSaveAndClose.TabIndex = 12;
			btnSaveAndClose.Text = "Save && Close";
			btnSaveAndClose.UseVisualStyleBackColor = true;

			// FormConfig
			AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
			AutoScaleMode = AutoScaleMode.Font;
			ClientSize = new System.Drawing.Size(294, 330);
			Controls.Add(label1);
			Controls.Add(nudFollowDistance);
			Controls.Add(cbLootInDungeons);
			Controls.Add(cbWaitForRessInDungeons);
			Controls.Add(cbAcceptBattlefieldPorts);
			Controls.Add(cbAcceptGroupInvitesFromLeader);
			Controls.Add(cbAcceptDungeonInvites);
			Controls.Add(cbAutoAcceptSharedQuests);
			Controls.Add(cbDoNothing);
			Controls.Add(cbIsLeader);
			Controls.Add(cbLeaderAutoRollGreed);
			Controls.Add(btnLevelbotSettings);
			Controls.Add(btnSaveAndClose);
			FormBorderStyle = FormBorderStyle.FixedSingle;
			MaximizeBox = false;
			MinimizeBox = false;
			Name = "FormConfig";
			Text = "PartyBot Configuration";
			Load += new EventHandler(FormConfig_Load);

			((ISupportInitialize)nudFollowDistance).EndInit();
			ResumeLayout(false);
			PerformLayout();
		}
	}
}
