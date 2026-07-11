using System;
using System.Drawing;
using System.Windows.Forms;
using Levelbot;
using Styx.UI;   // shared design system — was a local copy of the palette (Forms/Theme.cs, now deleted)

namespace VibeParty.Forms
{
    // ElvUI-styled config for VibeParty (code-built, no designer). Role-aware: the Leader/Follower choice decides
    // which knobs show — no point showing follower settings to a leader (2026-07-07). Only the two mode switches
    // are always visible; the rest lives in a panel rebuilt when the role flips.
    public class VibePartyConfig : ThemedForm
    {
        private readonly VibePartySettings _s = VibePartySettings.Instance;
        private Panel _rolePanel;

        public VibePartyConfig()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "VibeParty Configuration";

            // AccentLabel keeps its colour AND font through Theme.Apply (a plain gold Label would keep the colour
            // but get its font normalised to Theme.UI).
            Controls.Add(Theme.AccentLabel("VibeParty", new Point(16, 12), Theme.Gold, Theme.Title));
            Controls.Add(Theme.AccentLabel("Party follower & coordination", new Point(18, 42), Theme.Dim, Theme.UI));

            int y = 74;
            Section(this, "Mode", ref y);
            // Master switch — decides which knobs the panel shows. Leader also auto-buffs (see Root); Do Nothing
            // is the true idle. If both are set, Do Nothing wins.
            Check(this, "This is the Leader (drive manually, auto-buff you + party)", () => _s.IsLeader, v => { _s.IsLeader = v; RebuildRolePanel(); }, ref y);
            Check(this, "Do Nothing (completely idle: no buff / combat / follow)", () => _s.DoNothing, v => _s.DoNothing = v, ref y);

            y += 8;
            _rolePanel = new Panel { Location = new Point(0, y), Width = 380, BackColor = Theme.Bg };
            Controls.Add(_rolePanel);
            RebuildRolePanel();
        }

        // Rebuild only the role-specific knobs + buttons when the Leader toggle flips. The mode checkboxes above
        // live on the form (not this panel), so toggling never disposes the control that fired the event.
        private void RebuildRolePanel()
        {
            _rolePanel.SuspendLayout();
            _rolePanel.Controls.Clear();
            int y = 0;

            if (_s.IsLeader)
            {
                Section(_rolePanel, "Leader", ref y);
                Hint(_rolePanel, "The routine keeps buffs up on you + the party (out of", ref y, 18);
                Hint(_rolePanel, "combat). You drive movement and combat yourself.", ref y, 26);
                Check(_rolePanel, "Share quests to party (native /share)", () => _s.ShareQuestsToParty, v => _s.ShareQuestsToParty = v, ref y);
            }
            else
            {
                Section(_rolePanel, "Following", ref y);
                _rolePanel.Controls.Add(new Label { Text = "Follow distance", Location = new Point(20, y + 3), AutoSize = true, ForeColor = Theme.Text });
                var nud = new ThemedNumeric { Location = new Point(150, y), Size = new Size(84, 22), Minimum = 1, Maximum = 30, Value = _s.FollowDistance };
                nud.ValueChanged += (s, e) => _s.FollowDistance = nud.Value;
                _rolePanel.Controls.Add(nud);
                y += 34;

                y += 8;
                Section(_rolePanel, "Party", ref y);
                // Group invites from the leader are ALWAYS accepted (no toggle — safe by construction).
                Check(_rolePanel, "Auto accept shared quests", () => _s.AutoAcceptSharedQuests, v => _s.AutoAcceptSharedQuests = v, ref y);
                Check(_rolePanel, "Turn in quests at leader's NPC", () => _s.AutoTurnInQuests, v => _s.AutoTurnInQuests = v, ref y);
                Check(_rolePanel, "Accept dungeon invites", () => _s.AcceptDungeonInvites, v => _s.AcceptDungeonInvites = v, ref y);

                y += 8;
                Section(_rolePanel, "Dungeons", ref y);
                Check(_rolePanel, "Loot in dungeons", () => _s.LootInDungeons, v => _s.LootInDungeons = v, ref y);
                Check(_rolePanel, "Wait for res in dungeons", () => _s.WaitForRessInDungeons, v => _s.WaitForRessInDungeons = v, ref y);
            }

            y += 14;
            var btnLevelbot = MakeButton("Levelbot Settings…", 20, y, 160, (s, e) => { using (var f = new FormLevelbotSettings()) f.ShowDialog(this); });
            var btnSave = MakeButton("Save && Close", 260, y, 100, (s, e) => { _s.Save(); Close(); });
            _rolePanel.Controls.Add(btnLevelbot);
            _rolePanel.Controls.Add(btnSave);
            y += 42;

            _rolePanel.Height = y;
            _rolePanel.ResumeLayout();
            ClientSize = new Size(380, _rolePanel.Top + y);

            // Styx.UI ordering rule: generic theme first, accents after.
            ApplyTheme();
            Theme.StyleAccentButton(btnSave);
        }

        private void Section(Control parent, string text, ref int y)
        {
            // Gold survives Theme.Apply untagged (Apply only repaints default-coloured labels).
            parent.Controls.Add(new Label { Text = text.ToUpperInvariant(), Location = new Point(16, y), AutoSize = true, ForeColor = Theme.Gold, Font = Theme.UIBold });
            y += 24;
        }

        private void Hint(Control parent, string text, ref int y, int step)
        {
            parent.Controls.Add(new Label { Text = text, Location = new Point(20, y), AutoSize = true, ForeColor = Theme.Dim });
            y += step;
        }

        private void Check(Control parent, string text, Func<bool> get, Action<bool> set, ref int y)
        {
            // ThemedCheckBox owner-draws its glyph: a stock CheckBox on a dark surface paints the TICK in a system
            // near-black, so the checked state is invisible and clicks look like they did nothing.
            var cb = new ThemedCheckBox { Text = text, Location = new Point(20, y), Size = new Size(340, 20), Checked = get() };
            cb.CheckedChanged += (s, e) => set(cb.Checked);
            parent.Controls.Add(cb);
            y += 26;
        }

        private static Button MakeButton(string text, int x, int y, int w, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, 26) };
            b.Click += onClick;
            return b;
        }
    }
}
