# TreeSharp - Behavior Trees

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Behavior tree system for creating complex bot logic and combat routines.

TreeSharp is a lightweight behavior tree implementation used throughout CopilotBuddy for creating modular, reusable bot logic.

## Namespace

```csharp
using TreeSharp;
```

## What is a Behavior Tree?

A **behavior tree** is a hierarchical structure that controls bot decision-making. It's composed of **nodes** that execute in a specific order and return a status (`Success`, `Failure`, or `Running`).

### Advantages

✅ **Modular** - Behaviors are small, reusable components  
✅ **Readable** - Tree structure is easy to understand  
✅ **Composable** - Combine simple behaviors into complex logic  
✅ **Maintainable** - Easy to add/remove/modify behaviors

---

## RunStatus Enum

Every node returns one of three statuses:

```csharp
public enum RunStatus
{
    Success,    // Node completed successfully
    Failure,    // Node failed
    Running     // Node is still executing (async)
}
```

---

## Core Node Types

### 1. Action

**Executes a single action.** Returns `Success` or `Failure`.

```csharp
new Action(ctx => 
{
    SpellManager.Cast("Fireball");
    return RunStatus.Success;
})
```

### 2. Sequence

**Executes children in order. ALL must succeed.**

- If a child fails → entire sequence fails
- If all succeed → sequence succeeds

```csharp
new Sequence(
    new Action(ctx => ApplyDoTs()),
    new Action(ctx => CastNuke()),
    new Action(ctx => MoveAway())
)
```

**Like a logical AND:** `DoTs AND Nuke AND Move`

### 3. PrioritySelector

**Executes children in order until ONE succeeds.**

- If a child succeeds → selector succeeds immediately
- If all fail → selector fails

```csharp
new PrioritySelector(
    new Action(ctx => UseHealthPotion()),   // Try first
    new Action(ctx => CastHeal()),          // Try if first fails
    new Action(ctx => Bandage())            // Try if both fail
)
```

**Like a logical OR:** `Potion OR Heal OR Bandage`

### 4. Decorator

**Wraps a child node with a condition.**

```csharp
new Decorator(
    ctx => StyxWoW.Me.HealthPercent < 30,  // Condition
    new Action(ctx => UseHealthPotion())   // Child (executes only if condition true)
)
```

---

## Complete Examples

### Example 1: Basic Combat Rotation (Frost Mage)

```csharp
using TreeSharp;
using Styx;
using Styx.Logic.Combat;

public class FrostMageCombat
{
    public static Composite CreateCombatBehavior()
    {
        return new PrioritySelector(
            // 1. Check for target
            new Decorator(
                ctx => !StyxWoW.Me.GotTarget || !StyxWoW.Me.CurrentTarget.IsAlive,
                new Action(ctx => RunStatus.Failure) // Exit combat
            ),
            
            // 2. Apply Frostbolt (no conditions)
            new Action(ctx => 
            {
                if (SpellManager.CanCast("Frostbolt"))
                {
                    SpellManager.Cast("Frostbolt");
                    return RunStatus.Success;
                }
                return RunStatus.Failure;
            })
        );
    }
}
```

### Example 2: Advanced Combat with Cooldowns

