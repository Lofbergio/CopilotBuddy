# VibeQuester — Telemetry & Scoring Spec

Foundation layer for tuning. Everything downstream — the churn fix, dynamic class-aware rest, the death-feedback ratchet, and eventually LLM/optimizer-assisted tuning — needs one thing first: **a parseable record of where each session's time went, and a single score to compare runs.** This spec defines that.

## Principles
- **Measure before tuning.** Capture a baseline (current churny behavior) *before* the fixes, then measure the delta. Score is meaningless without a before.
- **Self-contained in the drop-in.** All code lives in this runtime folder (Roslyn-compiled at startup). No `CopilotBuddy.dll` rebuild. Edit → relaunch → new data.
- **Cheap.** Per-tick work is arithmetic + a bucket tally. File writes are buffered and flushed on a timer, never on the game thread per-event.
- **Attribution-first.** Every session records the *config that produced it*. A score with no config is noise; `(config vector) → (score)` rows are the whole point.
- **Two consumers:** humans/LLM read the event log to diagnose *why*; an optimizer reads the summary CSV to tune *numbers*.

## Architecture — two layers

**Implemented as the bot-agnostic plugin `Plugins/QuestLab/` (Phase 1 built).** Both layers sample from the plugin's `Pulse()` (app-session anchored, survives `TreeRoot` stop/starts). Inter-tick gaps > 3s are attributed to a `BotStopped` bucket — so the churn downtime is captured even though `Pulse()` doesn't fire while the bot is stopped. No background thread, no engine events (kills/deaths are polled).

### Layer 1 — Time-budget sampler (the "where did the hour go")
Every Pulse, classify the current state into **one bucket** and add the elapsed wall-clock since the last tick to that bucket's accumulator. No event detection needed — just a running tally.

Buckets (priority order when ambiguous):
| Bucket | Detection |
|---|---|
| `Dead` | `Me.Dead || Me.IsGhost` |
| `Combat` | `Me.Combat` |
| `Resting` | not combat AND (`Me.HasAura("Food"/"Drink")` OR idle-recover: stationary, resources below ceiling, not interacting) |
| `Vendoring` | `BotPoi.Current.Type ∈ {Sell,Repair,Buy,Train,Mail}` (moving-to or interacting) |
| `Stuck` | bot's existing no-progress detector active |
| `Traveling` | `Me.IsMoving` and none of the above |
| `Other` | fallback (quest gossip, looting pauses, etc.) |

This alone yields the headline diagnostic: `% Combat / Resting / Traveling / Vendoring / Dead / Stuck`. The churn fix should visibly cut `Other`/`Traveling` thrash; the rest fixes should cut `Resting`.

