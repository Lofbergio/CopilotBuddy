# WoWUnit Class - Complete Reference

Represents any unit in the game world (players, NPCs, creatures, pets).

**Namespace**: `Styx.WoWInternals.WoWObjects`  
**Inherits**: [WoWObject](wowobject.md)  
**Implements**: `ILootableObject`  
**Derived Classes**: [WoWPlayer](wowplayer.md), [LocalPlayer](localplayer.md)

!!! info "Source File"
    `Styx/WoWInternals/WoWObjects/WoWUnit.cs` - 1681 lines

## Overview

`WoWUnit` is the base class for all living entities in WoW. It provides over 200 properties and methods for:
- Health, power (mana/rage/energy), and resources
- Combat status and control effects (stun, root, silence)
- Movement and position
- Targeting and facing
- Auras (buffs/debuffs)
- NPC interactions (vendor, trainer, quest giver)
- Reaction and hostility
- Casting and channeling

---

## Properties by Category

### 🔹 Basic Information

#### Name
```csharp
public override string Name { get; }
```
The unit's name (e.g., "Hogger", "Arthas").

#### Level
```csharp
public int Level { get; }
```
The unit's level (1-80 in WotLK). Returns `-1` for skull level (boss).

#### Class
```csharp
public WoWClass Class { get; }
```
The unit's class (if player or pet).

**WotLK Classes:** Warrior, Paladin, Hunter, Rogue, Priest, DeathKnight, Shaman, Mage, Warlock, Druid

!!! warning "No Monk"
    Monk doesn't exist in WotLK 3.3.5a (added in MoP 5.0).

#### Race
```csharp
public WoWRace Race { get; }
```
The unit's race (if player).

**Races:** Human, Dwarf, NightElf, Gnome, Draenei (Alliance) | Orc, Undead, Tauren, Troll, BloodElf (Horde)

#### Gender
```csharp
public WoWGender Gender { get; }
```
The unit's gender: `Male` or `Female`.

#### PowerType
```csharp
public WoWPowerType PowerType { get; }
```
The unit's primary power type: `Mana`, `Rage`, `Energy`, `Focus`, `RunicPower`, `Happiness`, `Runes`.

---

### ❤️ Health & Resources

#### Health / CurrentHealth
```csharp
public int Health { get; }
public int CurrentHealth { get; }
```
Current health points.

#### MaxHealth / HealthMax
```csharp
public int MaxHealth { get; }
public int HealthMax { get; }
```
Maximum health points.

#### HealthPercent
```csharp
public double HealthPercent { get; }
```
Health percentage (0-100).

**Example:**
```csharp
if (unit.HealthPercent < 20)
{
    Logger.Write($"{unit.Name} is low - execute!");
}
```

#### CurrentPower
```csharp
public int CurrentPower { get; }
```
Current power of the unit's PowerType.

#### CurrentMana / CurrentRage / CurrentEnergy / CurrentFocus / CurrentRunicPower
```csharp
public int CurrentMana { get; }
public int CurrentRage { get; }
public int CurrentEnergy { get; }
public int CurrentFocus { get; }
public int CurrentRunicPower { get; }
```
Current power values for specific resource types.

!!! note "Rage & Runic Power"
    Returned divided by 10 (WoW internal representation).

#### GetCurrentPower / GetMaxPower
```csharp
public int GetCurrentPower(WoWPowerType power)
public int GetMaxPower(WoWPowerType power)
```
Get current/max power for any power type.

#### GetPowerPercent
```csharp
public double GetPowerPercent(WoWPowerType p)
```
Get power percentage for any power type.

#### ManaPercent / RagePercent / EnergyPercent
```csharp
public double ManaPercent { get; }
public double RagePercent { get; }
public double EnergyPercent { get; }
```
Power percentages (0-100).

#### HappinessPercent
```csharp
public double HappinessPercent { get; }
```
Pet happiness percentage (1-3 where 3 = 100%).

!!! info "WotLK Feature"
    Pet happiness exists in WotLK 3.3.5a, removed in Cataclysm.

---

### ⚔️ Combat Status

#### Combat
```csharp
public bool Combat { get; }
```
Whether the unit is in combat.

#### Dead / IsDead
```csharp
public bool Dead { get; }
public bool IsDead { get; }
```
Whether the unit is dead.

!!! note "Implementation"
    `Dead`: CurrentHealth == 0  
    `IsDead`: CurrentHealth == 0 OR has Dead dynamic flag (for non-players)