```csharp
public static Composite CreateAdvancedCombat()
{
    return new PrioritySelector(
        // 1. Self-preservation
        new Decorator(
            ctx => StyxWoW.Me.HealthPercent < 20,
            new PrioritySelector(
                new Action(ctx => UseItem("Healthstone")),
                new Action(ctx => SpellManager.Cast("Ice Block"))
            )
        ),
        
        // 2. Target validation
        new Decorator(
            ctx => !StyxWoW.Me.GotTarget || !StyxWoW.Me.CurrentTarget.IsAlive,
            new Action(ctx => RunStatus.Failure)
        ),
        
        // 3. Cooldowns (execute once when available)
        new Decorator(
            ctx => ShouldUseCooldowns(),
            new Sequence(
                new Action(ctx => SpellManager.Cast("Icy Veins")),
                new Action(ctx => SpellManager.Cast("Mirror Image")),
                new Action(ctx => UseItem("Talisman of Resurgence"))
            )
        ),
        
        // 4. Brain Freeze proc
        new Decorator(
            ctx => HasAura("Brain Freeze"),
            new Action(ctx => SpellManager.Cast("Frostfire Bolt"))
        ),
        
        // 5. Fingers of Frost proc (instant Ice Lance)
        new Decorator(
            ctx => HasAura("Fingers of Frost"),
            new Action(ctx => SpellManager.Cast("Ice Lance"))
        ),
        
        // 6. Apply Deep Freeze when target frozen
        new Decorator(
            ctx => TargetHasAura("Frozen") && SpellManager.CanCast("Deep Freeze"),
            new Action(ctx => SpellManager.Cast("Deep Freeze"))
        ),
        
        // 7. Maintain Water Elemental
        new Decorator(
            ctx => !StyxWoW.Me.GotAlivePet,
            new Action(ctx => SpellManager.Cast("Summon Water Elemental"))
        ),
        
        // 8. Default filler
        new Action(ctx => SpellManager.Cast("Frostbolt"))
    );
}
```

### Example 3: Healing (Holy Priest)

```csharp
public static Composite CreateHealingBehavior()
{
    return new PrioritySelector(
        // 1. Tank about to die (< 20%)
        new Decorator(
            ctx => GetTank().HealthPercent < 20,
            new Sequence(
                new Action(ctx => SpellManager.Cast("Guardian Spirit", GetTank())),
                new Action(ctx => SpellManager.Cast("Flash Heal", GetTank()))
            )
        ),
        
        // 2. Tank taking damage (< 60%)
        new Decorator(
            ctx => GetTank().HealthPercent < 60,
            new PrioritySelector(
                new Action(ctx => SpellManager.Cast("Penance", GetTank())),
                new Action(ctx => SpellManager.Cast("Greater Heal", GetTank()))
            )
        ),
        
        // 3. Party member low (< 40%)
        new Decorator(
            ctx => GetLowestPartyMember().HealthPercent < 40,
            new Action(ctx => 
            {
                var target = GetLowestPartyMember();
                SpellManager.Cast("Flash Heal", target);
                return RunStatus.Success;
            })
        ),
        
        // 4. Multiple injured (AoE heal)
        new Decorator(
            ctx => GetInjuredPartyMembers().Count >= 3,
            new Action(ctx => SpellManager.Cast("Circle of Healing"))
        ),
        
        // 5. Top up party (< 80%)
        new Decorator(
            ctx => GetLowestPartyMember().HealthPercent < 80,
            new Action(ctx => SpellManager.Cast("Heal", GetLowestPartyMember()))
        ),
        
        // 6. DPS if everyone healthy
        new Action(ctx => 
        {
            if (StyxWoW.Me.GotTarget)
                SpellManager.Cast("Smite");
            return RunStatus.Success;
        })
    );
}
```

### Example 4: Gathering (Herbalism)

```csharp
public static Composite CreateGatheringBehavior()
{
    return new PrioritySelector(
        // 1. Combat check
        new Decorator(
            ctx => StyxWoW.Me.Combat,
            new Action(ctx => 
            {
                Console.WriteLine("In combat, cannot gather");
                return RunStatus.Failure;
            })
        ),
        
        // 2. Find nearest herb
        new Sequence(
            new Action(ctx => 
            {
                var herb = FindNearestHerb();
                if (herb == null)
                    return RunStatus.Failure;
                
                Blackboard["CurrentHerb"] = herb;
                return RunStatus.Success;
            }),
            
            // 3. Check if reachable
            new Decorator(
                ctx => Navigator.CanNavigateFully(GetCurrentHerb().Location),
                new Sequence(
                    // 4. Move to herb
                    new Action(ctx => 
                    {
                        var herb = GetCurrentHerb();
                        if (herb.Distance > 5f)
                        {
                            Navigator.MoveTo(herb.Location, herb.Name);
                            return RunStatus.Running;
                        }
                        return RunStatus.Success;
                    }),
                    
                    // 5. Gather herb
                    new Action(ctx => 
                    {
                        var herb = GetCurrentHerb();
                        herb.Interact();
                        System.Threading.Thread.Sleep(3000); // Gather time
                        return RunStatus.Success;
                    })
                )
            )
        )
    );
}
```

