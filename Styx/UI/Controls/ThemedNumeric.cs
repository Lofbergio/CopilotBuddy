using System;
using System.ComponentModel;   // DesignerSerializationVisibility — WFO1000 requires it on public Control props
using System.Drawing;
using System.Windows.Forms;

namespace Styx.UI
{
    // Themed integer stepper: [ − | 55 | + ]. Replaces NumericUpDown, whose spin arrows live in an internal
    // UpDownButtons control that can't be overridden or recoloured — they always render OS-light on a dark
    // surface. Click-and-hold auto-repeats and the wheel steps, so a 1..100 range stays usable without a text
    // field. Values are clamped to [Minimum, Maximum]; the glyphs dim at the limits.
    //
    // IThemeExempt: it fully self-themes; Theme.Apply leaves it alone (it still inherits the parent's Font).
    public sealed class ThemedNumeric : Control, IThemeExempt
    {
        private const int ButtonW = 22;

        private int _min;
        private int _max = 100;
        private int _value;
        private int _hover;       // 0 = none, -1 = minus, +1 = plus
        private int _repeatDir;
        // Fully qualified: 'Timer' is ambiguous with System.Threading.Timer in this assembly.
        private readonly System.Windows.Forms.Timer _repeat = new System.Windows.Forms.Timer();

        public event EventHandler ValueChanged;

        public ThemedNumeric()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer, true);
            TabStop = false;
            BackColor = Theme.PanelDark;
            ForeColor = Theme.Text;
            Size = new Size(84, 22);
            _repeat.Tick += (s, e) => { _repeat.Interval = 60; Step(_repeatDir); };
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
        public int Minimum { get => _min; set { _min = value; SetValue(_value); } }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
        public int Maximum { get => _max; set { _max = value; SetValue(_value); } }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
        public int Value { get => _value; set => SetValue(value); }

        // Step per click / wheel notch (NumericUpDown parity).
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden), Browsable(false)]
        public int Increment { get; set; } = 1;

        private void SetValue(int v)
        {
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            if (v == _value) { Invalidate(); return; }
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Step(int direction) => SetValue(_value + direction * (Increment <= 0 ? 1 : Increment));

        private int RegionAt(Point p)
        {
            if (p.X < ButtonW) return -1;
            if (p.X >= Width - ButtonW) return 1;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int region = RegionAt(e.Location);
            Cursor = region == 0 ? Cursors.Default : Cursors.Hand;
            if (region != _hover) { _hover = region; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            StopRepeat();
            if (_hover != 0) { _hover = 0; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int region = RegionAt(e.Location);
            if (region == 0) return;
            Step(region);
            _repeatDir = region;
            _repeat.Interval = 350;   // initial delay; the Tick handler drops it to 60ms
            _repeat.Start();
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); StopRepeat(); }

        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Step(e.Delta > 0 ? 1 : -1); }

        private void StopRepeat() { _repeat.Stop(); _repeatDir = 0; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _repeat.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Theme.PanelDark);

            var minus = new Rectangle(0, 0, ButtonW, Height);
            var plus = new Rectangle(Width - ButtonW, 0, ButtonW, Height);

            if (_hover != 0)
                using (var hot = new SolidBrush(Theme.HoverBg))
                    g.FillRectangle(hot, _hover < 0 ? minus : plus);

            using (var pen = new Pen(Theme.Border))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                g.DrawLine(pen, ButtonW, 0, ButtonW, Height - 1);
                g.DrawLine(pen, Width - ButtonW - 1, 0, Width - ButtonW - 1, Height - 1);
            }

            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, "−", Font, minus, _value <= _min ? Theme.Dim : Theme.GoldBright, flags);
            TextRenderer.DrawText(g, "+", Font, plus, _value >= _max ? Theme.Dim : Theme.GoldBright, flags);

            var middle = new Rectangle(ButtonW, 0, Width - 2 * ButtonW, Height);
            TextRenderer.DrawText(g, _value.ToString(), Font, middle, ForeColor, flags);
        }
    }
}
