using System.Drawing;
using System.Windows.Forms;

namespace VibeParty.Forms
{
    // ElvUI-style dark-slate + gold palette, mirrored from the VibeTalents look.
    // (VibeTalents' Theme lives in the runtime-compiled plugin assembly, so the DLL-side
    // VibeParty can't reference it — this is a trimmed local copy kept visually in sync.)
    internal static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(32, 36, 43);        // #20242b
        public static readonly Color Panel = Color.FromArgb(40, 45, 54);     // control surfaces
        public static readonly Color Text = Color.FromArgb(232, 223, 200);   // parchment
        public static readonly Color Dim = Color.FromArgb(120, 128, 140);
        public static readonly Color Gold = Color.FromArgb(211, 169, 78);    // #d3a94e
        public static readonly Color GoldBright = Color.FromArgb(242, 214, 128);
        public static readonly Color Border = Color.FromArgb(58, 64, 72);

        public static readonly Font UI = new Font("Segoe UI", 9f);
        public static readonly Font UIBold = new Font("Segoe UI", 9f, FontStyle.Bold);

        // Recursively theme standard WinForms controls. Labels keep any non-default colour
        // they were given (so gold section headers survive), everything else gets the palette.
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                c.Font = UI;
                if (c is Button b) StyleButton(b);
                // Keep the system check glyph (clearly visible + toggles); flat-styling it on a dark
                // bg made the checked state invisible, so clicks looked like they did nothing.
                else if (c is CheckBox cbx) { cbx.ForeColor = Text; cbx.BackColor = Bg; }
                else if (c is NumericUpDown n) { n.BackColor = Panel; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is TextBox t) { t.BackColor = Panel; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is Label l) { l.BackColor = Color.Transparent; if (l.ForeColor == Control.DefaultForeColor) l.ForeColor = Text; }

                Apply(c);
            }
        }

        public static void StyleButton(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Panel;
            b.ForeColor = Text;
            b.UseVisualStyleBackColor = false;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 58, 68);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(62, 68, 80);
        }

        public static void StyleAccentButton(Button b)
        {
            StyleButton(b);
            b.ForeColor = GoldBright;
            b.FlatAppearance.BorderColor = Gold;
        }
    }
}