### Example 5: Questing Logic

```csharp
public static Composite CreateQuestBehavior()
{
    return new PrioritySelector(
        // 1. Turn in completed quests
        new Decorator(
            ctx => HasCompletedQuests(),
            new Sequence(
                new Action(ctx => Navigator.MoveTo(GetQuestGiver().Location)),
                new Action(ctx => 
                {
                    GetQuestGiver().Interact();
                    CompleteQuest();
                    return RunStatus.Success;
                })
            )
        ),
        
        // 2. Active quest objectives
        new Decorator(
            ctx => HasActiveQuests(),
            new PrioritySelector(
                // Kill objectives
                new Decorator(
                    ctx => HasKillObjective(),
                    new Sequence(
                        new Action(ctx => FindAndTargetQuestMob()),
                        new Action(ctx => KillCurrentTarget())
                    )
                ),
                
                // Collect objectives
                new Decorator(
                    ctx => HasCollectObjective(),
                    new Sequence(
                        new Action(ctx => FindQuestItem()),
                        new Action(ctx => MoveToAndLoot())
                    )
                )
            )
        ),
        
        // 3. Accept new quests
        new Decorator(
            ctx => CanAcceptQuests(),
            new Sequence(
                new Action(ctx => Navigator.MoveTo(GetQuestGiver().Location)),
                new Action(ctx => 
                {
                    GetQuestGiver().Interact();
                    AcceptQuest();
                    return RunStatus.Success;
                })
            )
        ),
        
        // 4. Grind while waiting
        new Action(ctx => Grind())
    );
}
```

### Example 6: Interrupt System

```csharp
public static Composite CreateInterruptBehavior()
{
    return new PrioritySelector(
        // Counterspell (instant)
        new Decorator(
            ctx => TargetCasting() && SpellManager.CanCast("Counterspell"),
            new Action(ctx => 
            {
                Console.WriteLine($"Interrupting: {StyxWoW.Me.CurrentTarget.CastingSpell.Name}");
                SpellManager.Cast("Counterspell");
                return RunStatus.Success;
            })
        ),
        
        // Ice Lance for minor interrupt
        new Decorator(
            ctx => TargetCasting() && !SpellManager.CanCast("Counterspell"),
            new Action(ctx => SpellManager.Cast("Ice Lance"))
        )
    );
}

bool TargetCasting()
{
    var target = StyxWoW.Me.CurrentTarget;
    if (target == null) return false;
    
    return target.IsCasting || target.IsChanneling;
}
```

---

## Advanced Patterns

### Pattern 1: Blackboard (Shared State)

Use a dictionary to share data between nodes:

```csharp
private static Dictionary<string, object> Blackboard = new();

new Sequence(
    // Store target in blackboard
    new Action(ctx => 
    {
        var target = FindBestTarget();
        Blackboard["BestTarget"] = target;
        return target != null ? RunStatus.Success : RunStatus.Failure;
    }),
    
    // Use target from blackboard
    new Action(ctx => 
    {
        var target = Blackboard["BestTarget"] as WoWUnit;
        target.Target();
        return RunStatus.Success;
    })
)
```

### Pattern 2: Custom Decorator

