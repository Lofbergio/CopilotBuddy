# CustomClass (CombatRoutine)

Base class for creating combat routines.

**Namespace**: `Styx.Combat.CombatRoutine`  
**Full Name**: `CombatRoutine` (often called `CustomClass` in documentation)  
**Abstract Class**

## Overview

`CombatRoutine` is the base class you inherit from to create combat routines (custom classes) for CopilotBuddy. It provides:
- Combat behavior methods (Pull, Combat, Heal, Rest)
- Buff management hooks
- TreeSharp behavior tree integration
- Pulse/Update callbacks

---

## Required Implementation

### Name
```csharp
public abstract string Name { get; }
```
Your combat routine's name (displayed in UI).

**Example:**
```csharp
public override string Name => "Frost Mage WotLK";
```

### Class
```csharp
public abstract WoWClass Class { get; }
```
The WoW class this routine is for.

**Example:**
```csharp
public override WoWClass Class => WoWClass.Mage;
```

---

## Optional Overrides - Legacy Methods

These are called by the bot if you don't provide behavior trees.

### Rest
```csharp
public virtual void Rest()
```
Called when out of combat to rest (eat/drink).

### NeedRest
```csharp
public virtual bool NeedRest { get; }
```
Whether the routine needs to rest.

**Example:**
```csharp
public override bool NeedRest => ObjectManager.Me.ManaPercent < 30;
```

### PreCombatBuff
```csharp
public virtual void PreCombatBuff()
```
Called before combat to apply long-duration buffs.

### NeedPreCombatBuffs
```csharp
public virtual bool NeedPreCombatBuffs { get; }
```
Whether pre-combat buffs are needed.

### PullBuff
```csharp
public virtual void PullBuff()
```
Called before pulling to apply short-term buffs.

### NeedPullBuffs
```csharp
public virtual bool NeedPullBuffs { get; }
```
Whether pull buffs are needed.

### Pull
```csharp
public virtual void Pull()
```
Called to pull a target into combat.

**Example:**
```csharp
public override void Pull()
{
    if (ObjectManager.Me.CurrentTarget != null)
    {
        SpellManager.Cast("Frostbolt");
    }
}
```

### Combat
```csharp
public virtual void Combat()
```
Called during combat to perform combat rotation.

### NeedCombatBuffs
```csharp
public virtual bool NeedCombatBuffs { get; }
```
Whether combat buffs are needed (refreshing during fight).

### CombatBuff
```csharp
public virtual void CombatBuff()
```
Called during combat to refresh buffs.

### Heal
```csharp
public virtual void Heal()
```
Called to heal (for hybrid/healer classes).

### NeedHeal
```csharp
public virtual bool NeedHeal { get; }
```
Whether healing is needed.

---

## Behavior Tree Overrides (Recommended)

These are the modern way to implement combat routines using TreeSharp.

### CombatBehavior
```csharp
public virtual Composite CombatBehavior { get; }
```
TreeSharp behavior tree for combat rotation.

**Example:**
```csharp
public override Composite CombatBehavior
{
    get
    {
        return new PrioritySelector(
            // Interrupt
            new Decorator(ret => target.IsCasting && target.CanInterruptCurrentSpellCast,
                new Action(ret => SpellManager.Cast("Counterspell"))),
            
            // Cooldowns
            new Decorator(ret => target.HealthPercent > 80,
                new Action(ret => SpellManager.Cast("Icy Veins"))),
            
            // Rotation
            new Action(ret => SpellManager.Cast("Frostbolt"))
        );
    }
}
```

### RestBehavior
```csharp
public virtual Composite RestBehavior { get; }
```
Behavior tree for resting.

### PreCombatBuffBehavior
```csharp
public virtual Composite PreCombatBuffBehavior { get; }
```
Behavior tree for pre-combat buffs.

### PullBuffBehavior
```csharp
public virtual Composite PullBuffBehavior { get; }
```
Behavior tree for pull buffs.

### PullBehavior
```csharp
public virtual Composite PullBehavior { get; }
```
Behavior tree for pulling.

### CombatBuffBehavior
```csharp
public virtual Composite CombatBuffBehavior { get; }
```
Behavior tree for combat buffs.

### HealBehavior
```csharp
public virtual Composite HealBehavior { get; }
```
Behavior tree for healing.

### MoveToTargetBehavior
```csharp
public virtual Composite MoveToTargetBehavior { get; }
```
Behavior tree for moving to target.

---

## Lifecycle Methods

### Initialize
```csharp
public virtual void Initialize()
```
Called when the routine is loaded.

