using System.Drawing;
using System.Windows.Forms;
using Styx.Combat.CombatRoutine;   // WoWClass

namespace Styx.UI
{
    // Controls that paint themselves opt out of Theme.Apply by implementing this. Apply skips them entirely
    // (including their children — a custom-painted control owns its whole subtree).
    public interface IThemeExempt { }

    // ElvUI-inspired dark-slate + gold skin for the WinForms surfaces of drop-ins (routines, plugins, botbases)
    // and the built-in botbases. Lives in CopilotBuddy.dll, so every drop-in gets it for free — SourceCompiler
    // already references this assembly. No //!CompilerOption:AddRef needed for the theme itself, and because the
    // custom PAINTING lives here, a consumer never names Graphics and so never trips the GdiPlus reference trap.
    //
    // WinForms reality: a stock CheckBox paints its tick, and a NumericUpDown its spin arrows, in OS colours that
    // ignore ForeColor — both render light-on-dark and unreadable. That's why ThemedCheckBox / ThemedNumeric are
    // owner-drawn rather than merely recoloured. A TabControl is likewise un-themeable (its strip background and
    // page frame stay system-white) — use NavBar. An etched GroupBox can't be darkened — use Card.
    public static class Theme
    {
        // Put this in a Label's Tag to tell Apply "don't overwrite my colour/font" (section headers, title,
        // class-coloured lines). Without it Apply flattens every Label to parchment.
        public const string KeepTag = "styx.ui.keep";

        public static readonly Color Bg = Color.FromArgb(32, 36, 43);        // #20242b window
        public static readonly Color Panel = Color.FromArgb(40, 45, 54);     // card / control surface
        public static readonly Color PanelDark = Color.FromArgb(27, 31, 37); // insets, input wells
        public static readonly Color Text = Color.FromArgb(232, 223, 200);   // parchment
        public static readonly Color Dim = Color.FromArgb(120, 128, 140);
        public static readonly Color Gold = Color.FromArgb(211, 169, 78);    // #d3a94e accent / complete
        public static readonly Color GoldBright = Color.FromArgb(242, 214, 128);
        public static readonly Color Green = Color.FromArgb(96, 186, 96);    // partial / in-progress state
        public static readonly Color GreenBright = Color.FromArgb(132, 222, 122);
        public static readonly Color Border = Color.FromArgb(58, 64, 72);     // soft separator / divider
        public static readonly Color HoverBg = Color.FromArgb(52, 58, 68);
        public static readonly Color DownBg = Color.FromArgb(62, 68, 80);
        public static readonly Color RowHover = Color.FromArgb(52, 58, 68);   // list-row hover tint
        public static readonly Color Danger = Color.FromArgb(224, 96, 96);    // destructive action / close ✕

        // ElvUI's actual `bordercolor` default is pure BLACK (Settings/Profile.lua: {r=0,g=0,b=0}, pixelPerfect)
        // — a crisp 1px black hairline around every box is its signature. Use it for STRUCTURAL boxes (cards,
        // input wells, frameless form frames). Keep `Border` (soft grey) for dividers and interactive outlines,
        // where a black line would erase the affordance on a dark surface.
        public static readonly Color Hairline = Color.FromArgb(0, 0, 0);

        public static readonly Font UI = new Font("Segoe UI", 9f);
        public static readonly Font UIBold = new Font("Segoe UI", 9f, FontStyle.Bold);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);      // hints / helper text
        public static readonly Font Section = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Title = new Font("Segoe UI", 15f, FontStyle.Bold);

