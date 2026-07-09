using System.Windows.Forms;

namespace Styx.UI
{
    // Dark base dialog for drop-in settings windows. Sets the surface + font; deliberately does NOT auto-apply
    // the theme, because Apply must run once AFTER the derived class has added its controls — and any accents
    // (StyleAccentButton, NavBar.Select) must run after Apply, or Apply overwrites them.
    //
    // Usage:
    //     class MySettings : ThemedForm {
    //         MySettings() { ...build controls...; ApplyTheme(); Theme.StyleAccentButton(ok); nav.Select(0); }
    //     }
    //
    // NOTE for drop-ins: subclassing Form still needs
    //     //!CompilerOption:AddRef:System.Windows.Forms.Primitives.dll
    // (the Form/Control base chain lives there and loads lazily). The theme itself needs nothing.
    public class ThemedForm : Form
    {
        public ThemedForm()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
        }

        // Call last in the derived constructor, before applying accents.
        protected void ApplyTheme() => Theme.Apply(this);
    }
}
