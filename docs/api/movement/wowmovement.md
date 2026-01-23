# WoWMovement Class

!!! info "WotLK 3.3.5a Support"
    ✅ **Full support** - Low-level movement control. Offsets: CTM_Function=0x727F90, StopMovement=0x72D320, ClickToMove_Base=0xCA11B8

The `WoWMovement` class provides low-level control over player movement, facing, and click-to-move functionality.

## Namespace

```csharp
using Styx.WoWInternals;
```

## Overview

`WoWMovement` is a static class for direct movement control. For high-level pathfinding, use [Navigator](../pathing/navigator.md) instead.

!!! warning "Low-Level API"
    This class directly controls WoW's movement system. Use [Navigator.MoveTo()](../pathing/navigator.md) for safe pathfinding.

---

## Properties

### `bool IsMoving`

Returns `true` if the player is currently moving.

```csharp
if (WoWMovement.IsMoving)
{
    Console.WriteLine("Player is moving");
}
```

### `WoWPoint ActiveMover`

Gets the current player location.

```csharp
WoWPoint currentPos = WoWMovement.ActiveMover;
Console.WriteLine($"Position: {currentPos}");
```

### `ClickToMoveInfoStruct ClickToMoveInfo`

Gets the current click-to-move state (velocity, destination, type, etc.).

```csharp
var ctmInfo = WoWMovement.ClickToMoveInfo;
Console.WriteLine($"CTM Type: {ctmInfo.Type}");
Console.WriteLine($"Velocity: {ctmInfo.Velocity}");
Console.WriteLine($"Destination: {ctmInfo.ClickPos}");
Console.WriteLine($"Is Click Moving: {ctmInfo.IsClickMoving}");
```

### `bool IsFacing`

Returns `true` if the player is currently facing a target.

```csharp
if (WoWMovement.IsFacing)
{
    Console.WriteLine("Player is facing a target");
}
```

### `InputControl ActiveInputControl`

Gets the current input control state (time, movement control flags).

```csharp
var input = WoWMovement.ActiveInputControl;
Console.WriteLine($"Movement Control: {input.MovementControl}");
```

---

## Basic Movement

### `void Move(MovementDirection direction)`

Starts movement in the specified direction.

```csharp
// Move forward
WoWMovement.Move(MovementDirection.Forward);

// Strafe left
WoWMovement.Move(MovementDirection.StrafeLeft);

// Move backward
WoWMovement.Move(MovementDirection.Backwards);
```

### `void Move(MovementDirection direction, bool start)`

Starts or stops movement in a direction.

```csharp
// Start moving forward
WoWMovement.Move(MovementDirection.Forward, start: true);

// Stop moving forward
WoWMovement.Move(MovementDirection.Forward, start: false);
```

### `void Move(MovementDirection direction, TimeSpan duration)`

Moves in a direction for a specific duration.

```csharp
// Move forward for 2 seconds
WoWMovement.Move(MovementDirection.Forward, TimeSpan.FromSeconds(2));

// Strafe right for 500ms
WoWMovement.Move(MovementDirection.StrafeRight, TimeSpan.FromMilliseconds(500));
```

---

## Stopping Movement

### `void MoveStop()`

Stops all movement.

```csharp
// Stop all movement
WoWMovement.MoveStop();
```

### `void MoveStop(MovementDirection direction)`

Stops movement in a specific direction.

```csharp
// Stop forward movement only
WoWMovement.MoveStop(MovementDirection.Forward);

// Stop strafing
WoWMovement.MoveStop(MovementDirection.StrafeMask);
```

### `void StopMovement(MovementDirection direction)`

Stops movement in specific directions (alias for `MoveStop`).

```csharp
// Stop all allowed directions
WoWMovement.StopMovement(MovementDirection.AllAllowed);
```

---

## Facing

### `void Face(WoWPoint target)`

Faces a specific location.

```csharp
WoWPoint destination = new WoWPoint(1234, 5678, 100);
WoWMovement.Face(destination);
```

### `void Face(WoWUnit target)`

Faces a specific unit.

```csharp
WoWUnit enemy = StyxWoW.Me.CurrentTarget;
WoWMovement.Face(enemy);
```

### `void Face(float angle)`

Faces a specific angle (radians).

