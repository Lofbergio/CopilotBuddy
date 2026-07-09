# Styx.UI — the shared WinForms design system

An ElvUI-inspired dark skin + themed controls for **every WinForms surface in the app**: settings dialogs for
routines, plugins, drop-in botbases, and the built-in botbases.

It lives in `CopilotBuddy.dll`, which **every consumer already references** (SourceCompiler adds this assembly
automatically). So there is nothing to install:

```csharp
using Styx.UI;
```

**There is exactly one design system. Never hand-roll a palette, never copy one.** Need a colour? Add it to
`Theme`, not to your form. (`grep "static class Theme"` across the tree must return one hit — `Theme.cs`.)

**Scope:** WinForms only. CB's main window is WPF/MahApps and is deliberately untouched.

---

## Quick start

```csharp
using Styx.UI;

public class MySettings : ThemedForm            // dark surface + font defaults
{
    public MySettings()
    {
        Text = "My Settings";

        var s = new Stack(this, width: 420);    // vertical layout cursor

        s.Section("Combat");                    // gold header + divider
        var interrupt = s.Check("Interrupt enemy casts", true, "Kick anything interruptible.");

        var healAt = new ThemedNumeric { Minimum = 1, Maximum = 100, Value = 55 };
        s.Row("Heal at (% HP)", healAt, "Self-heal below this.");

        s.Hint("Tip text, muted and smaller.");

        var ok = new Button { Text = "Save && Close", DialogResult = DialogResult.OK };
        ok.SetBounds(s.Width - 120, s.Y, 100, 28);
        Controls.Add(ok);

        ClientSize = new Size(s.Width, s.Y + 44);

        ApplyTheme();                           // 1) generic theme, ONCE, after all controls exist
        Theme.StyleAccentButton(ok);            // 2) accents AFTER
    }
}
```

### The ordering rule (this gets people every time)

`Theme.Apply(root)` themes **generically**, so it must run **once, after every control is added**, and anything
that is an *accent* must be applied **after** it — otherwise `Apply` overwrites it.

```csharp
BuildControls();
ApplyTheme();                       // ThemedForm helper == Theme.Apply(this)
Theme.StyleAccentButton(okButton);  // accents AFTER
nav.Select(0);                      // selection state AFTER
```

---

## Use the themed controls

The stock controls are genuinely broken on a dark surface and **cannot be fixed by recolouring** — that's the
whole reason these exist.

| Use | Instead of | Why the stock control can't be themed |
| --- | --- | --- |
| `ThemedCheckBox` | `CheckBox` | With `FlatStyle.Flat`, WinForms paints the **tick** in a system near-black regardless of `ForeColor` → invisible on dark; clicks look like they did nothing. The glyph must be owner-drawn. |
| `ThemedNumeric` | `NumericUpDown` | Its spin arrows live in an internal `UpDownButtons` control that can't be overridden or recoloured. Replaced by a `[− │ value │ +]` stepper (hold-repeat, wheel, `Increment`, clamps; glyphs dim at the limits). |
| `Card` | `GroupBox` | The etched border is OS-drawn. `Card` is a 1px outer `Panel` padding an inner surface `Panel` — no custom painting. **Add controls to `.Content`.** |
| `NavBar` | `TabControl` | Owner-draw only paints the tab *rects*; the strip background and page frame stay system-white. `NavBar` is flat buttons + a gold underline toggling page `Visible`. |
| `Stack` | hand-rolled `_y += 32` | Vertical layout cursor: `Section` / `Row` / `Check` / `Block` / `Hint` / `Divider` / `Space`, tooltips wired in. |
| `ThemedForm` | `Form` | Dark surface + font defaults. Not required — `Theme.Apply(this)` works on a plain `Form` (use that when you need a resizable window; `ThemedForm` fixes the border style). |
| `Theme.Tip(c, text)` | a per-form `ToolTip` | One shared `ToolTip` (350ms in, 20s out). |

A **glyph-only toggle** for list rows is just `new ThemedCheckBox { Text = "", BackColor = Color.Transparent }` —
it paints its parent's colour, so it sits correctly on a hover-tinted row.

`Theme.Apply` also skins what it can of the stock set (`Button`, `Label`, `TextBox`, `ComboBox`, `ListBox`,
`NumericUpDown`, and `PropertyGrid`, which ignores `BackColor` and exposes its own colour surface). It
**deliberately never flat-styles a stock `CheckBox`** — that's what hides the tick. So a legacy form can simply
call `Theme.Apply(this)` and stay usable without swapping its controls.

---

## Escape hatches

- **`IThemeExempt`** — marker interface. `Theme.Apply` skips the control **and its whole subtree**. Implement it
  on any control that paints itself (`ThemedCheckBox`, `ThemedNumeric`, `NavBar` already do; so should yours —
  e.g. VibeTalents' `TalentCell` / `TalentMinimap`).
- **`Theme.KeepTag`** — put it in a `Label.Tag` and `Apply` won't touch its colour **or** font.
  `Theme.AccentLabel(text, loc, colour, font)` builds one for you.
  *A label given a non-default colour keeps it automatically* — the tag is only needed to preserve a custom
  **font** (a 15pt title, a small hint).

## Palette

`Bg` `Panel` `PanelDark` · `Text` `Dim` · `Gold` `GoldBright` · `Green` `GreenBright` · `Danger` ·
`Border` `Hairline` · `HoverBg` `DownBg` `RowHover`
Fonts: `UI` `UIBold` `Small` `Section` `Title`. Plus `Theme.ClassColor(WoWClass)`.

`Hairline` is **pure black** — ElvUI's signature 1px border. Use it for *structural boxes* (card frames, input
wells, frameless form frames). `Border` (soft grey) is for *dividers and interactive outlines*, where a black
line would erase the affordance on dark.

