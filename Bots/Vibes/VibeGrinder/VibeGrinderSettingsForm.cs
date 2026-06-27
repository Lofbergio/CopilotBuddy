using System.Drawing;
using System.Windows.Forms;

namespace Bots.VibeGrinder
{
    /// <summary>
    /// Settings UI: a PropertyGrid over VibeGrinderSettings with explicit Save / Cancel. Closing with
    /// the X (or Cancel) reverts in-memory edits by reloading from disk — only Save persists.
    /// </summary>
    public class VibeGrinderSettingsForm : Form
    {
        private bool _saved;

        public VibeGrinderSettingsForm()
        {
            Text = "VibeGrinder Settings";
            Size = new Size(460, 620);
            StartPosition = FormStartPosition.CenterScreen;

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

            // Discard edits on any close path that wasn't an explicit Save.
            FormClosing += (s, e) => { if (!_saved) VibeGrinderSettings.Instance.Load(); };
        }
    }
}
