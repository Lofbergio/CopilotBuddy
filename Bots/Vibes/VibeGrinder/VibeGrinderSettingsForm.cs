using System.Drawing;
using System.Windows.Forms;
using Styx.UI;

namespace Bots.VibeGrinder
{
    /// <summary>
    /// Settings UI: a PropertyGrid over VibeGrinderSettings with explicit Save / Cancel. Closing with
    /// the X (or Cancel) reverts in-memory edits by reloading from disk — only Save persists.
    /// Skinned with the shared Styx.UI theme (deliberately NOT a ThemedForm: that fixes the border style,
    /// and this grid wants to stay resizable).
    /// </summary>
    public class VibeGrinderSettingsForm : Form
    {
        private bool _saved;

        public VibeGrinderSettingsForm()
        {
            Text = "VibeGrinder Settings";
            Size = new Size(460, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;

            var grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = VibeGrinderSettings.Instance,
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
            save.Click += (s, e) => { VibeGrinderSettings.Instance.Save(); _saved = true; Close(); };
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
            FormClosing += (s, e) => { if (!_saved) VibeGrinderSettings.Instance.Load(); };
        }
    }
}