```csharp
// Face north (0 radians)
WoWMovement.Face(0);

// Face east (π/2 radians)
WoWMovement.Face((float)Math.PI / 2);
```

### `void StopFace()`

Stops facing.

```csharp
WoWMovement.StopFace();
```

---

## Click-to-Move

### `void ClickToMove(WoWPoint destination)`

Moves to a destination using click-to-move.

```csharp
WoWPoint destination = new WoWPoint(1234, 5678, 100);
WoWMovement.ClickToMove(destination);
```

### `void ClickToMove(WoWPoint destination, ulong interactGuid)`

Moves to a destination and interacts with an object.

```csharp
WoWUnit npc = FindNearestNPC();
WoWMovement.ClickToMove(npc.Location, npc.Guid);
```

---

## Jump and Ascend

### `void Jump()`

Makes the player jump.

```csharp
// Jump!
WoWMovement.Jump();
```

### `void Ascend()`

Makes the player ascend (flying mounts).

```csharp
if (StyxWoW.Me.Mounted && StyxWoW.Me.IsFlying)
{
    WoWMovement.Ascend();
}
```

### `void Descend()`

Makes the player descend (flying mounts).

```csharp
if (StyxWoW.Me.Mounted && StyxWoW.Me.IsFlying)
{
    WoWMovement.Descend();
}
```

---

## Constant Facing

### `void ConstantFace(float angle)`

Continuously faces an angle.

```csharp
// Keep facing north
WoWMovement.ConstantFace(0);
```

### `void ConstantFace(ulong guid)`

Continuously faces an object by GUID.

```csharp
WoWUnit boss = FindBoss();
WoWMovement.ConstantFace(boss.Guid);
```

### `void ConstantFaceStop()`

Stops constant facing.

```csharp
WoWMovement.ConstantFaceStop();
```

---

## Utility Methods

### `WoWPoint CalculatePointFrom(WoWPoint target, float distance)`

Calculates a point at a specific distance from a target.

```csharp
WoWUnit boss = FindBoss();

// Get a point 10 yards behind the boss (from player's perspective)
WoWPoint retreatPoint = WoWMovement.CalculatePointFrom(boss.Location, -10f);

// Move to retreat point
Navigator.MoveTo(retreatPoint);
```

### `float GetHeadingDiff(float heading1, float heading2)`

Calculates the difference between two headings (in radians).

```csharp
float playerHeading = StyxWoW.Me.Rotation;
float targetHeading = 1.5f;

float diff = WoWMovement.GetHeadingDiff(playerHeading, targetHeading);
Console.WriteLine($"Heading difference: {diff} radians");
```

### `void Navigate(WoWPoint destination)`

Navigates to a destination using Navigator (wrapper).

```csharp
// Equivalent to Navigator.MoveTo()
WoWMovement.Navigate(destination);
```

### `void Navigate(WoWPoint destination, float precision)`

Navigates with custom precision (wrapper).

```csharp
// Navigate with 0.5 yard precision
WoWMovement.Navigate(destination, precision: 0.5f);
```

---

## MovementDirection Enum

```csharp
[Flags]
public enum MovementDirection : uint
{
    None = 0,
    RMouse = 1,
    LMouse = 2,
    Forward = 16,              // 0x00000010
    Backwards = 32,            // 0x00000020
    StrafeLeft = 64,           // 0x00000040
    StrafeRight = 128,         // 0x00000080
    TurnLeft = 256,            // 0x00000100
    TurnRight = 512,           // 0x00000200
    PitchUp = 1024,            // 0x00000400
    PitchDown = 2048,          // 0x00000800
    AutoRun = 4096,            // 0x00001000
    JumpAscend = 8192,         // 0x00002000
    Descend = 16384,           // 0x00004000
    ClickToMove = 4194304,     // 0x00400000
    IsCTMing = 2097152,        // 0x00200000
    
    // Composite masks
    StrafeMask = StrafeMovement | StrafeRight | StrafeLeft,
    TurnMask = TurnMovement | TurnRight | TurnLeft,
    MoveMask = ForwardBackMovement | AutoRun | Backwards | Forward,
    All = MoveMask | TurnMask | StrafeMask,
    AllAllowed = Descend | JumpAscend | AutoRun | TurnRight | TurnLeft | 
                 StrafeRight | StrafeLeft | Backwards | Forward
}
```

