# WotLK 3.3.5a — Rotation Reference (from icy-veins.com)

> **Purpose:** Authoritative rotation data for every class/spec, used to fix the Singular WotLK combat routine.
> **Source:** All data from https://www.icy-veins.com/wotlk-classic/
> **Rule:** "On doit rien inventer" — implement exactly what the guides say.

---

## Table of Contents

1. [Death Knight](#death-knight)
2. [Druid](#druid)
3. [Hunter](#hunter)
4. [Mage](#mage)
5. [Paladin](#paladin)
6. [Priest](#priest)
7. [Rogue](#rogue)
8. [Shaman](#shaman)
9. [Warlock](#warlock)
10. [Warrior](#warrior)
11. [Audit Issues vs Rotation Data](#audit-issues-vs-rotation-data)

---

## Death Knight

### Blood DK (Tank)

**Presence:** Frost Presence (required for threat).

**ST Priority:**
1. Maintain diseases (Icy Touch + Plague Strike)
2. Death Strike (self-heal + damage)
3. Blood Strike (filler, Desolation uptime if Unholy)
4. Icy Touch spam (massive threat in Frost Presence)
5. Blood Tap on IT for snap threat

**Opener:** Death Grip → IT → PS → Death Strike → Blood Strike → Blood Tap (on IT) → Blood Strike → IT → Empower Rune Weapon → Pestilence → Death Strike → spam IT.

**AoE:** Death and Decay → IT → PS → Pestilence → Blood Boil spam (health stable) / Death Strike (danger).

**CDs:** Anti-Magic Shell, Raise Dead + Death Pact (40% heal), Vampiric Blood (+15% HP, +35% healing), Mark of Blood, Dancing Rune Weapon, Rune Tap, Unholy Frenzy, Icebound Fortitude, Empower Rune Weapon.

---

### Frost DK (DPS)

**Presence:** Blood Presence (for 15% damage).

**ST Priority:**
1. Diseases up (IT + PS)
2. Obliterate (main damage, Annihilation prevents disease removal)
3. Pestilence (refresh diseases with Glyph of Disease) or Blood Strike
4. Frost Strike on Killing Machine proc
5. Howling Blast **ONLY on Rime proc** (single target)
6. Frost Strike (without KM, RP dump)

**Opener:** IT → PS → Unbreakable Armor/Blood Tap → Obliterate → Pestilence → ERW → 3x Obliterate.

**AoE:** Diseases → Pestilence spread → spam Howling Blast + Death and Decay.

**CDs:** Unbreakable Armor (+25% armor, +20% str), Deathchill (next Frost = crit), ERW, AMS, IBF, Raise Dead + Death Pact.

**CODE FIX NEEDED:** Howling Blast in Blood.cs pull is dead code (51-pt Frost talent). In Frost, HB should only fire on Rime proc for ST.

---

### Unholy DK (DPS)

**Presence:** Unholy Presence (opener) → Blood Presence (sustained for 15% damage).

**ST Priority:**
1. Diseases up (PS + IT)
2. Blood Strike (for Desolation uptime)
3. Death and Decay (primary Frost/Unholy rune spender, **even single target!**)
4. Death Coil (RP spender)

**Opener:** Unholy Presence → PS → IT → 2x Blood Strike → Blood Tap → DnD → Ghoul Frenzy → haste items → Summon Gargoyle → ERW + cancel BT → Army of the Dead → Blood Presence.

**AoE:** Diseases → Pestilence spread → DnD.

**CDs:** Summon Gargoyle (snapshots buffs, main damage source), Anti-Magic Zone, ERW, AMS, IBF.

**CODE FIX NEEDED:** Duplicate Lichborne with `Spell.Buff` instead of `Spell.BuffSelf` + no settings guard.

---

## Druid

### Balance Druid (DPS)

**ST Priority:**
1. Faerie Fire (maintain Improved Faerie Fire debuff)
2. Force of Nature (within melee range of boss)
3. Starfall (safe positioning)
4. Insect Swarm (maintain unless in Lunar Eclipse)
5. During Eclipse: spam buffed spell (Lunar = Starfire, Solar = Wrath)
6. When not in Eclipse: work towards next one (Wrath for first, then alternate)
7. Moonfire (movement only, unless T9 2-set)

**AoE:**
1. Starfall
2. Typhoon (with Glyph of Typhoon)
3. Hurricane (channel)

**CDs:** Force of Nature (3min), Starfall (1.5min).

---

### Feral Druid DPS (Cat)

**ST Priority:**
1. Tiger's Fury (when <40 Energy)
2. Berserk (if available and 15+ sec remain on TF cooldown, pool to ~90 Energy first)
3. Savage Roar (maintain at ALL times — use at any CP count if about to fall off)
4. Faerie Fire (Feral) (to proc Omen of Clarity)
5. Use OoC procs on Shred
6. Rip at 5 CP (maintain, refresh after expiry)
7. Rake (maintain, refresh after expiry)
8. Mangle (Cat) debuff (unless Arms Warrior with Trauma in raid)
9. Shred (CP builder)
10. Ferocious Bite (only if target dying soon, replaces Rip)

**AoE:**
1. Tiger's Fury + Berserk same rules
2. Savage Roar
3. Faerie Fire (Feral) for OoC
4. Swipe (Cat)

**CODE FIX NEEDED:**
- **Blood in the Water is Cata-only** — FB at <25% HP refreshes Rip does NOT exist in WotLK. Remove.
- Missing Savage Roar in PvP Cat and Normal combat.
- Missing Rip + SR maintenance in general rotation.

---

### Feral Druid Tank (Bear)

*(Not a separate spec in WotLK — same Feral tree, but bear form)*

**ST:** Mangle (Bear) > Lacerate (maintain 5 stacks) > Swipe > Maul (rage dump).
**CDs:** Survival Instincts, Barkskin, Frenzied Regeneration, Enrage.

---

### Restoration Druid (Healer)

**Priority:**
1. Nature's Swiftness + Healing Touch (emergency)
2. Swiftmend, Regrowth, Nourish (emergency backup if NS on CD)
3. Lifebloom on tank (spend Omen of Clarity procs)
4. Wild Growth (on groups of damaged allies)
5. Rejuvenation (on anyone taking damage)

**Tank Healing:** Keep Lifebloom, Rejuvenation, Wild Growth, Regrowth rolling. Swiftmend + Nourish spam if health dropping.

**CDs:** Tranquility (party burst heal), Innervate (mana), Rebirth (combat res), Nature's Swiftness.

**CODE FIX NEEDED:** DruidSettings treats Tree of Life as Cata cooldown (ToLHealth/Count). In WotLK, ToL is a **permanent form** (like Bear/Cat). Settings should be removed or repurposed.

---

## Hunter

### Beast Mastery Hunter (DPS)

**ST Priority:**
1. Kill Shot (<20% HP)
2. Kill Command
3. Explosive Trap (if safe to place)
4. Serpent Sting (maintain)
5. Multi-Shot (if mana allows)
6. Arcane Shot
7. Steady Shot (filler)

**AoE:** Explosive Trap + **Volley** spam.

**CDs:** Bestial Wrath (+damage for pet+self), Rapid Fire (+40% ranged haste), Call of the Wild (pet talent, +10% AP).

---

### Marksmanship Hunter (DPS)

**ST Priority:**
1. Kill Shot (<20% HP)
2. Kill Command (off-GCD)
3. Serpent Sting (maintain, refreshed by Chimera Shot)
4. Explosive Trap
5. Chimera Shot (refreshes Serpent Sting)
6. Aimed Shot
7. Arcane Shot
8. Silencing Shot (off-GCD, use for damage)
9. Steady Shot (filler)

**AoE:** Explosive Trap + **Volley** spam.

**CDs:** Readiness (resets all CDs), Rapid Fire, Call of the Wild.

---

### Survival Hunter (DPS)

**ST Priority:**
1. Kill Shot (<20% HP)
2. Explosive Shot
3. Explosive Trap
4. Kill Command (off-GCD)
5. Serpent Sting (maintain)
6. Aimed Shot
7. Steady Shot (filler)

**Lock and Load proc:** Spam Explosive Shot (R4→R3→R4 to avoid DoT clipping, or wait 1 GCD between casts).

**AoE:** Explosive Trap + **Volley** spam.

**CDs:** Rapid Fire, Call of the Wild.

**CODE FIX NEEDED (ALL SPECS):**
- **Volley completely missing** from all AoE rotations — primary WotLK ranged AoE.
- **Aspect of the Viper missing** — WotLK mana management aspect.
- `PetManager.CallPet(PetSlot)` — WotLK only has one "Call Pet" (no numbered slots).
- `StyxWoW.Me.Pet.HealthPercent` without null guard — NRE for sub-10 hunters.
- Hardcoded max-rank trap spell IDs won't work for sub-80.

---

## Mage

### Arcane Mage (DPS)

**ST Priority (Burn Phase — with CDs):**
1. Arcane Power + Icy Veins (if available) + Mirror Image
2. **Presence of Mind** + Arcane Blast (instant cast)
3. Arcane Blast x4 (build to 4 stacks)
4. Arcane Missiles (proc-based, use with 4 AB stacks)
5. Arcane Barrage (if Missile Barrage proc consumed and need to move)

**ST Priority (Conserve Phase — mana management):**
1. Arcane Blast to 3 stacks
2. Arcane Missiles (with Missile Barrage proc)
3. Arcane Barrage (reset stacks if mana low)

**AoE:** Arcane Explosion (if grouped) / Blizzard (if stationary).

**CDs:** Arcane Power, Icy Veins, **Presence of Mind** (instant next cast), Mirror Image, Evocation.

**CODE FIX NEEDED:** Presence of Mind completely missing.

---

### Fire Mage (DPS)

**ST Priority:**
1. Living Bomb (maintain)
2. Pyroblast (with Hot Streak proc — instant cast)
3. Fire Blast (if Hot Streak not proccing)
4. Scorch (maintain debuff — Improved Scorch, unless another source)
5. Fireball (filler)

**AoE:** Flamestrike (initial) → Blizzard / Living Bomb on multiple targets.

**CDs:** **Combustion** (crit stacking), Mirror Image, Icy Veins (from Frost tree, if talented).

**CODE FIX NEEDED:** Combustion completely missing.

---

### Frost Mage (DPS)

**ST Priority:**
1. Frostbolt (primary nuke, procs Fingers of Frost and Brain Freeze)
2. Deep Freeze (on Fingers of Frost proc — huge damage on frozen targets)
3. Frostfire Bolt (on Brain Freeze proc — instant cast)
4. Ice Lance (on Fingers of Frost proc, if Deep Freeze on CD)
5. Mirror Image (on CD)

**AoE:** Blizzard (channeled).

**CDs:** Icy Veins, **Cold Snap** (resets all Frost CDs), Mirror Image, Summon Water Elemental.

**CODE FIX NEEDED:** Cold Snap completely missing. Conjure Water missing for pre-74.

---

## Paladin

### Retribution Paladin (DPS)

**Seal:** Seal of Vengeance (ST) / Seal of Command (AoE).

**ST Priority (FCFS — First Come First Serve):**
1. Crusader Strike
2. Judgement of Wisdom
3. **Divine Storm** ← SINGLE TARGET TOO, not gated to 4+ targets
4. Consecration
5. Exorcism
6. Holy Wrath (Undead/Demon only)
7. Hand of Reckoning (small damage if not tanking)

**AoE:** Seal of Command → Consecration → Divine Storm → JoW → Holy Wrath → CS → Exorcism.

**CDs:** Avenging Wrath (+30% damage), Divine Protection, Divine Shield, Divine Sacrifice, Lay on Hands. Divine Plea for mana.

**CODE FIX NEEDED:** Divine Storm gated to 4+ targets only — should be in single-target FCFS rotation.

---

### Protection Paladin (Tank)

**Seal:** Seal of Vengeance (ST) / Seal of Command (AoE).

**Pre-pull:** Holy Shield active.

**ST Priority:**
1. Hammer of the Righteous
2. Judgement of Wisdom
3. Consecration
4. Shield of Righteousness
5. Holy Shield (maintain)

**Pull:** Exorcism / Avenger's Shield → melee rotation.

**AoE:** Seal of Command → HotR → Consecration → Holy Wrath (Undead/Demon) → JoW → SoR → Holy Shield.

**CDs:** Avenging Wrath, Divine Protection, Divine Shield, Divine Sacrifice, Lay on Hands, Hand of Sacrifice.

---

### Holy Paladin (Healer)

**No rotation — reactive healing.**

- Holy Light (main heal, procs Light's Grace for faster cast)
- Flash of Light (spot heal / last resort)
- Holy Shock (emergency instant)
- Seal of Wisdom + Glyph for mana reduction
- Judge once per minute for Judgements of the Pure haste buff

**CDs:** Divine Illumination (50% mana cost reduction), Divine Favor (100% crit next heal), Aura Mastery, Divine Protection, Divine Sacrifice, Lay on Hands.

**CODE FIX NEEDED:** Holy.cs has Crusader Strike — deep Ret talent, unavailable to Holy. Missing Sacred Shield, Divine Favor, seal management.

---

## Priest

### Shadow Priest (DPS)

**ST Priority:**
1. Vampiric Touch (maintain — procs Replenishment with Mind Blast)
2. Devouring Plague (maintain — instant cast, good for movement)
3. Mind Blast (on CD — high damage, procs Improved Spirit Tap)
4. Mind Flay (filler — also refreshes SW:P via Pain and Suffering talent)
5. Shadow Word: Death (movement only)
6. Shadow Word: Pain (apply once, then Pain and Suffering refreshes it via Mind Flay — snapshot at 5 stacks of Shadow Weaving)

**AoE:** Mind Sear (if mobs die quickly) or multi-DoT (if mobs live 24+ sec).

**CDs:** Dispersion (DR + mana), Inner Focus (free cast + 25% crit), Shadowfiend (mana), Divine Hymn (emergency raid heal), Hymn of Hope (mana restore), Fade (with Improved Shadowform removes snares).

**CODE FIX NEEDED:**
- `Thread.Sleep(100)` blocks main thread (3 occurrences) — replace with WaitTimer/Coroutine
- `MindBlastOrbs` setting references Shadow Orbs (Cata mechanic) — remove

---

### Discipline Priest (Healer)

**ST Healing Priority:**
1. Penance (best burst heal)
2. Power Word: Shield (main priority — always cast, especially for Borrowed Time buff)
3. Prayer of Mending (on CD)
4. Renew (on targets taking constant damage)
5. Greater Heal (if target won't die before cast finishes)
6. Flash Heal (filler)
7. Binding Heal (if self also injured)

**Emergency Priority:** PW:S → Penance → Flash Heal spam → Binding Heal.

**Raid Healing:**
1. Prayer of Mending
2. Power Word: Shield (trigger Borrowed Time)
3. Prayer of Healing (with Borrowed Time haste buff)
4. Penance
5. Renew
6. Divine Hymn (8min CD, emergency only)

**CDs:** Pain Suppression (2.4min), Power Infusion (1.6min), Inner Focus (2.4min, pair with PoH/Divine Hymn), Shadowfiend, Hymn of Hope, Divine Hymn, Desperate Prayer.

---

### Holy Priest (Healer)

**ST Healing Priority:**
1. Renew (on targets taking constant damage)
2. Prayer of Mending (on CD)
3. Greater Heal (big incoming damage)
4. Flash Heal (filler)
5. Binding Heal (if self injured)

**Emergency Priority:** PW:S → Flash Heal spam → Binding Heal.

**Raid Healing:**
1. Prayer of Mending
2. Circle of Healing (on CD)
3. Prayer of Healing (with Serendipity stacks)
4. Renew
5. Flash Heal
6. Binding Heal
7. Divine Hymn (8min CD, emergency only)

**CDs:** Guardian Spirit (tank saver, 40% healing received + cheat death), Inner Focus, Shadowfiend, Divine Hymn, Hymn of Hope, Desperate Prayer.

---

## Rogue

### Assassination Rogue (DPS)

**ST Priority:**
1. Mutilate (CP builder)
2. **Hunger for Blood** (maintain — 51-pt talent, +15% damage, needs bleed on target)
3. Slice and Dice (maintain)
4. Envenom at 4-5 CP

**Opener:** Mutilate → Slice and Dice → Hunger for Blood → Mutilate → Envenom. If no bleed provider: self-Rupture first then HfB.

**CDs:** Vanish (for Overkill energy regen), Cold Blood (on Envenom). Expose Armor if no Warrior.

**AoE:** Fan of Knives at 6+ targets.

**CODE FIX NEEDED:** **Hunger for Blood completely missing** from code — core 51-pt talent.

---

### Combat Rogue (DPS)

**ST Priority:**
1. Sinister Strike (CP builder)
2. Slice and Dice (maintain with 5 CP)
3. Rupture (maintain with 5 CP)
4. Eviscerate (only as filler when SnD + Rupture both active)

**CDs:** Blade Flurry (cleave), Adrenaline Rush (energy), Killing Spree (burst). CD sequence: BF → KS → AR.

**AoE:** Fan of Knives at 3+ targets, maintain SnD.

---

### Subtlety Rogue (DPS)

**ST Priority:**
1. Hemorrhage (CP builder, replaces Sinister Strike)
2. Honor Among Thieves (passive CP from group crits)
3. Expose Armor (maintain)
4. Slice and Dice (maintain)
5. Rupture (maintain — Shadowstep before Rupture for +20% damage)
6. Ghostly Strike (on CD)
7. Eviscerate (CP dump)

**CDs:** Shadow Dance (use stealth abilities: Garrote → Ambush → refresh debuffs → Eviscerate), Vanish (for Master of Subtlety buff before Shadow Dance).

**AoE:** Fan of Knives at 6+ targets.

---

## Shaman

### Elemental Shaman (DPS)

**ST Priority:**
1. Totems (maintain)
2. Flame Shock (maintain)
3. Lava Burst (with FS on target for guaranteed crit)
4. Elemental Mastery (when available)
5. Chain Lightning (if mana OK)
6. Lightning Bolt (filler)
7. Frost Shock (movement)

**Weapon:** Flametongue.

**AoE:** Magma Totem → Fire Nova → Elemental Mastery → Chain Lightning → Thunderstorm.

---

### Enhancement Shaman (DPS)

**ST Priority:**
1. Totems (maintain)
2. Fire Elemental + Feral Spirit (CDs)
3. Lightning Bolt / Chain Lightning with 5 Maelstrom Weapon stacks (cast weaving without resetting swing timer)
4. Stormstrike
5. Flame Shock (maintain)
6. Magma Totem (if in range)
7. Lightning Shield (maintain)
8. Earth Shock
9. Fire Nova
10. Lava Lash (lowest priority)

**Weapons:** Windfury main-hand, Flametongue off-hand.

**CDs:** Shamanistic Rage (30% DR + mana), Feral Spirit, Fire Elemental Totem.

**CODE FIX NEEDED:** **Shamanistic Rage completely missing** (41-pt talent).

---

### Restoration Shaman (Healer)

**Priority:**
1. Nature's Swiftness + Tidal Force + Healing Wave (emergency)
2. Lesser Healing Wave (emergency backup)
3. Cleanse debuffs
4. Earth Shield on tank + Water Shield on self
5. Totems active
6. Riptide (on CD)
7. Chain Heal (consume Riptide for bonus)
8. Healing Wave (single target filler)

**Weapon:** Earthliving.

**Key:** Mana Tide Totem, Tremor Totem timing, cast-cancel for mana efficiency.

**CODE FIX NEEDED:** Dead Cata T12 tier-set code in Restoration.cs. Elemental Resistance Totem comment says "not in WotLK" but it IS in WotLK (since 3.0.2).

---

## Warlock

### Affliction Warlock (DPS)

**ST Priority:**
1. Shadow Bolt (precast opener)
2. Haunt (maintain — key talent, buffs all DoTs)
3. Unstable Affliction (maintain)
4. Corruption (apply after Haunt + UA for ISB snapshot — then Everlasting Affliction auto-refreshes it)
5. Curse of Agony (maintain)
6. Shadow Bolt (filler)

**Corruption note:** Snapshots crit + damage modifiers. Only re-apply manually at 35% (Death's Embrace).

**Execute (<25%):** Drain Soul (4x damage) replaces Shadow Bolt. Keep DoTs + Haunt up, Drain Soul as much as possible.

**AoE:** Seed of Corruption spam. Soulshatter for threat.

**CDs:** no major DPS cooldowns outside trinkets.

---

### Demonology Warlock (DPS)

**Pet:** Felguard.

**ST Priority:**
1. Life Tap (maintain Glyph buff)
2. Metamorphosis (pop when available)
3. Immolate (maintain)
4. Corruption (maintain — apply while running in)
5. Curse of Doom (or Curse of Agony if boss dies <60s)
6. Immolation Aura + Shadow Cleave (during Meta, melee range)
7. Shadow Bolt (filler)
8. Incinerate (on Molten Core procs)

**Execute (<35%):** Decimation → Soul Fire spam as filler (replaces Shadow Bolt). Keep Immolate + Corruption up. ISB debuff maintenance.

**AoE:** Meta → Immolation Aura + Shadow Cleave → Shadowflame → Seed of Corruption spam.

**CDs:** Metamorphosis (3min, +20% damage).

---

### Destruction Warlock (DPS)

**Pet:** Imp.

**ST Priority:**
1. Soul Fire (precast opener)
2. Curse of Doom (or CoA if boss dies <60s)
3. Immolate (maintain)
4. Conflagrate (on CD — grants 3 Backdraft stacks)
5. Chaos Bolt (on CD)
6. Incinerate (filler)
7. Corruption (movement only — damage loss to maintain fulltime)

**AoE:** Seed of Corruption + Shadowfury on CD + Inferno (summon).

**CDs:** Inferno (only major CD).

---

## Warrior

### Arms Warrior (DPS)

**Stance:** Battle Stance.

**ST Priority:**
1. Sunder Armor (if needed, no dedicated debuff provider)
2. Rend (maintain — procs Taste for Blood)
3. Overpower (on Taste for Blood proc)
4. Bladestorm
5. Execute (with Sudden Death proc or <20%)
6. Mortal Strike
7. Slam (filler)
8. Heroic Strike (>40 rage dump, off-GCD)

**AoE:** Rend → Sweeping Strikes → Overpower → Thunder Clap → Bladestorm → Cleave.

**CDs:** Sweeping Strikes, Bladestorm (90s), Shattering Throw (armor debuff, use on Bloodlust), Recklessness.

**CODE FIX NEEDED:**
- "Slaughter" aura reference (Cata)
- "Blood and Thunder" talent check (Cata — auto-spreads Rend via Thunder Clap)
- "Incite" proc aura (Cata passive in WotLK — different mechanic)

---

### Fury Warrior (DPS)

**Stance:** Berserker Stance.

**ST Priority:**
1. Sunder Armor (if needed)
2. Slam (on **Bloodsurge proc ONLY** — instant cast)
3. Bloodthirst (bread and butter, low CD, 20% chance Bloodsurge)
4. Whirlwind (highest damage with Titan's Grip dual 2H + Improved Whirlwind)
5. Rend (filler with Improved Rend + Glyph, if talented)
6. Heroic Strike (>40 rage dump, off-GCD)
7. Execute (lowest priority, nerfed in WotLK)

**AoE:** Whirlwind → Thunder Clap → Cleave (replaces HS).

**CDs:** Death Wish (+20% damage, 2min), Heroic Fury, Shattering Throw, Recklessness.

**CODE FIX NEEDED:**
- "Incite" proc aura (Cata)
- UseWarriorSMF setting (Cata talent — Single-Minded Fury)

---

### Protection Warrior (Tank)

**Stance:** Defensive Stance.

**ST Priority:**
1. Shield Slam (with Shield Block active — bypasses BV cap)
2. Revenge (highest damage with Improved Revenge, hits 2 targets)
3. Shield Slam (without SB, still high priority)
4. Shockwave (scales with AP, prioritize over Devastate at ~3500 AP)
5. Devastate (filler, 30% chance to reset Shield Slam via Sword and Board proc)
6. Heroic Strike (>60 rage dump)

**AoE:** Thunder Clap → Shockwave → Revenge → Cleave.

**CDs:** Shield Block (40s, 100% block + 2x BV), Last Stand, Shield Wall (60% or 40% DR with glyph at 2min CD), Retaliation, Recklessness, Enraged Regeneration, Shattering Throw, Challenging Shout.

---

## Audit Issues vs Rotation Data

### Critical Issues Confirmed by Research

| Issue | Spec | Confirmed | Action |
|-------|------|-----------|--------|
| Divine Storm gated to 4+ targets | Ret Paladin | YES — FCFS includes DS in ST | Remove target count gate |
| Hunger for Blood missing | Assassination Rogue | YES — core 51-pt talent | Add to rotation |
| Volley missing from all AoE | All Hunter specs | YES — primary ranged AoE | Add Volley to AoE |
| Blood in the Water (Cata) | Feral Druid | YES — does NOT exist in WotLK | Remove |
| Presence of Mind missing | Arcane Mage | YES — key instant-cast CD | Add |
| Combustion missing | Fire Mage | YES — key crit-stacking CD | Add |
| Cold Snap missing | Frost Mage | YES — resets all Frost CDs | Add |
| Shamanistic Rage missing | Enhancement Shaman | YES — 41-pt talent | Add |
| Crusader Strike in Holy Paladin | Holy Paladin | YES — unavailable (deep Ret) | Remove |
| Thread.Sleep in Shadow Priest | Shadow Priest | N/A — code issue | Replace with WaitTimer |
| Shadow Orbs setting | Shadow Priest | YES — Cata mechanic | Remove |
| Howling Blast in Blood pull | Blood DK | YES — 51-pt Frost talent | Remove from Blood |
| ToL as Cata cooldown | Resto Druid | YES — permanent form in WotLK | Fix settings |
| T12 tier-set code | Resto Shaman | YES — Cata tier set | Remove |
| Slaughter/Blood and Thunder/Incite | Arms/Fury Warrior | YES — Cata mechanics | Remove |
| SMF setting | Fury Warrior | YES — Cata talent | Remove |
| Group.MeIsTank always false | Infrastructure | N/A — code bug | Fix |
| Aspect of the Viper missing | All Hunter specs | YES — WotLK mana management | Add |
| CallPet(PetSlot) | Hunter Common | YES — WotLK has 1 Call Pet | Fix API |

### Level-Scaling Notes

Rotations must degrade gracefully for pre-80 leveling:

| Level | Key Unlocks |
|-------|-------------|
| 1-9 | Auto-attack + class basic abilities only |
| 10 | Talent points begin, first talent abilities |
| 20 | First mount, more abilities |
| 30 | More talent depth |
| 40 | Epic mount, mid-tree talents |
| 50 | Deep talents accessible |
| 60 | All vanilla abilities, near-finished tree |
| 70 | Most WotLK abilities, dual spec available |
| 80 | All abilities, 71-point talents |

**Implementation approach:** Use `SpellManager.HasSpell("SpellName")` or `SpellManager.CanCast("SpellName")` checks before every spell. Fall back to simpler rotations when key spells are unavailable. Never hardcode spell rank IDs — use spell names.