```csharp
public class CooldownDecorator : Decorator
{
    private DateTime _lastRun = DateTime.MinValue;
    private TimeSpan _cooldown;
    
    public CooldownDecorator(TimeSpan cooldown, Composite child)
        : base(child)
    {
        _cooldown = cooldown;
    }
    
    protected override bool CanRun(object context)
    {
        if (DateTime.Now - _lastRun < _cooldown)
            return false;
        
        _lastRun = DateTime.Now;
        return true;
    }
}

// Usage: Execute at most once per 5 seconds
new CooldownDecorator(
    TimeSpan.FromSeconds(5),
    new Action(ctx => SpellManager.Cast("Evocation"))
)
```

### Pattern 3: Subtree Reuse

```csharp
// Define reusable subtrees
private static Composite CreateSurvivalBehavior()
{
    return new PrioritySelector(
        new Decorator(
            ctx => StyxWoW.Me.HealthPercent < 30,
            new Action(ctx => UseHealthstone())
        ),
        new Decorator(
            ctx => StyxWoW.Me.HealthPercent < 50,
            new Action(ctx => SpellManager.Cast("Ice Barrier"))
        )
    );
}

// Reuse in multiple places
public static Composite CreateMainBehavior()
{
    return new PrioritySelector(
        CreateSurvivalBehavior(),  // Reused subtree
        CreateCombatBehavior(),
        CreateRestBehavior()
    );
}
```

### Pattern 4: Dynamic Children

```csharp
private static Composite CreateDynamicRotation()
{
    var children = new List<Composite>();
    
    // Add defensive spells if talented
    if (HasTalent("Ice Barrier"))
    {
        children.Add(new Decorator(
            ctx => StyxWoW.Me.HealthPercent < 60,
            new Action(ctx => SpellManager.Cast("Ice Barrier"))
        ));
    }
    
    // Add offensive cooldowns
    children.Add(new Action(ctx => SpellManager.Cast("Icy Veins")));
    children.Add(new Action(ctx => SpellManager.Cast("Frostbolt")));
    
    return new PrioritySelector(children.ToArray());
}
```

---

## Comparison with Traditional Logic

### Traditional If/Else

```csharp
// Hard to read, deeply nested
if (Me.HealthPercent < 20)
{
    if (CanUseItem("Healthstone"))
    {
        UseItem("Healthstone");
    }
    else if (SpellManager.CanCast("Ice Block"))
    {
        SpellManager.Cast("Ice Block");
    }
}
else if (Me.GotTarget && Me.CurrentTarget.IsAlive)
{
    if (HasAura("Brain Freeze"))
    {
        SpellManager.Cast("Frostfire Bolt");
    }
    else
    {
        SpellManager.Cast("Frostbolt");
    }
}
```

### TreeSharp Behavior Tree

```csharp
// Clean, modular, composable
new PrioritySelector(
    new Decorator(
        ctx => Me.HealthPercent < 20,
        new PrioritySelector(
            new Action(ctx => UseItem("Healthstone")),
            new Action(ctx => SpellManager.Cast("Ice Block"))
        )
    ),
    new Decorator(
        ctx => HasAura("Brain Freeze"),
        new Action(ctx => SpellManager.Cast("Frostfire Bolt"))
    ),
    new Action(ctx => SpellManager.Cast("Frostbolt"))
)
```

---

## Best Practices

### 1. Keep Actions Small

```csharp
// GOOD - one responsibility
new Action(ctx => SpellManager.Cast("Fireball"))

// BAD - multiple responsibilities
new Action(ctx => 
{
    ApplyDoTs();
    CastNuke();
    MoveTo(safePosition);
    UseHealthstone();
    return RunStatus.Success;
})
```

### 2. Use Descriptive Names

```csharp
// GOOD
private static Composite CreateInterruptBehavior() { ... }
private static Composite CreateSurvivalBehavior() { ... }

// BAD
private static Composite Behavior1() { ... }
private static Composite DoStuff() { ... }
```

### 3. Fail Fast