**Example:**
```csharp
public override void Initialize()
{
    Logger.Write($"Loaded {Name} combat routine");
}
```

### ShutDown
```csharp
public virtual void ShutDown()
```
Called when the routine is unloaded.

### Pulse
```csharp
public virtual void Pulse()
```
Called every bot tick (multiple times per second).

**Use for:**
- Updating internal state
- Checking cooldowns
- Cache invalidation

---

## Settings Button

### WantButton
```csharp
public virtual bool WantButton { get; }
```
Whether to show a settings button.

### ButtonText
```csharp
public string ButtonText { get; }
```
Text for the settings button (default: "Settings").

### OnButtonPress
```csharp
public virtual void OnButtonPress()
```
Called when settings button is pressed.

**Example:**
```csharp
public override bool WantButton => true;

public override void OnButtonPress()
{
    var settingsForm = new MySettingsForm();
    settingsForm.ShowDialog();
}
```

---

## Other Properties

### PullDistance
```csharp
public virtual double? PullDistance { get; }
```
Override pull distance (null = use default).

---

## Complete Example

```csharp
using System;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using TreeSharp;

namespace MyRoutines
{
    public class FrostMage : CombatRoutine
    {
        public override string Name => "Frost Mage WotLK";
        public override WoWClass Class => WoWClass.Mage;

        public override void Initialize()
        {
            Logger.Write($"Loaded {Name}");
        }

        public override void ShutDown()
        {
            Logger.Write($"Unloaded {Name}");
        }

        public override Composite CombatBehavior
        {
            get
            {
                return new PrioritySelector(
                    // Safety: Ice Block if low
                    new Decorator(ret => Me.HealthPercent < 20 && !Me.HasAura("Ice Block"),
                        new Action(ret => SpellManager.Cast("Ice Block"))),
                    
                    // Interrupt
                    new Decorator(ret => Target != null && 
                                         Target.IsCasting && 
                                         Target.CanInterruptCurrentSpellCast,
                        new Action(ret => SpellManager.Cast("Counterspell"))),
                    
                    // Cooldowns
                    new Decorator(ret => Target.HealthPercent > 80,
                        new PrioritySelector(
                            new Action(ret => SpellManager.Cast("Icy Veins")),
                            new Action(ret => SpellManager.Cast("Mirror Image"))
                        )),
                    
                    // Fingers of Frost proc
                    new Decorator(ret => Me.HasAura("Fingers of Frost"),
                        new Action(ret => SpellManager.Cast("Ice Lance"))),
                    
                    // Brain Freeze proc
                    new Decorator(ret => Me.HasAura("Brain Freeze"),
                        new Action(ret => SpellManager.Cast("Fireball"))),
                    
                    // Filler
                    new Action(ret => SpellManager.Cast("Frostbolt"))
                );
            }
        }

        public override Composite PreCombatBuffBehavior
        {
            get
            {
                return new PrioritySelector(
                    new Decorator(ret => !Me.HasAura("Frost Armor"),
                        new Action(ret => SpellManager.Cast("Frost Armor"))),
                    
                    new Decorator(ret => !Me.HasAura("Arcane Intellect"),
                        new Action(ret => SpellManager.Cast("Arcane Intellect")))
                );
            }
        }

        public override Composite RestBehavior
        {
            get
            {
                return new PrioritySelector(
                    // Conjure food/water
                    new Decorator(ret => Me.ManaPercent < 50 && !Me.HasAura("Drink"),
                        new Action(ret => SpellManager.Cast("Conjure Water"))),
                    
                    // Use food/water
                    new Decorator(ret => Me.ManaPercent < 50,
                        new Action(ret => {
                            // Use mana food item
                            return RunStatus.Success;
                        }))
                );
            }
        }

        private LocalPlayer Me => ObjectManager.Me;
        private WoWUnit Target => ObjectManager.Me.CurrentTarget;
    }
}
```

---

## WotLK Notes

### ✅ Features Available
- All base combat routine methods
- Behavior tree system (TreeSharp)
- Pulse/Initialize/ShutDown hooks
- Settings button support

### ⚠️ Best Practices
- Use **Behavior Trees** instead of legacy methods
- Always check `IsValid` before accessing objects
- Cache expensive calculations in `Pulse()`
- Don't spam spell casts - check cooldowns

---

## See Also

- [TreeSharp Composites](#) - Behavior tree nodes
- [SpellManager](#) - Spell casting
- [ObjectManager](../styx/objectmanager.md) - Object access
- [Creating Combat Routines Guide](../../guides/creating-routines.md)