### Layer 2 — Event log (the "what happened, with context")
Discrete records emitted on **state transitions** detected in `Tick` (poll-and-compare; don't depend on engine events existing). Each carries enough context for per-class / per-level / per-zone attribution.

State machine tracks previous: `combat`, `dead`, `level`, `xp`, `position`, `hasFoodDrinkAura`, `poiType`.

Events:
- **`fight`** — on combat exit. `{durationMs, targetId, targetName, targetLevel, levelDelta, peakHostilesInRange, hpStart, hpEnd, powerType, powerStart, powerEnd, loc}`. `peakHostilesInRange` = max hostiles within pull radius during the fight → the overpull signal.
- **`death`** — on alive→dead. `{loc, level, lastFightPeakHostiles, hpTrace?, suspectedCause}`. Then on dead→alive emit **`resurrect`** `{corpseRunMs, durabilityLostPctApprox}`.
- **`rest`** — on rest end (aura cleared / resources recovered / combat). `{reason: food|drink|noConsumableWait, durationMs, hpStart, hpEnd, powerStart, powerEnd, consumable, loc}`. The `noConsumableWait` reason is the costly idle-to-85% branch — we want it visible and counted separately.
- **`quest`** — `{kind: pickup|turnin|objective|abandon|blacklist, questId}`.
- **`levelup`** — `{newLevel, sessionMs}`.
- **`vendor`** — `{kind, durationMs, goldDelta}`.

(If engine events like `BotEvents.Player.OnLevelUp` / an `OnDeath` exist, subscribe as a more reliable source; the poll state-machine is the portable fallback. **Verify which exist before building.**)

## Scoring

### Primary metric — table-free throughput
Avoid embedding an XP-per-level table. Track **effective level progress**:
```
progress(t) = Me.Level + Me.XP / Me.MaxXP        // a real number, e.g. 7.42
score = (progress_end - progress_start) / wallClockHours   // effective levels/hour
```
Levels naturally take longer as you climb, so this number falls with level — fine, because we tune *within* a level band and compare like-for-like. At level cap, switch primary to **quests/hour** (and track **gold/hour**).

Because it's measured on real wall-clock, deaths and downtime are *already* priced in (corpse runs, re-buffing, vendor trips all consume the hour). The decomposition in Layer 1 tells you *which* cost is hurting.

### Guardrail metrics (reported alongside; catch what a short xp/hour sample hides)
- `deathsPerHour`, `meanCorpseRunMs`
- `restingPct`, `noConsumableWaitPct`
- `travelingPct`, `stuckPct`
- `meanTimeToKill`, `overpullRate` = fights with `peakHostiles ≥ 2` / total

### Risk-adjusted fitness (for the optimizer; encodes the asymmetry)
```
fitness = levelsPerHour − λ · deathsPerHour
```
`λ` is large by default — we established a death costs ~10–30 rests, so the optimizer must be risk-averse and never trade safety for a marginal throughput gain on a lucky sample. `λ` is itself a config knob.

## Output files

App-root `Telemetry/` (next to `CopilotBuddy.exe`) — bot-agnostic, since the plugin scores any botbase.

1. **`Telemetry/{yyyy-MM-dd_HHmm}_{char}_{pid}.jsonl`** — append-only event log. One JSON object per line. Restart-safe. First line is a `session` record (see below); thereafter Layer-2 events + periodic `budget` snapshots (Layer 1 flushed every ~30s and at session end).
2. **`Telemetry/sessions.csv`** — **the optimizer/LLM table.** One row appended per session at end (and on a heartbeat, so a crash still leaves a row). Columns = full config vector + score + all guardrail metrics. This is the `(config) → (score)` dataset.

### The `session` record / CSV config snapshot (attribution)
Captured at session start. **This is non-negotiable** — score without config can't be tuned against.
```json
{ "type":"session", "ts":"...", "botVersion":"...",
  "char":"...", "class":"Shaman", "race":"Tauren", "specGuess":"Elemental",
  "startLevel":7.42, "zone":"Mulgore", "map":1,
  "config":{
    "minHealth":65, "minMana":65, "recoverTarget":85,
    "pullDistance":45, "foodAmount":0, "drinkAmount":0,
    "scanStartDistance":250, "maxQuestsPerProfile":20,
    "reloadStrategy":"timer-30s",          // vs "event-driven" after churn fix
    "perClassRestParams":{...},            // once dynamic rest lands
    "lambdaDeathPenalty":...,
    "enabledPlugins":["Talented","AutoEquip","Tidy Bags 3 Reloaded"]  // CharacterSettings.EnabledPlugins — plugins ARE config; they change survivability/bags/death rate and confound tuning if toggled between runs
  } }
```
When the per-class rest formula exists, its parameters serialize into `config` too — so the same CSV tunes *that*.

## Performance
- Per Pulse: one bucket classification + add a `double` (sub-microsecond). Transition checks are field compares.
- Writes buffered in memory, flushed by a 5–10s timer and on `Stop()`. No file IO on the combat path.
- Wrap everything in try/catch; telemetry must **never** be able to crash or stall the bot.

## Build phasing
- **Phase 1 — budget sampler + session summary.** Smallest thing that yields signal: the time breakdown + levels/hour + the config snapshot. Enough to baseline current behavior and then prove the churn/rest fixes work. **Do this first, before the fixes.**
- **Phase 2 — event log.** Fights/deaths/rests with context → per-class/level/zone diagnosis and the overpull signal.
- **Phase 3 — `sessions.csv` rows.** The `(config)→(score)` table that makes optimizer/LLM tuning possible.

## Resolved — engine hooks (verified in source, build-ready)

1. **Events: mostly poll, don't trust the event bus.**
   - `BotEvents.Player.OnLevelUp` **fires** (driven each pulse by `Player.CheckLevelChange`) → safe to subscribe for `levelup`.
   - ⚠️ `BotEvents.Player.OnMobKilled` and `OnPlayerDied` are **dead wiring** — `RaiseMobKilled`/`RaisePlayerDied` are *defined but never called* anywhere. Subscribers (`GameStats`, `BotPoi`, `ToastNotifier`, `Mount`) exist but never fire. **Do not use them.** Detect kills and deaths by **polling transitions** in `Tick`: death = `Me.IsDead` false→true (and `IsGhost` for corpse run); kill = `CurrentTarget` alive→dead, corroborated by XP gain / combat exit.
   - No combat enter/exit event → poll `Me.Combat`.
2. **XP: use descriptors, table-free.** `Me.Experience` (`CurrentXP`) and `Me.NextLevelExperience` (`XPToNextLevel`) read the client descriptors correctly. `progress = Me.Level + Experience / NextLevelExperience`, recomputed each sample — level-up resets are handled automatically (Experience drops, NextLevelExperience changes, the integer Level ticks up).
3. **Overpull signal:** `Targeting.Instance.TargetList.Count` is the cheap proxy (the targeting candidate set of nearby hostiles). For precision in Phase 2, radius-scan `ObjectManager` hostiles that are aggroed on me/pet. Start with the count.
4. **Durability:** `Me.DurabilityPercent` and `Me.LowestDurabilityPercent` (both 0.0–1.0). Sample pre-death and post-resurrect for the loss; fixed-10% is a fallback.
5. **Idle-recover `Resting` bucket:** `!Me.Combat && (Me.HasAura("Food") || Me.HasAura("Drink") || (!Me.IsMoving && belowRecoverCeiling && BotPoi.Current.Type ∉ {Sell,Repair,Buy,Train,Mail,QuestPickUp,QuestTurnIn}))`. The last clause captures the costly no-consumable wait without misclassifying travel pauses.

### ⚠️ Do NOT reuse `GameStats` for session totals
`GameStats` already computes `XPPerHour` / `Deaths` / `DeathsPerHour` / `TimeToLevel` and looks tempting — but it **`Reset()`s on every `OnBotStart`**, and this bot stop/starts `TreeRoot` constantly (72× in the 41-min baseline run). So its counters are per-restart-segment, not per-session, and `MobsKilled` is broken anyway (depends on the dead `OnMobKilled`). **Anchor our telemetry to app-session start, independent of `TreeRoot` start/stop.** We may read `GameStats.XPPerHour` as a loose cross-check once the churn fix makes restarts rare, but it is not our source of truth.
