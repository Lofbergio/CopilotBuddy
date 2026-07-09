using System.Drawing;
using System.Windows.Forms;

namespace Styx.UI
{
    // Vertical layout cursor for hand-authored settings dialogs — lifted from GuildRecruiter's ConfigForm, which
    // proved the pattern: a running `y`, a Section header with an underline, label+input Rows with tooltips, and
    // muted Hints. Without it every form re-invents its own `_y += 32` bookkeeping and the spacing drifts apart.
    //
    // Absolute-positioned Cards (see GVSettingsForm) remain fine for grid-ish layouts; Stack is for the common
    // "column of labelled knobs" form.
    //
    // Usage:
    //     var s = new Stack(this, width: 500);
    //     s.Section("Targeting");
    //     s.Row("Minimum level", minLevelControl, "Skip players below this level.");
    //     s.Hint("Params: {player} {level}");
    //     ClientSize = new Size(s.Width, s.Y);
    public sealed class Stack
    {
        private readonly Control _host;

        public Stack(Control host, int width, int pad = 20, int labelWidth = 170, int top = 0)
        {
            _host = host;
            Width = width;
            Pad = pad;
            LabelWidth = labelWidth;
            Y = top == 0 ? pad : top;
        }

        public int Y { get; set; }
        public int Width { get; }
        public int Pad { get; }
        public int LabelWidth { get; }

        /// X coordinate where an input control starts (right of the label column).
        public int InputX => Pad + LabelWidth;

        public T Add<T>(T c) where T : Control { _host.Controls.Add(c); return c; }

        public void Space(int px) => Y += px;

        public void Divider(int gapAbove = 0, int gapBelow = 10)
        {
            Y += gapAbove;
            Add(new Panel { BackColor = Theme.Border, Location = new Point(Pad, Y), Size = new Size(Width - 2 * Pad, 1) });
            Y += gapBelow;
        }

        // Gold header + hairline underline. Tagged so Theme.Apply keeps the colour AND the semibold font.
        public void Section(string title)
        {
            Y += 6;
            var l = Theme.AccentLabel(title.ToUpperInvariant(), new Point(Pad, Y), Theme.Gold, Theme.Section);
            l.UseMnemonic = false;   // an '&' in a section title must render, not become an access key
            Add(l);
            Y += 21;
            Divider();
        }

        // A labelled input row. `input` is positioned + sized for you; `tip` (optional) becomes hover help.
        public void Row(string label, Control input, string tip = null, int inputWidth = 84, int height = 22)
        {
            Add(new Label { Text = label, ForeColor = Theme.Text, AutoSize = true, UseMnemonic = false, Location = new Point(Pad, Y + 3) });
            input.SetBounds(InputX, Y, inputWidth, height);
            Theme.Tip(input, tip);
            Add(input);
            Y += height + 10;
        }

        // A full-width control (list box, text area…) with an optional caption above it.
        public void Block(Control control, int height, string caption = null, string tip = null)
        {
            if (!string.IsNullOrEmpty(caption))
            {
                Add(new Label { Text = caption, ForeColor = Theme.Text, AutoSize = true, UseMnemonic = false, Location = new Point(Pad, Y) });
                Y += 20;
            }
            control.SetBounds(Pad, Y, Width - 2 * Pad, height);
            Theme.Tip(control, tip);
            Add(control);
            Y += height + 6;
        }

        public void Hint(string text)
        {
            Add(new Label
            {
                Text = text,
                ForeColor = Theme.Dim,
                Font = Theme.Small,
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(Pad, Y),
                Tag = Theme.KeepTag,   // keep the smaller font through Theme.Apply
            });
            Y += 20;
        }

        // A checkbox row (glyph + caption), full width.
        public ThemedCheckBox Check(string text, bool value, string tip = null)
        {
            var cb = new ThemedCheckBox
            {
                Text = text,
                Checked = value,
                Location = new Point(Pad, Y),
                Size = new Size(Width - 2 * Pad, 20),
            };
            Theme.Tip(cb, tip);
            Add(cb);
            Y += 26;
            return cb;
        }
    }
}
