using System.Drawing;
using System.Windows.Forms;
using Styx.UI;

namespace Bots.Vibes.Shared
{
    /// <summary>
    /// Loot-policy UI for the whole Vibe suite: a PropertyGrid over the shared
    /// <see cref="VibesLootSettings"/> with explicit Save / Cancel. Closing with the X (or Cancel)
    /// reverts in-memory edits by reloading from disk — only Save persists.
    ///
    /// ONE form for every bot on purpose. VibeGrinder shows this policy inline in its own grid;
    /// VibeParty's config is a hand-built form, so it opens this dialog instead. Either way both edit
    /// the same instance, which is the point — a VibeParty user previously had no way to see or change
    /// their loot policy at all and silently inherited VibeGrinder's.
    ///
    /// Skinned with the shared Styx.UI theme (deliberately NOT a ThemedForm: that fixes the border
    /// style, and this grid wants to stay resizable). Mirrors VibeGrinderSettingsForm.
    /// </summary>
    public class VibesLootSettingsForm : Form
    {
        private bool _saved;

        public VibesLootSettingsForm()
        {
            Text = "Vibe Loot Policy (shared by all Vibe bots)";
            Size = new Size(460, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;

            var grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = VibesLootSettings.Instance,
                PropertySort = PropertySort.Categorized,
                ToolbarVisible = false,
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(8),
                Height = 44,
            };

            var save = new Button { Text = "Save", DialogResult = DialogResult.OK, Width = 90 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
            save.Click += (s, e) => { VibesLootSettings.Instance.Save(); _saved = true; Close(); };
            cancel.Click += (s, e) => Close();
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);

            Controls.Add(grid);
            Controls.Add(buttons);
            AcceptButton = save;
            CancelButton = cancel;

            // Styx.UI ordering rule: generic theme first (this also skins the PropertyGrid), accents after.
            Theme.Apply(this);
            buttons.BackColor = Theme.Bg;
            Theme.StyleAccentButton(save);

            // Discard edits on any close path that wasn't an explicit Save.
            FormClosing += (s, e) => { if (!_saved) VibesLootSettings.Instance.Load(); };
        }
    }
}
