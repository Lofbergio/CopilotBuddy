# Dungeon viability — combat coordination plan

**Status:** designed 2026-07-08 (long session), **NOT built**. Build after `/clear` with this + the CLAUDE.mds loaded.
**Scenario:** user drives the TANK manually (VibeParty leader) + 4 bot followers (VibeParty follower + GoodVibes).
Goal: make leveling 5-man dungeons viable.

**Two repos / two deploy paths:**
- **GoodVibes** = combat routine, `E:\!Games\World of Warcraft\CopilotBuddy\Routines\GoodVibes\` (its own git repo,
  branch `main`). Drop-in: edit `.cs`, relaunch CB, NO DLL rebuild. Read `GoodVibes\CLAUDE.md` first.
- **VibeParty** = botbase, `C:\git\CopilotBuddy\Bots\Vibes\VibeParty\`. DLL rebuild + deploy + CB restart. Read this
  folder's `CLAUDE.md` first. ⚠ The whole VibeParty platform (PartyBus/Phase3/Phase5/water) is **still UNTESTED**
  (see the `vibeparty-platform` memory) — Batch 2 rides on it.

⚠ Do NOT let a blind subagent write this tuned logic — read the relevant CLAUDE.md + trace the runtime first.

---

## Batch 1 — GoodVibes only (drop-in, INDEPENDENTLY testable, no bus). Do first.

### 1a. Threat-ease = "default attack near aggro" (user decision)
- **Rule:** in an instance, if the current target is tanked by SOMEONE ELSE (`target.CurrentTargetGuid` != me and
  != my pet) AND my scaled threat% > **80** → restrict to DEFAULT ATTACK only until threat% < **60** (hysteresis).
- **Default attack** = melee auto-attack (melee specs) or **wand/Shoot** (casters); a caster with NO wand → just
  pause (do NOT melee a caster into the pack). During ease, do NOT refresh DoTs (they tick threat); let existing ride.
- **SKULL BYPASS:** if the current target is skull-marked (`GetRaidTargetIndex(unit)==8`) → full rotation, IGNORE
  threat. Skull = the "nuke this" signal.
- **API:** `UnitDetailedThreatSituation("player","target")` → isTanking, status, scaledPercent, rawPercent, threat
  (3.3.5a has it, added 3.0). `GetRaidTargetIndex("unit")`.
- **GOTCHA (load-bearing):** the "tanked by someone else" gate is what keeps SOLO / open-world / being-the-tank at
  full DPS. NEVER throttle when the mob targets me/pet. This is why it's safe to ship routine-wide.

### 1b. Totem auto-swat
- Hostile totems near the fight die ASAP. **Melee:** auto-swap a swing to the nearest hostile totem (near-free).
  **Casters:** INSTANT only (Fire Blast / Shock / Ice Lance), never a hardcast. **Nearest-wins** heuristic — do NOT
  make all 4 swap (a totem dies in one hit; no claim/lease needed). Priority above the normal assist target but
  bounded to the quick kill, then back to focus.
- Totems ARE attackable units (casting works), but instant/melee is right. Detect via creature-type = Totem — VERIFY
  the API (`WoWUnit` creature type / IsTotem; GoodVibes already matches its OWN totems by `CreatedBySpellId`).
- Motivating case: ZF (tons of casters + totems).

### 1c. AoE-dodge melee exclude (user decision)
- `AvoidGroundEffects`: gate OFF for MELEE specs (melee eat the fire to stay glued; casters/healer always step out).

### 1d. Aggressive interrupts / caster lockdown in instances
- In instances, drop the `Interrupt.xml` whitelist → interrupt ANY interruptible cast from any caster in the fight.
  Plus proactive hard CC (Hammer of Justice etc.) on casters even between casts (stuns don't break on damage, so
  stun-locking a caster you're killing is upside). Accept interrupt overlap for v1; light "who-interrupts" bus
  coordination is a later refinement.

---

## Batch 2 — VibeParty (DLL, on the UNTESTED bus). Only after the bus is smoke-tested.

### 2a. Skull-as-assist (tab-free kill order)
- Assist target = the skull-marked hostile unit if one exists in the party's combat, else `LeaderTargetGuid`. Lets
  the tank mark skull ONCE and tab freely (build threat / grab adds) while DPS stay on skull. (1a's bypass alone
  only holds skull while YOU stay targeted on it; this makes it persist.)
- Where: `LeaderAssistTarget` / assist selection in `VibeParty.cs`.

### 2b. Priority-target ladder
- Each tick, follower assist priority: **skull > hostile totem (quick kill) > mob attacking the healer (peel) >
  straggler (engaged, low-HP, not tank's target, one-GCD kill) > leader's current target.**
- HARD rule: only leave the assist target for a ONE-GCD kill (totem / execute-range mob) — never abandon a landing
  hardcast, never chase a healthy straggler. GoodVibes' `AttackWillFinish` already picks an instant for the lethal
  hit, so VibeParty just hands it the target.

### 2c. Peel alert = WHISPER (user decision)
- When a hostile's `CurrentTargetGuid` == a party healer → **`SendChatMessage(..., "WHISPER", nil, leaderName)`**
  (leaderName from the bus), THROTTLED. NOT party chat (mixed parties / PUGs / looks botty). Human tank peels. The
  DPS-help version is just the ladder's "peel" rung.

### 2d. Res coordination — REUSE the lease broker (`PartyLoot` pattern)
- Out of combat, a live ressable-class member claims a dead member (broker keyed `(deadGuid, "res")`); leader grants
  **HEALER-FIRST**; grantee casts its res (Priest Resurrection / Paladin Redemption / Shaman Ancestral Spirit / Druid
  Revive out of combat / Rebirth in). If ALL ressable classes are dead → release + run to the instance entrance; if
  the LEADER/tank is dead → follow his corpse/release.
- Finicky part = corpse-run navigation back into the instance → folds into the deferred nav work.

---

## Build + test order
1. **Batch 1** (GoodVibes, drop-in). Test with 2 bots: one "tanks" by pulling → watch the other ease to
   auto-attack/wand and resume when threat drops; drop a totem → watch the swat; mark skull → watch full-send.
   No bus needed.
2. **Batch 2** (VibeParty) — only AFTER the PartyBus platform is smoke-tested (VibeParty CLAUDE.md "Test status").

## Deferred (acknowledged, not in these batches)
- Navigation reliability in instances (LoS/doors/elevators/corpse-run) — user will improve later; the biggest
  practical wildcard. Worth a 10-min "just follow me through a dungeon, no combat" check before trusting a clear.
- Interrupt round-robin coordination; lowest-count-first loot assignment (needs Phase 3 to send counts); Phase 4
  corpse loop.