---

## Build loop

`Styx.UI` is source in `Styx/UI/`, compiled into `CopilotBuddy.dll`.

- **Changing the library** → rebuild `CopilotBuddy.csproj`, redeploy `CopilotBuddy.dll` to the runtime, **restart
  CB** (kills the user's session — ask first).
- **Consuming it** → nothing special. A plugin recompiles from the Plugins window; a routine/drop-in bot needs a
  normal CB relaunch.

### Drop-in compile traps

SourceCompiler builds its reference set from the assemblies **loaded at compile time**, and these load lazily:

- Subclassing `Form` needs `//!CompilerOption:AddRef:System.Windows.Forms.Primitives.dll`.
- Naming `Graphics` yourself needs `AddRef:System.Private.Windows.GdiPlus.dll` — **but don't.** Keep painting
  inside this library and you never name it. (SourceCompiler now force-loads GdiPlus too, so this can no longer
  kill a routine the way it once did: a **routine** compiles early at startup and missed it →
  `Loaded 0 combat routine(s)`, while a **plugin**, compiled later, was fine. A genuinely confusing asymmetry.)
- ⚠ An out-of-band check build (GVCheck / VTCheck / GRCheck) sets `UseWindowsForms`, which auto-references both —
  so it can **never** catch a missing `AddRef`. **"Compiles clean" ≠ "loads in CB."** Only a real relaunch proves it.

### Gotchas when editing this library

- `Timer` is ambiguous in this assembly (`System.Threading` vs `System.Windows.Forms`) — fully qualify it.
- Public properties on a `Control` subclass need `[DesignerSerializationVisibility]`, or the **WFO1000** analyzer
  fails the build (these controls are hand-authored and never see a designer).
- There are no tests. Verification is: build → deploy → restart CB → confirm `Loaded N combat routine(s)` in
  `Logs\` and eyeball the dialog.

---

## Consumers

`Routines/GoodVibes/GVSettingsForm.cs` (reference implementation: title band, `NavBar`, `Card`s, accent Save) ·
`Plugins/VibeTalents/Forms/*` (talent-tree controls are `IThemeExempt`) · `Plugins/GuildRecruiter/ConfigForm.cs`
(frameless chrome, `Stack`-style rows, glyph-only toggles) · `Bots/Vibes/VibeParty` · `Bots/Vibes/VibeGrinder`
(`PropertyGrid`) · `Bots/Vibes/VibeQuester` (legacy, stock controls + `Apply`).

Third-party drop-ins (AutoEquip2, RareKiller, LazyRaider, Templar…) are shipped originals — leave them alone.

---

## Design record

### Consolidation (2026-07-09)

The same ElvUI palette had been copy-pasted **four** times — `Plugins/VibeTalents/Forms/Theme.cs`,
`Bots/Vibes/VibeParty/Forms/Theme.cs`, `Routines/GoodVibes/GVTheme.cs`, and GuildRecruiter's private constants —
each with a *different* `Apply()`. VibeParty's own header said why: *"VibeParty can't reference [VibeTalents'
Theme] … this is a trimmed local copy kept visually in sync."* All four are deleted.

Each copy had one idea the others lacked; the survivor keeps all of them:

| Idea | Came from | Why it won |
| --- | --- | --- |
| Non-default label colours survive `Apply` | VibeParty | Gold section headers work with no tag. VibeTalents' copy flattened every label, silently losing its own gold headers. |
| **Never flat-style a stock `CheckBox`** | VibeParty (learned live) | On dark the tick goes near-black → *"clicks looked like they did nothing."* GoodVibes independently hit the same bug. |
| Owner-drawn glyphs + `Card` / `NavBar` | GoodVibes | The actual fix rather than a workaround. |
| `IThemeExempt` | generalised from VibeTalents' hardcoded `if (c is TalentCell …) continue;` | Any custom-painted control can opt out. |
| `Stack`, `Theme.Tip`, `Danger`, `RowHover`, glyph-only toggle | GuildRecruiter | It had a layout system and hover help; nobody else did. Its `ToggleBox` *is* the glyph-only `ThemedCheckBox`. |

Four independent workarounds for one checkbox-glyph bug is the best argument this library needed to exist.

### What the REAL ElvUI does

Checked against the installed addon (`3.3.5a AddOns/ElvUI/Settings/Profile.lua`) rather than trusting the label
"ElvUI-inspired":

```lua
bordercolor       = {r = 0,    g = 0,    b = 0}                 -- pure BLACK
backdropcolor     = {r = 0.1,  g = 0.1,  b = 0.1}               -- #1A1A1A
backdropfadecolor = {r = 0.06, g = 0.06, b = 0.06, a = 0.8}     -- #0F0F0F inset
valuecolor        = {r = 0.99, g = 0.48, b = 0.17}              -- #FD7A2B  (warm ORANGE)
pixelPerfect      = true
```

- **Our gold accent is directionally right** — ElvUI's default accent is a *warm orange*, so GuildRecruiter's cool
  blue was the outlier, not us. (`valuecolor` is user-configurable; gold is a legitimate variant.)
- **ElvUI's real signature is the pure-black 1px hairline** around every box, not the accent colour. Our `Border`
  was a light grey. Hence `Theme.Hairline`. Reverting is a one-constant change.
- We keep the slate `Bg`/`Panel` rather than ElvUI's neutral `#1A1A1A` — that's the Vibes identity, and black
  hairlines read well against it.
