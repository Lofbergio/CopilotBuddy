using System.Drawing;
using System.Windows.Forms;

namespace Bots.VibeGrinder
{
    /// <summary>
    /// Minimal settings UI: a PropertyGrid over VibeGrinderSettings. Saves on close.
    /// </summary>
    public class VibeGrinderSettingsForm : Form
    {
        public VibeGrinderSettingsForm()
        {
            Text = "VibeGrinder Settings";
            Size = new Size(460, 600);
            StartPosition = FormStartPosition.CenterScreen;

            var grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = VibeGrinderSettings.Instance,
                PropertySort = PropertySort.Categorized,
                ToolbarVisible = false,
            };
            Controls.Add(grid);

            FormClosing += (s, e) => VibeGrinderSettings.Instance.Save();
        }
    }
}