---

## Complete Examples

### Example 1: Simple Strafing

```csharp
public void StrafeAroundTarget(WoWUnit target)
{
    // Face target
    WoWMovement.Face(target);
    
    // Strafe right for 1 second
    WoWMovement.Move(MovementDirection.StrafeRight, TimeSpan.FromSeconds(1));
    
    // Face target again
    WoWMovement.Face(target);
    
    // Strafe left for 1 second
    WoWMovement.Move(MovementDirection.StrafeLeft, TimeSpan.FromSeconds(1));
}
```

### Example 2: Kiting (Move and Cast)

```csharp
public class KitingHelper
{
    public void KiteEnemy(WoWUnit enemy)
    {
        var me = StyxWoW.Me;
        
        // If enemy is too close, back up
        if (enemy.Distance < 10)
        {
            // Face enemy
            WoWMovement.Face(enemy);
            
            // Move backward while casting
            WoWMovement.Move(MovementDirection.Backwards, start: true);
            
            // Cast spell
            if (SpellManager.CanCast("Frostbolt"))
            {
                SpellManager.Cast("Frostbolt");
                System.Threading.Thread.Sleep(2000); // Cast time
            }
            
            // Stop moving
            WoWMovement.MoveStop();
        }
        else
        {
            // Enemy far enough, just cast
            WoWMovement.Face(enemy);
            SpellManager.Cast("Frostbolt");
        }
    }
}
```

### Example 3: Jump Over Obstacle

```csharp
public void JumpObstacle()
{
    // Start moving forward
    WoWMovement.Move(MovementDirection.Forward, start: true);
    
    // Wait a bit
    System.Threading.Thread.Sleep(200);
    
    // Jump
    WoWMovement.Jump();
    
    // Continue forward for 1 second
    System.Threading.Thread.Sleep(1000);
    
    // Stop
    WoWMovement.MoveStop();
}
```

### Example 4: Circle Strafe Boss

```csharp
public class CircleStrafing
{
    private bool _clockwise = true;
    
    public void CircleStrafeBoss(WoWUnit boss)
    {
        // Always face boss
        WoWMovement.Face(boss);
        
        // Strafe direction
        MovementDirection strafeDir = _clockwise ? 
            MovementDirection.StrafeRight : 
            MovementDirection.StrafeLeft;
        
        // Start strafing
        WoWMovement.Move(strafeDir, start: true);
        
        // Switch direction every 3 seconds
        System.Threading.Thread.Sleep(3000);
        _clockwise = !_clockwise;
        
        // Stop current strafe
        WoWMovement.MoveStop(MovementDirection.StrafeMask);
    }
}
```

### Example 5: Melee Range Positioning

```csharp
public void MaintainMeleeRange(WoWUnit target)
{
    var me = StyxWoW.Me;
    float optimalRange = 4.5f; // Melee range
    
    // Face target
    WoWMovement.Face(target);
    
    if (target.Distance > optimalRange + 1)
    {
        // Too far, move forward
        WoWMovement.Move(MovementDirection.Forward, start: true);
    }
    else if (target.Distance < optimalRange - 1)
    {
        // Too close, move backward
        WoWMovement.Move(MovementDirection.Backwards, start: true);
    }
    else
    {
        // In range, stop
        WoWMovement.MoveStop();
    }
}
```

### Example 6: Flying Mount Control

```csharp
public class FlyingControl
{
    public void FlyToHeight(float targetZ)
    {
        var me = StyxWoW.Me;
        
        if (!me.Mounted || !me.IsFlying)
        {
            Console.WriteLine("Not flying");
            return;
        }
        
        float currentZ = me.Location.Z;
        
        if (currentZ < targetZ - 5)
        {
            // Need to ascend
            Console.WriteLine("Ascending...");
            WoWMovement.Move(MovementDirection.JumpAscend, start: true);
        }
        else if (currentZ > targetZ + 5)
        {
            // Need to descend
            Console.WriteLine("Descending...");
            WoWMovement.Descend();
        }
        else
        {
            // At target height
            Console.WriteLine("At target height");
            WoWMovement.MoveStop();
        }
    }
}
```

