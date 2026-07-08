# Party Bot — Quest & Loot Coordination (Design Spec)

**Status:** design only — NOT built. Phased build plan at the bottom; build + live-test one phase
at a time (there are no unit tests — test = build → deploy DLL → relaunch CB → read `Logs\`).

This captures a long design session. Party Bot = `DiscoBot` (built-in botbase, compiled into
`CopilotBuddy.dll` → any change here needs a DLL rebuild + redeploy + CB restart). IPC lives in
`Styx\PartyBot\IPC\`. Loot rolling is owned by the AutoEquip2 plugin.

---

## 1. What exists today (baseline)

- **Follow / command broadcast.** `LeaderPlugin` runs on the leader, pushes a `BotMessage`
  (Kill / Vendor / FollowLeader + leader pos/target) over a **one-way** TCP hub (`RemotingServer`
  on loopback :1337, write-only). Followers run `DiscoBot` as `RemotingClient`s and react.
- **Leader mode checkbox** (just added): `PartyBotSettings.IsLeader` auto-enables `LeaderPlugin`
  and idles the bot. Follow is native `/follow` by name + mesh-nav catch-up.
- **Quest accept, partial.** `DiscoBot.OnQuestDetail` already auto-accepts shared quests when
  `AutoAcceptSharedQuests` is set (`QUEST_DETAIL` → `AcceptQuest()`).
- **Loot, corpses.** `DiscoBot.CreateLootBehavior` → LevelBot loot + `LootTargeting`.
- **Gear rolls.** AutoEquip2 hooks `START_LOOT_ROLL` and rolls Need on upgrades / Greed / DE.
  (LeaderPlugin's blind auto-greed is now gated OFF behind `LeaderAutoRollGreed`.)
- **Turn-in / pickup primitives.** `QuestFrame` (`AcceptQuest`/`CompleteQuest`/`SelectQuestReward`/
  `ClickContinue`) and `Bots\Quest\QuestOrder\ForcedQuestTurnIn.cs` / `ForcedQuestPickUp.cs`.

---

## 2. Invariants (these are load-bearing — violating any is how it wedges)

1. **Leader is the coordination hub; followers announce, leader arbitrates.** Discovery is
   *local* — a follower sees objects next to it that the leader can't — so interest originates at
   the edge and is adjudicated at the center. (This is why follower-initiated beats leader-scan.)
2. **Safety is follower-local and survives coordinator outage.** The "never pull" gate is checked
   by each follower against its own surroundings and does NOT depend on the broker. The broker
   decides *who* gets a resource, never *whether it's safe to go get it*.
3. **Fail degraded, never fail closed.** If the leader goes quiet (DC/dead/zoning/crash), claims
   go stale and followers fall back to safe-local-FFA. They never freeze waiting for grants.
4. **Don't assume the favorable case.** Not always group loot (FFA has no server round-robin/rolls).
   Not always a free leader tick (human-driven leader is idle, but a *botted* leader is busy). The
   design must not bake in either.
5. **Everything that waits or loops is bounded, and the collider is logged.** Orphaned leases,
   over-farm, redo-loops, despawn races — every one has an explicit timeout/cap.

---

## 3. Mode decision: MIRROR (locked for this design)

- **Mirror (chosen):** followers follow the leader and *replicate* its quest actions; native party
  mechanics (shared kill credit, per-player loot) keep objectives roughly in sync. Tight multibox
  feel. Cost: needs the progress back-channel + party-min gating (§5) so it doesn't stall at turn-in.
- **Independent (rejected):** followers run the Quest botbase on the same profile; desync self-heals
  but you lose tight follow and bots path-diverge. Revisit only if mirror proves too fragile.

**Prerequisite:** mirror mode requires a **real party** (grouped), not just TCP-follow — quest
sharing and shared kill credit are server-side party features. Quest mode auto-enables grouping
(`AcceptGroupInvitesFromLeader`).

---

## 4. IPC changes (Phase 0 groundwork)

- **Make the hub bidirectional.** `RemotingServer.ServeClient` is currently write-only. Add a read
  path so followers can send upstream (progress reports, loot interest/claims, heartbeats, acks).
- **Extend `BotMessage`.** It's a string-discriminated message with public fields; the TCP path
  serializes fields via `IncludeFields`, so new public fields "just work" on the wire — BUT keep the
  legacy `ISerializable` `GetObjectData`/ctor in sync. New content: `QuestId`, `ObjectiveIndex`,
  `Have`/`Need`, resource-claim fields, and new `Message` types (`ShareQuest`, `TurnInQuest`,
  `Claim`, `Grant`, `Heartbeat`, `Release`, `Progress`).

---

## 5. Quest sync

- **Accept.** Leader hooks `QUEST_ACCEPTED` → select quest → `QuestLogPushQuest()` (native share,
  shareable quests only). Followers auto-accept: existing `QUEST_DETAIL` handler PLUS add
  `QUEST_ACCEPT_CONFIRM` → `ConfirmAcceptQuest()` (shared quests raise the confirm popup, distinct
  from QUEST_DETAIL).
- **Kill objectives.** Shared credit is automatic for grouped players in range; followers already
  assist via the Kill broadcast. Near-free.
- **Collect / quest-object objectives.** Handled by the loot loop (§6).
- **Turn-in.** Leader broadcasts `TurnInQuest(npcGuid, questId)`; followers (already at the NPC via
  follow) reuse `ForcedQuestTurnIn` logic. Reward choice needs a policy: pick by vendor value or
  defer to AutoEquip2's scorer. **Gate:** turn-in only fires once every *live* grouped member's
  objective is complete (see party-min) — otherwise the party desyncs into a stuck state.
- **Progress back-channel (the piece that makes mirror mode not stall).** Because quest progress is
  private per-client, the leader *cannot read* a follower's `9/12`. Followers self-report objective
  counts on `QUEST_LOG_UPDATE`/`UNIT_QUEST_LOG_CHANGED` up the (now bidirectional) hub. Leader
  aggregates a `PartyQuestProgress` table and the completion predicate becomes:
  **`done = min(Have) over {self ∪ live followers} >= Need`.** That single change is "farm more until
  the laggard catches up." Bounds:
  - **Liveness timeout:** a member that hasn't reported in N s drops out of the min (log it) — one
    dead client must not wedge the leader forever.
  - **Over-farm only helps a present member:** keep farming while a laggard is *live AND in credit
    range*; if it's present-but-not-progressing, pause for it; if absent/dead/stuck past a bound,
    turn in for whoever can and move on. Never an unbounded "wait for the party" loop.
  - Don't hardcode "leader last." Use **lowest-current-count-first** assignment — the leader, present
    for everything, is usually most complete, so it defers *emergently*, and self-corrects the moment
    it becomes the laggard. (Hardcoded "leader last" causes bottleneck inversion: everyone waits on
    the leader who kept yielding.)

---

## 6. Loot coordination

### 6a. Corpses — one claim→loot→release→redo loop, method-agnostic

The bot that gets a corpse is the one granted the claim; after it loots, if the corpse is **still
lootable *for another bot***, that bot re-claims and loots. Repeat until nobody sees loot on it.

- **"Still available" = each bot's OWN per-player lootable flag, NOT a global check.** The leader
  can't see whether follower B still has a per-player quest item on the corpse. So the redo signal
  must come from **followers re-announcing interest while they personally still see loot.** The
  corpse is "done" when no one is announcing.
- **No per-slot logic needed.** Each claimant auto-loots whatever the server shows *it* — quest items
  (per-player, always) + shared trash (only for whoever the server/first-claim gives it). The server
  already filters each bot's view. A bot without the quest never sees the item → never claims
  (auto-excluded).
- **Same loop under both loot methods (no branch).** Group loot: server pre-limits each bot's view
  (few redos). FFA: first claimant gets the shared trash, everyone else redoes for their quest items.
- **Per-corpse serialization.** One looter at a time *per corpse GUID* (a short-lived mutex, released
  on done). This is a **correctness** requirement in FFA (two bots opening one corpse race on the
  shared trash and can desync the loot window); in group loot it's just orderliness. **Parallelize
  across *different* corpses** — four bots on four corpses at once. Serialize only on GUID collision.
- **Failure cap (anti-wedge).** A bot that sees a corpse as lootable but can't clear it (bag full,
  declined BoP-confirm) will re-announce forever. Cap per (bot, corpse): after N fails, blacklist
  that corpse for that bot and log the reason. Bag-full → route to vendor/mail, don't thrash.
- **FFA trash distribution is a claim-*order* policy, not a mechanism.** Rotate first-claim across
  corpses → round-robin trash; don't → first-come/pooled. Deferrable; default first-come/pooled.

### 6b. World objects (ground nodes, chests) — the real lease broker

These aren't governed by party loot rules (a ground spawn despawns for whoever grabs it — no roll,
no round-robin), so the software layer must arbitrate. This is where the full protocol lives:

- **Lease with heartbeat.** Follower announces interest → leader grants a provisional exclusive
  lease → holder renews by heartbeat ("still alive + still intend it", decoupled from being
  interrupted by combat en route) → no heartbeat for TTL → leader revokes and reassigns. Explicit
  **release** on success is the fast path; TTL is only the crash safety net. **Leader is the sole
  timeout authority.**
- **Grant ≠ success.** The world can beat the grant (despawn, another player, a fumbled interact).
  Grants are advisory; the holder handles "granted but gone" and releases cleanly.
- **Orphaned-lease guard** = the TTL. A holder that crashes must not lock the object forever.
- **Assignment = lowest-count-first** (need-based; produces lead-by-example, §5).
- **Chests / locked objects:** the **rogue self-selects** — checks its *own* Lockpicking skill vs the
  lock and claims if capable (no central skill registry); leader just routes the party/rogue there.
  Generalizes to key-holders.

### 6c. One generic broker, many resource types

Loot corpses, quest ground-spawns, chests, lockpick assignment, (later) CC targets are all **clients
of one claim/lease broker keyed by `(resourceGuid, purpose)`** with the same lease/TTL/ack/reassign
semantics. Build the primitive once. It lives in the **botbase** (coupled to loot/target/quest state;
keeps `LeaderPlugin` thin) — every member *can* coordinate, but we do NOT build live leader election
(see §8).

---

## 7. Safety gate (the "never accidentally pull" invariant)

Combat-first is already structural — `DiscoBot.Root` orders death → combat → events → loot → follow,
and `IsInCombatState()` folds in party-member combat. The real pull risk is the **walk to** a
detached lootable, not the looting. So a lootable is **eligible only if:**

1. Inside the **cleared bubble** — no live hostile within aggro range of the object (and ideally the
   path). Corpses pass trivially; forward objects past an uncleared pack are simply not eligible yet.
2. Within the **leader tether** — bounded distance from the follow position; no solo cross-room runs.
3. **Nav-reachable cheaply** — straight-line prefilter first, then `GeneratePath` only on survivors,
   cache per GUID. Reject by **detour ratio** (path ÷ straight-line — catches "10 yd away, 60 around")
   + an absolute cap expressed **relative to the tether**, NOT a magic yard constant.
4. **Combat preempts mid-action** — cancel a loot/interact channel and drop to combat next tick.

The gate lives *upstream of assignment*: the broker only ever offers jobs that already pass it, and it
**stays enforced even in the FFA-degraded fallback** (it's follower-local). Arbitration may fail open;
safety may not.

**Named/unique quest mobs:** the binding constraint is the *loot*, not the (shared, instant) kill
credit. A lagging bot that misses the corpse before despawn loses its head on a long-respawn named.
→ **Pull-timing invariant on the leader: don't kill the named until every live quester is stacked in
loot range.** Once the corpse despawns, no re-claim brings it back.

---

## 8. Explicitly NOT doing (and why)

- **Live leader election / failover for loot.** Detection (dead vs slow → split-brain, two brokers
  granting the same node), state transfer — all to marginally improve loot fairness during an outage
  the **FFA-fallback already covers**. And true leader-down continuity (follow anchor, pull decisions)
  is a whole-run problem; with a *human* leader the answer is "the run pauses," not "a bot seizes
  command." The *capability* is free (code in the botbase); we just don't exercise it as election.
- **Load-shedding delegation** (run the broker on a calmer member). Unnecessary for a human-driven
  leader (idle tick, §Invariant 4). Revisit only if a *botted* leader shows real broker latency.
- **Per-slot FFA loot selection.** Superseded by the redo-claim loop (§6a) — simpler and correct.

---

## 9. Wedge / failure catalog (each must have its bound)

| Failure | Bound |
| --- | --- |
| Orphaned lease (holder crashes holding an object) | lease TTL → revoke + reassign |
| Grant ≠ success (world beat the grant) | advisory grant; holder handles "gone", releases |
| Over-farm forever (laggard out of range/dead) | farm only while laggard live+in-range; pause/give-up bounds |
| Dead follower poisons party-min | liveness timeout drops it from the min |
| Redo-loop spins (bag full / declined) | per-(bot,corpse) attempt cap + blacklist; bag-full→vendor |
| Despawn race on named quest mob | leader pull-timing: stack all questers before the kill |
| Split-brain | avoided by NOT doing leader election |
| Broker unreachable (leader DC/dead) | stale-timestamp → safe-local-FFA, safety gate still on |

---

## 10. Phased build order (build + live-test each before the next)

- **Phase 0 — groundwork.** Bidirectional IPC read path; `BotMessage` extension (+ ISerializable
  sync); grouping-prereq wiring. No user-visible behavior yet.
- **Phase 1 — quest accept sharing.** Leader auto-share on `QUEST_ACCEPTED`; follower
  `QUEST_ACCEPT_CONFIRM` auto-confirm. Small, self-contained (no IPC needed), immediately visible
  in-game. **Good first slice.**
- **Phase 2 — turn-in.** Leader `TurnInQuest` broadcast; followers reuse `ForcedQuestTurnIn` + reward
  policy. First WITHOUT party-min gating (simplest), then add the gate.
- **Phase 3 — progress back-channel.** Follower progress reports; `PartyQuestProgress` party-min
  predicate; liveness timeout + over-farm bounds.
- **Phase 4 — corpse loot loop.** Claim→loot→release→redo (§6a) + the safety gate (§7).
- **Phase 5 — world-object lease broker.** Nodes/chests (§6b) + rogue lockpick; generalize to the
  keyed broker (§6c).

---

## 11. Open decisions (defaults chosen; confirm or override)

1. **Assumed loot method:** default **Group Loot** (server does corpse round-robin + gear rolls via
   AutoEquip2); FFA fully supported via the redo-claim loop. — *default: Group Loot.*
2. **FFA trash distribution:** **first-come / pooled** (defer round-robin; it's a claim-order knob). 
3. **Mode:** **mirror** (locked unless you want independent).
4. **Reward-choice policy on turn-in:** defer to **AutoEquip2 scorer**, fall back to vendor value.