```csharp
// GOOD - check preconditions early
new PrioritySelector(
    new Decorator(
        ctx => !Me.GotTarget,
        new Action(ctx => RunStatus.Failure) // Exit early
    ),
    // ... rest of logic
)
```

### 4. Return Correct Status

```csharp
// GOOD
new Action(ctx => 
{
    if (SpellManager.Cast("Fireball"))
        return RunStatus.Success;
    return RunStatus.Failure;
})

// BAD - always returns success
new Action(ctx => 
{
    SpellManager.Cast("Fireball");
    return RunStatus.Success; // Wrong if cast failed!
})
```

### 5. Use Sequence for "AND" Logic

```csharp
// GOOD - all steps must succeed
new Sequence(
    new Action(ctx => CheckMana()),
    new Action(ctx => CheckRange()),
    new Action(ctx => CastSpell())
)
```

### 6. Use PrioritySelector for "OR" Logic

```csharp
// GOOD - try options in priority order
new PrioritySelector(
    new Action(ctx => UseHealthPotion()),   // Try first
    new Action(ctx => CastHeal()),          // Fallback
    new Action(ctx => Bandage())            // Last resort
)
```

---

## Common Pitfalls

### ❌ Forgetting RunStatus.Running

```csharp
// WRONG - returns immediately
new Action(ctx => 
{
    Navigator.MoveTo(destination);
    return RunStatus.Success; // Doesn't wait for arrival!
})

// CORRECT - waits for completion
new Action(ctx => 
{
    if (AtDestination())
        return RunStatus.Success;
    
    Navigator.MoveTo(destination);
    return RunStatus.Running; // Still moving
})
```

### ❌ Infinite Loops

```csharp
// WRONG - sequence never completes
new Sequence(
    new Action(ctx => RunStatus.Running), // Always running!
    new Action(ctx => DoSomething())      // Never executed
)
```

### ❌ Decorator Without Child

```csharp
// WRONG - decorator needs exactly 1 child
new Decorator(ctx => Me.HealthPercent < 50)

// CORRECT
new Decorator(
    ctx => Me.HealthPercent < 50,
    new Action(ctx => UseHealthPotion())
)
```

---

## TreeSharp Classes Reference

| Class | Purpose | Returns Success When... |
|-------|---------|------------------------|
| `Action` | Execute single action | Action returns `Success` |
| `Sequence` | Execute all children in order | ALL children succeed |
| `PrioritySelector` | Execute until one succeeds | ONE child succeeds |
| `Selector` | Same as PrioritySelector | ONE child succeeds |
| `Decorator` | Conditional execution | Condition true AND child succeeds |
| `RandomSelector` | Random child selection | Selected child succeeds |
| `Wait` | Pause execution | Wait time elapsed |
| `AlwaysSucceed` | Wrapper that always succeeds | Child finishes (ignores status) |
| `AlwaysFail` | Wrapper that always fails | Child finishes (inverts success) |

---

## Integration with CustomClass

```csharp
using TreeSharp;
using Styx.Logic.Combat;

public class MyCustomClass : CustomClass
{
    public override Composite Combat
    {
        get
        {
            return new PrioritySelector(
                CreateSurvivalBehavior(),
                CreateCombatBehavior(),
                CreateBuffBehavior()
            );
        }
    }
    
    public override Composite Pull
    {
        get
        {
            return new Sequence(
                new Action(ctx => FindTarget()),
                new Action(ctx => PullTarget())
            );
        }
    }
    
    public override Composite Rest
    {
        get
        {
            return new Sequence(
                new Action(ctx => Eat()),
                new Action(ctx => Drink())
            );
        }
    }
}
```

---

## See Also

- [CustomClass](../combat/customclass.md) - Using TreeSharp in combat routines
- [SpellManager](../combat/spellmanager.md) - Casting spells in Action nodes
- [Navigator](../pathing/navigator.md) - Movement in behavior trees
- [ObjectManager](../styx/objectmanager.md) - Finding targets for behaviors