#### IsAlive
```csharp
public bool IsAlive { get; }
```
Whether the unit is alive (not dead).

#### Attackable
```csharp
public bool Attackable { get; }
```
Whether the unit can be attacked (not immune/friendly).

#### CanAttack
```csharp
public bool CanAttack { get; }
```
Whether the player can attack this unit.

#### Aggro
```csharp
public bool Aggro { get; }
```
Whether the unit is in aggro range.

#### IsTargetingMe
```csharp
public bool IsTargetingMe { get; }
```
Whether this unit is targeting the player.

#### IsTargetingMeOrPet
```csharp
public bool IsTargetingMeOrPet { get; }
```
Whether this unit is targeting the player or player's pet.

#### IsTargetingMyPartyMember
```csharp
public bool IsTargetingMyPartyMember { get; }
```
Whether this unit is targeting a party member.

#### IsTargetingMyRaidMember
```csharp
public bool IsTargetingMyRaidMember { get; }
```
Whether this unit is targeting a raid member.

---

### 🎯 Control Effects (CC)

#### Stunned
```csharp
public bool Stunned { get; }
```
Whether the unit is stunned.

#### Rooted
```csharp
public bool Rooted { get; }
```
Whether the unit is rooted (can't move).

#### Silenced
```csharp
public bool Silenced { get; }
```
Whether the unit is silenced (can't cast spells).

#### Dazed
```csharp
public bool Dazed { get; }
```
Whether the unit is dazed (slowed).

#### Disarmed
```csharp
public bool Disarmed { get; }
```
Whether the unit is disarmed (can't use weapon attacks).

#### Fleeing
```csharp
public bool Fleeing { get; }
```
Whether the unit is fleeing (feared).

#### Pacified
```csharp
public bool Pacified { get; }
```
Whether the unit is pacified (can't attack).

#### Possessed
```csharp
public bool Possessed { get; }
```
Whether the unit is mind-controlled.

#### FeignDeathed
```csharp
public bool FeignDeathed { get; }
```
Whether the unit is feigning death (Hunter ability).

---

### 🏃 Movement & Position

#### Location
```csharp
public override WoWPoint Location { get; }
```
The unit's 3D position (X, Y, Z).

#### X / Y / Z
```csharp
public override float X { get; }
public override float Y { get; }
public override float Z { get; }
```
Individual coordinates.

#### Rotation
```csharp
public override float Rotation { get; }
```
The unit's facing direction (radians).

#### IsMoving
```csharp
public bool IsMoving { get; }
```
Whether the unit is moving.

#### IsFalling
```csharp
public bool IsFalling { get; }
```
Whether the unit is falling.

#### IsFlying
```csharp
public bool IsFlying { get; }
```
Whether the unit is flying (flying mount or form).

#### IsOnTransport
```csharp
public bool IsOnTransport { get; }
```
Whether the unit is on a transport (boat, zeppelin).

#### Transport
```csharp
public WoWGameObject? Transport { get; }
```
The transport the unit is on (if any).

#### MovementInfo / WoWMovementInfo
```csharp
public WoWMovementInfo MovementInfo { get; }
public WoWMovementInfo WoWMovementInfo { get; }
```
Detailed movement information.

#### MovementFlags
```csharp
public uint MovementFlags { get; }
```
Raw movement flags bitmask.

#### Behind
```csharp
public bool Behind(WoWUnit? obj)
```
Whether this unit is behind another unit.

#### IsPlayerBehind
```csharp
public bool IsPlayerBehind { get; }
```
Whether the player is behind this unit.

---

### 👀 Targeting & Facing

#### CurrentTarget
```csharp
public WoWUnit? CurrentTarget { get; }
```
The unit's current target.

#### CurrentTargetGuid
```csharp
public ulong CurrentTargetGuid { get; }
```
GUID of the unit's current target.

#### HasTarget
```csharp
public bool HasTarget { get; }
```
Whether the unit has a target.

#### IsFacing
```csharp
public bool IsFacing(WoWObject obj)
public bool IsFacing(WoWPoint point)
public bool IsFacing(float degrees = 70f)
```
Whether the unit is facing an object, point, or direction.

#### InteractRange
```csharp
public float InteractRange { get; }
```
The range at which the unit can be interacted with.

#### BoundingRadius
```csharp
public float BoundingRadius { get; }
```
The unit's collision radius.

#### CombatReach
```csharp
public float CombatReach { get; }
```
The unit's melee attack range.

---

### 🧙 Casting & Channeling

#### IsCasting
```csharp
public bool IsCasting { get; }
```
Whether the unit is casting a spell.

#### IsChanneling
```csharp
public bool IsChanneling { get; }
```
Whether the unit is channeling a spell.

#### CastingSpell
```csharp
public WoWSpell? CastingSpell { get; }
```
The spell the unit is currently casting.

#### ChanneledSpell
```csharp
public WoWSpell? ChanneledSpell { get; }
```
The spell the unit is currently channeling.

#### CastingSpellId / ChanneledCastingSpellId
```csharp
public int CastingSpellId { get; }
public int ChanneledCastingSpellId { get; }
```
Spell IDs of current cast/channel.

#### CurrentCastTimeLeft
```csharp
public TimeSpan CurrentCastTimeLeft { get; }
```
Time remaining on current cast.

#### CurrentChannelTimeLeft
```csharp
public TimeSpan CurrentChannelTimeLeft { get; }
```
Time remaining on current channel.

#### CanInterruptCurrentSpellCast
```csharp
public bool CanInterruptCurrentSpellCast { get; }
```
Whether the current cast can be interrupted.

---

### ✨ Auras (Buffs/Debuffs)

#### Auras
```csharp
public List<WoWAura> Auras { get; }
```
All auras on the unit.

#### ActiveAuras
```csharp
public Dictionary<string, WoWAura> ActiveAuras { get; }
```
Dictionary of active auras by spell name.

#### Debuffs
```csharp
public List<WoWAura> Debuffs { get; }
```
All debuffs on the unit.

#### GetAuraById / GetAuraByName
```csharp
public WoWAura? GetAuraById(int spellId)
public WoWAura? GetAuraByName(string name)
```
Get a specific aura by ID or name.

#### GetAllAuras
```csharp
public List<WoWAura> GetAllAuras()
```
Get all auras (buffs and debuffs).

#### GetAuraTimeLeft
```csharp
public TimeSpan GetAuraTimeLeft(string auraName)
```
Get time remaining on an aura.

#### GetAuraStacks
```csharp
public uint GetAuraStacks(string auraName)
```
Get stack count of an aura.

#### HasAura / HasAuraById
```csharp
public bool HasAura(string auraName)
public bool HasAura(int spellId)
public bool HasAuraById(int spellId)
```
Check if unit has a specific aura.

#### HasAuraWithEffect
```csharp
public bool HasAuraWithEffect(WoWApplyAuraType auraType)
```
Check if unit has an aura with a specific effect type.

#### HasMyAura
```csharp
public bool HasMyAura(string auraName)
public bool HasMyAura(int spellId)
```
Check if unit has an aura cast by the player.

---

### 🤝 Reaction & Hostility

#### Reaction
```csharp
public WoWUnitReaction Reaction { get; }
```
The unit's reaction: `Hostile`, `Neutral`, `Friendly`, `Unknown`.

#### IsHostile
```csharp
public bool IsHostile { get; }
```
Whether the unit is hostile to the player.

#### IsFriendly
```csharp
public bool IsFriendly { get; }
```
Whether the unit is friendly.

#### IsNeutral
```csharp
public bool IsNeutral { get; }
```
Whether the unit is neutral.

#### FactionId
```csharp
public uint FactionId { get; }
```
The unit's faction ID.

---

### 🏪 NPC Types

#### IsQuestGiver
```csharp
public bool IsQuestGiver { get; }
```
Whether the unit gives quests.

#### IsVendor
```csharp
public bool IsVendor { get; }
```
Whether the unit is a vendor.

#### IsAnyVendor
```csharp
public bool IsAnyVendor { get; }
```
Whether the unit is any type of vendor.

#### IsTrainer / IsClassTrainer / IsProfessionTrainer
```csharp
public bool IsTrainer { get; }
public bool IsClassTrainer { get; }
public bool IsProfessionTrainer { get; }
```
Whether the unit is a trainer.

#### IsFlightMaster
```csharp
public bool IsFlightMaster { get; }
```
Whether the unit is a flight master.

#### IsInnkeeper
```csharp
public bool IsInnkeeper { get; }
```
Whether the unit is an innkeeper.

#### IsBanker / IsGuildBanker
```csharp
public bool IsBanker { get; }
public bool IsGuildBanker { get; }
```
Whether the unit is a banker.

#### IsAuctioneer
```csharp
public bool IsAuctioneer { get; }
```
Whether the unit is an auctioneer.

#### IsRepairMerchant
```csharp
public bool IsRepairMerchant { get; }
```
Whether the unit repairs equipment.

#### IsSpiritHealer / IsSpiritGuide
```csharp
public bool IsSpiritHealer { get; }
public bool IsSpiritGuide { get; }
```
Whether the unit is a spirit healer/guide.

#### IsStableMaster
```csharp
public bool IsStableMaster { get; }
```
Whether the unit is a stable master.

#### IsBattleMaster
```csharp
public bool IsBattleMaster { get; }
```
Whether the unit is a battleground master.

#### IsTabardDesigner
```csharp
public bool IsTabardDesigner { get; }
```
Whether the unit is a tabard designer.

#### IsGuard
```csharp
public bool IsGuard { get; }
```
Whether the unit is a guard.

#### CanGossip
```csharp
public bool CanGossip { get; }
```
Whether the unit can be gossiped with.

---

### 🎯 Tagging & Looting

#### TaggedByMe
```csharp
public bool TaggedByMe { get; }
```
Whether the unit is tagged by the player.

#### TaggedByOther
```csharp
public bool TaggedByOther { get; }
```
Whether the unit is tagged by someone else.

#### CanLoot
```csharp
public bool CanLoot { get; }
```
Whether the unit can be looted.

#### Lootable
```csharp
public bool Lootable { get; }
```
Alias for `CanLoot`.

#### Skinnable
```csharp
public bool Skinnable { get; }
```
Whether the unit can be skinned.

#### Looting
```csharp
public bool Looting { get; }
```
Whether the unit is currently being looted.

---

### 🐾 Pet & Summon

#### IsPet
```csharp
public bool IsPet { get; }
```
Whether this unit is a pet.

#### Pet
```csharp
public virtual WoWUnit? Pet { get; }
```
The unit's pet (if any).

#### PetGuid
```csharp
public ulong PetGuid { get; }
```
GUID of the unit's pet.

#### SummonedBy
```csharp
public WoWUnit? SummonedBy { get; }
```
The unit that summoned this unit (if summoned).

#### SummonedByGuid
```csharp
public ulong SummonedByGuid { get; }
```
GUID of the summoner.

#### CreatedBy
```csharp
public WoWUnit? CreatedBy { get; }
```
The unit that created this unit (e.g., totems).

#### CreatedByGuid
```csharp
public ulong CreatedByGuid { get; }
```
GUID of the creator.

---

### 🎭 Creature Information

#### CreatureTypeId
```csharp
public uint CreatureTypeId { get; }
```
Creature type ID (Beast, Dragonkin, Demon, etc.).

#### CreatureType
```csharp
public WoWCreatureType CreatureType { get; }
```
Creature type enum.

#### CreatureSkinType
```csharp
public WoWCreatureSkinType CreatureSkinType { get; }
```
Creature skin type.

#### CreatureRank
```csharp
public WoWUnitClassificationType CreatureRank { get; }
```
Creature rank: `Normal`, `Elite`, `RareElite`, `WorldBoss`, `Rare`.

#### Elite
```csharp
public bool Elite { get; }
```
Whether the unit is elite.

#### IsBoss
```csharp
public bool IsBoss { get; }
```
Whether the unit is a boss (elite and high level or skull).

#### IsCritter
```csharp
public bool IsCritter { get; }
```
Whether the unit is a critter.

---

### 🚩 Flags & State

#### PvpFlagged
```csharp
public bool PvpFlagged { get; }
```
Whether the unit is flagged for PvP.

#### PlayerControlled
```csharp
public bool PlayerControlled { get; }
```
Whether the unit is player-controlled (player or player pet).

#### CanSelect
```csharp
public bool CanSelect { get; }
```
Whether the unit can be selected.

#### Shapeshift
```csharp
public ShapeshiftForm Shapeshift { get; }
```
The unit's current shapeshift form (Druid forms, Warrior stances, etc.).

#### Mounted
```csharp
public virtual bool Mounted { get; }
```
Whether the unit is mounted.

#### MountDisplayId
```csharp
public uint MountDisplayId { get; }
```
Display ID of the unit's mount.

#### OnTaxi
```csharp
public bool OnTaxi { get; }
```
Whether the unit is on a taxi/flight path.

#### PetInCombat
```csharp
public bool PetInCombat { get; }
```
Whether the unit's pet is in combat.

#### IsAutoAttacking
```csharp
public bool IsAutoAttacking { get; }
```
Whether the unit is auto-attacking.

#### Tracked
```csharp
public bool Tracked { get; }
```
Whether the unit is being tracked on the minimap.

#### RafLinked
```csharp
public bool RafLinked { get; }
```
Whether the unit is linked via Recruit-A-Friend.

!!! info "WotLK Feature"
    RAF was introduced in WotLK 3.3.0.

---

### 🎨 Display

#### DisplayId
```csharp
public uint DisplayId { get; }
```
The unit's display model ID.

#### NativeDisplayId
```csharp
public uint NativeDisplayId { get; }
```
The unit's native (original) display ID.

---

## Methods

### 🎯 Targeting

#### Target
```csharp
public void Target()
```
Sets this unit as the player's current target.

**Example:**
```csharp
WoWUnit enemy = ObjectManager.GetObjectsOfType<WoWUnit>()
    .FirstOrDefault(u => u.IsHostile && u.IsAlive);
if (enemy != null)
{
    enemy.Target();
}
```

### 🤝 Interaction

#### Interact
```csharp
public void Interact()
```
Right-clicks the unit (open vendor, talk, etc.).

#### RightClick
```csharp
public void RightClick()
```
Alias for `Interact()`.

### 🔄 Facing

#### Face
```csharp
public void Face()
```
Turns the player to face this unit.

**Example:**
```csharp
unit.Face();
SpellManager.Cast("Fireball");
```

### 🔍 Utility

#### UpdateAuras
```csharp
public void UpdateAuras()
```
Forces an update of the unit's aura list.

!!! note
    Usually called automatically. Rarely needed manually.

---

## Usage Examples

### Find and Attack Nearest Enemy
```csharp
var enemy = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsHostile && 
                u.IsAlive && 
                u.Attackable && 
                u.Distance < 40)
    .OrderBy(u => u.Distance)
    .FirstOrDefault();

if (enemy != null)
{
    enemy.Target();
    enemy.Face();
    SpellManager.Cast("Sinister Strike");
}
```

### Check for CC'd Target
```csharp
WoWUnit target = ObjectManager.Me.CurrentTarget;
if (target != null && target.IsAlive)
{
    if (target.Stunned || target.Rooted || target.Fleeing)
    {
        Logger.Write("Target is CC'd - safe to cast");
    }
}
```

### Find Low Health Enemies (Execute Range)
```csharp
var executeTargets = ObjectManager.GetObjectsOfType<WoWUnit>()
    .Where(u => u.IsHostile && 
                u.IsAlive && 
                u.HealthPercent < 20 &&
                u.Distance < 10)
    .ToList();

Logger.Write($"{executeTargets.Count} enemies in execute range");
```

### Check Casting Enemy
```csharp
if (target.IsCasting && target.CanInterruptCurrentSpellCast)
{
    if (SpellManager.CanCast("Kick"))
    {
        SpellManager.Cast("Kick");
        Logger.Write($"Interrupted {target.CastingSpell?.Name}");
    }
}
```

### Find Quest Giver
```csharp
var questGiver = ObjectManager.GetObjectsOfType<WoWUnit>()
    .FirstOrDefault(u => u.IsQuestGiver && u.Distance < 50);

if (questGiver != null)
{
    questGiver.Target();
    questGiver.Interact();
}
```

### Check Auras
```csharp
if (unit.HasAura("Polymorph"))
{
    Logger.Write("Unit is polymorphed");
}

if (unit.HasMyAura("Corruption"))
{
    Logger.Write($"My Corruption has {unit.GetAuraTimeLeft(\"Corruption\").TotalSeconds:F1}s left");
}
```

---

## WotLK 3.3.5a Notes

### ✅ Available Features
- All basic properties (health, power, combat status)
- Movement and position
- Casting and channeling detection
- Aura system (buffs/debuffs)
- NPC flags and interactions
- Pet happiness (removed in Cataclysm)
- Refer-A-Friend (added in 3.3.0)
- LFG roles via raid roster (patch 3.3.0+)

### ❌ Not Available
- Transmog/void storage (Cataclysm 4.3)
- Glyphs v2 (MoP 5.0)
- Monk class (MoP 5.0)
- Some Cataclysm aura mechanics

---

## See Also

- [WoWPlayer](wowplayer.md) - Player-specific properties
- [LocalPlayer](localplayer.md) - The current player
- [WoWObject](wowobject.md) - Base object class
- [ObjectManager](../styx/objectmanager.md) - Object retrieval
- [WoWAura](#) - Aura/buff/debuff information
- [WoWSpell](#) - Spell information
