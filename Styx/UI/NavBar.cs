using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Styx.UI
{
    // Flat page switcher: a row of nav buttons with a gold underline on the active one, toggling the Visible
    // state of the pages you register. Replaces TabControl, which cannot be dark-themed — owner-draw only paints
    // the tab RECTS, leaving the strip background and page frame system-white.
    //
    // IThemeExempt: it styles its own buttons, so Theme.Apply must not clobber the selection state.
    //
    // Usage:
    //     var nav = new NavBar { Location = new Point(12, 72) };
    //     nav.AddPage("General", 96, generalPanel);
    //     nav.AddPage("Feral",   110, specPanel);
    //     Controls.Add(nav); Controls.Add(generalPanel); Controls.Add(specPanel);
    //     ApplyTheme();      // then…
    //     nav.Select(0);     // …selection last
    public sealed class NavBar : Panel, IThemeExempt
    {
        private const int ButtonH = 28;
        private const int UnderlineH = 3;
        private const int Gap = 4;

        private readonly List<Button> _buttons = new List<Button>();
        private readonly List<Panel> _underlines = new List<Panel>();
        private readonly List<Control> _pages = new List<Control>();
        private int _nextX;

        public NavBar()
        {
            BackColor = Theme.Bg;
            Height = ButtonH + UnderlineH;
        }

        public int SelectedIndex { get; private set; } = -1;

        // `page` may be null (a nav button with no page of its own).
        public void AddPage(string label, int width, Control page)
        {
            int index = _buttons.Count;

            var button = new Button { Text = label, Location = new Point(_nextX, 0), Size = new Size(width, ButtonH) };
            button.Click += (s, e) => Select(index);

            var underline = new Panel { Location = new Point(_nextX, ButtonH), Size = new Size(width, UnderlineH), BackColor = Theme.Gold };

            Controls.Add(button);
            Controls.Add(underline);
            _buttons.Add(button);
            _underlines.Add(underline);
            _pages.Add(page);

            _nextX += width + Gap;
            Width = Math.Max(Width, _nextX);
        }

        public void Select(int index)
        {
            SelectedIndex = index;
            for (int i = 0; i < _buttons.Count; i++)
            {
                bool active = i == index;
                var b = _buttons[i];
                Theme.StyleButton(b);
                b.BackColor = active ? Theme.Panel : Theme.Bg;
                b.ForeColor = active ? Theme.GoldBright : Theme.Dim;
                b.Font = active ? Theme.UIBold : Theme.UI;
                b.FlatAppearance.BorderColor = active ? Theme.Border : Theme.Bg;

                _underlines[i].Visible = active;
                if (_pages[i] != null) _pages[i].Visible = active;
            }
        }
    }
}