---

## ClickToMoveInfoStruct

```csharp
public struct ClickToMoveInfoStruct
{
    public float Velocity;           // Current movement velocity
    public float InteractDistSqrd;   // Interact distance squared
    public float InteractDist;       // Interact distance
    public float FaceAngle;          // Face angle (radians)
    public uint CurrentTime;         // Current time
    public ClickToMoveType Type;     // CTM type
    public ulong InteractGuid;       // Interact GUID
    public WoWPoint CurrentPos;      // Current position
    public WoWPoint ClickPos;        // Destination position
    
    public bool IsClickMoving;       // True if actively click-moving
    public bool IsUsing;             // True if CTM is active
}
```

### Example Usage

```csharp
var ctm = WoWMovement.ClickToMoveInfo;

if (ctm.IsClickMoving)
{
    Console.WriteLine($"Moving to {ctm.ClickPos}");
    Console.WriteLine($"Distance remaining: {ctm.CurrentPos.Distance(ctm.ClickPos):F1}y");
    Console.WriteLine($"Velocity: {ctm.Velocity:F2}");
}
```

---

## ClickToMoveType Enum

```csharp
public enum ClickToMoveType
{
    LeftClick = 1,
    Face = 2,
    StopThrowsException = 3,
    Move = 4,
    NpcInteract = 5,
    Loot = 6,
    ObjInteract = 7,
    FaceOther = 8,
    Skin = 9,
    AttackPosition = 10,
    AttackGuid = 11,
    ConstantFace = 12,
    None = 13
}
```

---

## Best Practices

### 1. Use Navigator for Pathfinding

```csharp
// GOOD - use Navigator for long-distance movement
Navigator.MoveTo(destination);

// BAD - WoWMovement has no pathfinding
WoWMovement.Move(MovementDirection.Forward); // Will hit obstacles
```

### 2. Always Stop Movement Before Actions

```csharp
// GOOD
WoWMovement.MoveStop();
SpellManager.Cast("Frostbolt");

// BAD - may interrupt cast
SpellManager.Cast("Frostbolt"); // Still moving!
```

### 3. Face Target Before Casting

```csharp
// GOOD
WoWMovement.Face(target);
SpellManager.Cast("Fireball");

// BAD - may get "Target not in front" error
SpellManager.Cast("Fireball");
```

### 4. Use TimeSpan for Timed Movement

```csharp
// GOOD - automatic stop
WoWMovement.Move(MovementDirection.Forward, TimeSpan.FromSeconds(1));

// BAD - manual sleep/stop
WoWMovement.Move(MovementDirection.Forward, start: true);
System.Threading.Thread.Sleep(1000);
WoWMovement.MoveStop();
```

---

## WotLK 3.3.5a Offsets

```csharp
// Click to move function address
private const uint CTM_Function = 0x00727F90;  // 7503760 decimal

// Stop movement function
private const uint StopMovement_Function = 0x0072D320;  // 7524128 decimal

// Click to move base address
private const uint ClickToMove_Base = 0xCA11B8;  // 13242808 decimal

// Active input control pointer
private const uint ActiveInputControl_Ptr = 0xC24D54;  // 12732756 decimal

// Player rotation offset
private const uint RotationOffset = 0x7A8;
```

---

## Troubleshooting

### Movement Not Working

**Problem:** `Move()` doesn't move the player.

**Solution:** Ensure bot has control:

```csharp
// Check if player is controlled by bot
if (StyxWoW.Me.IsValid && !StyxWoW.Me.Dead)
{
    WoWMovement.Move(MovementDirection.Forward);
}
```

### Facing Not Accurate

**Problem:** `Face()` doesn't face target precisely.

**Solution:** Check angle is in radians:

```csharp
// Angle in radians (0 to 2π)
float angle = (float)Math.Atan2(deltaY, deltaX);
WoWMovement.Face(angle);
```

---

## See Also

- [Navigator](../pathing/navigator.md) - High-level pathfinding (recommended)
- [LocalPlayer](../wowobjects/localplayer.md) - Player state (Location, IsMoving, Rotation)
- [WoWUnit](../wowobjects/wowunit.md) - Unit locations and facing
- [SpellManager](../combat/spellmanager.md) - Spell casting (requires facing)