        // One shared ToolTip for the whole app. Settings dialogs without hover help are a UX gap; this makes
        // adding it a one-liner: Theme.Tip(control, "why this knob exists").
        private static readonly ToolTip Tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 100 };

        public static void Tip(Control c, string text)
        {
            if (c != null && !string.IsNullOrEmpty(text)) Tips.SetToolTip(c, text);
        }

        // Recursively theme standard controls. Call it ONCE, after every control is added — then apply accents
        // (StyleAccentButton, NavBar.Select) afterwards, or Apply will overwrite them.
        public static void Apply(Control root)
        {
            foreach (Control c in root.Controls)
            {
                if (c is IThemeExempt) continue;                 // self-painting control: hands off, don't recurse
                if (c is Label keep && KeepTag.Equals(keep.Tag))  // author owns this label's colour + font
                {
                    keep.BackColor = Color.Transparent;
                    Apply(c);
                    continue;
                }

                c.Font = UI;
                if (c is Button b) StyleButton(b);
                // NEVER flat-style a stock CheckBox. On a dark surface WinForms paints the TICK in a system
                // near-black, so the checked state goes invisible and clicks look like they did nothing (learned
                // the hard way twice — in VibeParty and again in GoodVibes). Recolour the caption only and leave
                // the OS glyph legible. For a properly dark-themed glyph use ThemedCheckBox, which owner-draws.
                else if (c is CheckBox cbx) cbx.ForeColor = Text;
                else if (c is NumericUpDown n) { n.BackColor = PanelDark; n.ForeColor = Text; n.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is TextBox t) { t.BackColor = PanelDark; t.ForeColor = Text; t.BorderStyle = BorderStyle.FixedSingle; }
                else if (c is ComboBox cb) { cb.FlatStyle = FlatStyle.Flat; cb.BackColor = PanelDark; cb.ForeColor = Text; }
                else if (c is ListBox lb) { lb.BackColor = PanelDark; lb.ForeColor = Text; lb.BorderStyle = BorderStyle.FixedSingle; }
                // PropertyGrid ignores BackColor/ForeColor — it exposes its own colour surface.
                else if (c is PropertyGrid pg)
                {
                    pg.BackColor = Bg;
                    pg.ViewBackColor = PanelDark;
                    pg.ViewForeColor = Text;
                    pg.ViewBorderColor = Border;
                    pg.LineColor = Border;
                    pg.CategoryForeColor = Gold;
                    pg.CategorySplitterColor = Border;
                    pg.HelpBackColor = Panel;
                    pg.HelpForeColor = Dim;
                    pg.HelpBorderColor = Border;
                    pg.CommandsBackColor = Panel;
                    pg.CommandsForeColor = Text;
                    pg.DisabledItemForeColor = Dim;
                }
                // A Label that was GIVEN a colour keeps it — so gold section headers survive without any tag.
                // Only default-coloured labels drop to parchment. (Font still normalises to Theme.UI; use
                // AccentLabel/KeepTag when a custom FONT must survive too, e.g. a 15pt title.)
                else if (c is Label l)
                {
                    l.BackColor = Color.Transparent;
                    if (l.ForeColor == Control.DefaultForeColor) l.ForeColor = Text;
                }

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
            b.FlatAppearance.MouseOverBackColor = HoverBg;
            b.FlatAppearance.MouseDownBackColor = DownBg;
        }

        // The primary action (Save, OK). Call AFTER Apply.
        public static void StyleAccentButton(Button b)
        {
            StyleButton(b);
            b.ForeColor = GoldBright;
            b.FlatAppearance.BorderColor = Gold;
        }

        // A label Apply won't repaint — for titles, section headers and class-coloured lines.
        public static Label AccentLabel(string text, Point location, Color color, Font font = null)
            => new Label
            {
                Text = text,
                Location = location,
                AutoSize = true,
                ForeColor = color,
                Font = font ?? UIBold,
                BackColor = Color.Transparent,
                Tag = KeepTag,
            };

        public static Color ClassColor(WoWClass c)
        {
            switch (c)
            {
                case WoWClass.Warrior: return Color.FromArgb(199, 156, 110);
                case WoWClass.Paladin: return Color.FromArgb(245, 140, 186);
                case WoWClass.Hunter: return Color.FromArgb(171, 212, 115);
                case WoWClass.Rogue: return Color.FromArgb(255, 245, 105);
                case WoWClass.Priest: return Color.FromArgb(240, 240, 240);
                case WoWClass.DeathKnight: return Color.FromArgb(196, 31, 59);
                case WoWClass.Shaman: return Color.FromArgb(60, 140, 230);
                case WoWClass.Mage: return Color.FromArgb(105, 204, 240);
                case WoWClass.Warlock: return Color.FromArgb(148, 130, 201);
                case WoWClass.Druid: return Color.FromArgb(255, 125, 10);
                default: return Text;
            }
        }
    }
}
