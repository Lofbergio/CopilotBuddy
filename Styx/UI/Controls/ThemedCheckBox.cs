using System;
using System.Drawing;
using System.Windows.Forms;

namespace Styx.UI
{
    // Owner-drawn checkbox. A stock CheckBox with FlatStyle.Flat paints its CHECKMARK in a system near-black
    // regardless of ForeColor, so on a dark surface the tick is invisible — recolouring the box isn't enough.
    // We draw the lot: a gold-filled box with a DARK tick when checked (max contrast), an outlined empty box
    // when not, a gold hover border, then the label.
    //
    // IThemeExempt: it fully self-themes; Theme.Apply leaves it alone (it still inherits the parent's Font).
    public sealed class ThemedCheckBox : CheckBox, IThemeExempt
    {
        private const int BoxSize = 14;
        private bool _hover;

        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            AutoSize = false;
            Size = new Size(300, 20);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Cursor = Cursors.Hand;
        }

        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; base.OnMouseLeave(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            var box = new Rectangle(0, (Height - BoxSize) / 2, BoxSize, BoxSize);

            using (var fill = new SolidBrush(Checked ? Theme.Gold : Theme.PanelDark))
                g.FillRectangle(fill, box);
            using (var pen = new Pen(Checked ? Theme.GoldBright : (_hover ? Theme.Gold : Theme.Border)))
                g.DrawRectangle(pen, box);

            if (Checked)
                using (var tick = new Pen(Theme.Bg, 2f))   // dark tick on gold — legible at a glance
                    g.DrawLines(tick, new[]
                    {
                        new Point(box.Left + 3, box.Top + 7),
                        new Point(box.Left + 6, box.Top + 10),
                        new Point(box.Left + 11, box.Top + 4),
                    });

            TextRenderer.DrawText(g, Text, Font,
                new Rectangle(BoxSize + 8, 0, Width - BoxSize - 8, Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
