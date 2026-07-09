using System.Drawing;
using System.Windows.Forms;

namespace Styx.UI
{
    // A flat titled section box, with NO custom painting: a 1px outer Panel in the border colour padding an
    // inner Panel in the surface colour. Replaces GroupBox, whose etched border is drawn by the OS and can't be
    // darkened.
    //
    // Add controls to .Content (NOT to the Card itself). First row sits at y≈34; rows step by ~28.
    public sealed class Card : Panel
    {
        public Panel Content { get; }

        public Card(string title, Point location, Size size)
        {
            Location = location;
            Size = size;
            BackColor = Theme.Hairline;   // ElvUI's signature: a crisp 1px BLACK hairline around every box
            Padding = new Padding(1);

            Content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel };
            Controls.Add(Content);

            if (!string.IsNullOrEmpty(title))
            {
                var header = Theme.AccentLabel(title.ToUpperInvariant(), new Point(12, 8), Theme.Gold);
                header.BackColor = Theme.Panel;
                Content.Controls.Add(header);
            }
        }
    }
}
