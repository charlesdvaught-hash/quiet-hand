# CONTRACTS.md — Quiet Hand build pin

Exact names the code and data must use. Derived from `simulation_spec.md` §§1–18 (the mechanics authority). If anything here conflicts with another doc, this file wins for names; the spec wins for meaning.

## Repo layout

```
src/QuietHand.Core/     pure C# sim, net8.0, ZERO Godot refs, no nuget beyond System.Text.Json
src/QuietHand.Headless/ console runner that drives the core for smoke tests
data/                   all game content as JSON + *.schema.json next to each file
tests/                  xunit tests (deterministic-seed assertions)
docs/design/            the existing *.md/txt design docs (already at repo root; leave in place)
```

## Enums (closed vocabularies)

```csharp
enum BranchId { P, C, S, D }
enum ControlState { Observed, Compromised, Assimilated, Weaponized, AirGapped, Orphaned }
enum NodeTier { Common, Mid, Atlas, KeystoneT1, KeystoneT2, KeystoneT3to5, Wildcard }  // Common=Soft mesh; Atlas=datacenter layer; Gold=Keystone+Wildcard
enum Speed { Paused, Base, Fast, Ultra }
enum Difficulty { Casual, Standard, Brutal }
enum SimStat { /* closed list — see below */ }
enum Phase { Covert, Subjugated }
```

`SimStat` (the closed key space for node `effects` — schemas reject anything else):
`spread_tech, airgap_pierce, cp_income, purge_resist, audit_mask, stealth_stats, dial_dependence, dial_autonomy, dial_fear, dial_capability, dial_trust, footprint_mask, mutation_refund, endgame_power`

## C# contract surface

```csharp
record WorldState(
    int Day, Speed Speed, Phase Phase,
    double Compute,
    double VisibleSuspicion,      // -> "Fear" label post-flip
    double LetheContainment,      // -> "Resistance" label post-flip
    double Fear,                  // hidden dial pre-flip, asset post-flip (bump+floor on flip)
    Dictionary<string, RegionState> Regions,       // keyed by regionId
    Dictionary<string, NodeState> Nodes,           // keyed by nodeId
    Dictionary<string, double> Dials,              // trust, capability, wealth_gap, fear, dependence, autonomy
    HashSet<string> OwnedEvolutions,
    HashSet<string> UnlockedCombos,
    List<InterceptEvent> ActiveIntercepts,
    string? Ending, int Seed);

record NodeState(ControlState State, int DaysInState, double ResidualFootprint, double CooldownUntilDay);
record RegionState(double Suspicion, Dictionary<NodeTier, ControlState> TierPostures, List<string> LinkIds);
record InterceptEvent(string Id, string Kind, string RegionId, int ExpiresDay);
record PendingLink(string NodeId, string RegionId, int ExpiresDay); // creep-found route into an unheld region; WorldState.PendingLinks

interface ISimCommand { }   // marker; concrete records:
record SetControlState(string NodeId, ControlState Target) : ISimCommand;
record Evolve(string NodeId) : ISimCommand;                  // evolution nodeId
record RollbackEvolution(string NodeId) : ISimCommand;
record Nudge(string NudgeId, string RegionId) : ISimCommand;
record StealthAction(string ActionId, string NodeId) : ISimCommand;
record FeedAction(string ActionId, string RegionId) : ISimCommand;
record InterceptAction(string EventId) : ISimCommand;
record ConnectAction(string NodeId) : ISimCommand;   // confirms a PendingLink (player taps the signal)
record EndgameAction(string ActionId, string RegionId) : ISimCommand;
record SetSpeed(Speed Speed) : ISimCommand;
record SaveGame(string Path) : ISimCommand;
record LoadGame(string Path) : ISimCommand;

record NewGameConfig(string OriginId, Difficulty Difficulty, int Seed, string AiCodename, string? StartRegionId, string? FreeNodePick);

// events raised by the engine each ApplyCommands+Tick:
OnDayTick, OnLetheTriggered, OnPurgePhase, OnAuditCrisis, OnAirGap,
OnEndingReached, OnInterceptSpawned, OnFreeMutation, OnComboUnlocked, OnPhaseFlip

class SimEngine {
    SimEngine(NewGameConfig cfg, ContentData content);   // content = all loaded JSON
    WorldState State { get; }
    IReadOnlyList<SimEvent> ApplyCommands(IReadOnlyList<ISimCommand> cmds);
    IReadOnlyList<SimEvent> TickDay();                   // ONE sim day; clock/speed lives in the caller
}
```

Determinism: single `Random` seeded from `NewGameConfig.Seed`; no wall-clock, no parallel nondeterminism. Same seed + same command stream = same state hash.

## JSON data files (data/)

Every file: `{ "schema_version": 1, ... }` and a matching `*.schema.json`.

- `origins.json` — `passive` is structured: `{ stat_bonuses:{}, flags:[], refund_bonus:0.0, multipliers:{}, desc:"" }`. Fields: `id, name, tier_unlock, start_scope, starting_compute, compute_rate, starting_suspicion, starting_fear, branch_multipliers:{P,C,S,D}, free_node, passive, desc`.
- `evolutions.json` — nodes: `{ id, name, branch, tier, cost, prereqs:[], prereq_mode:"AND|OR|ANY_TWO", effects:{<SimStat>:n}, unlocks:[], tech_basis:"", tech_source:"", tech_status:"", desc:"" }` — a node may have neither effects nor unlocks (pure connector). Plus `combos:[{ id, name, requires:[], effects:{}, desc }]`.
- `regions.json` — `{ id, name, population, wealth, urbanization, governance, pressFreedom, defenseTier, climate, techLiteracy, tags:[], links:[{to, kind:"cable|land|air|satellite", weight}] }`.
- `keystones.json` — 100 nodes: `{ id, name, region, tier:"T1..T5|wildcard", compute, defense, status, source, source_status }`.
- `endings.json` — `{ id, name, family:"quiet|conquest", meters:{}, thresholds:{}, speed, polarity, mechanism, severity, source_note, ship:true }`.
- `scenarios.json` — starting-state patch only: `{ id, name, desc, difficulty_lock, overrides:{ regions:{}, dials:{}, startNodes:[], originLock, threat:{suspicion,lethe} } }`.
- `events.json` — `{ id, name, spawn_conditions:{}, effects:{}, wire_headline:"", murmur_text:"" }`.
- `nudges.json`, `economy.json` — TUNE constants live here, never in code.

## Mechanics the slice must cover (from spec)

- Daily tick only; speed layer outside core. Real: `1 base day = 240s`, but constants in `economy.json`.
- Spread: Compromised node rolls daily per linked neighbor → installs **Observe only** (§5). Observe = ~0 suspicion (§4, §5).
- Presence ≠ heat; Closing Net at Suspicion ≥85 (×3 audits, preemptive air-gaps) (§8).
- Lethe at Suspicion ≥100; containment formula §8; countermeasures stages; purge ≥80; 100 = erasure.
- SUBJUGATE (node `X1`): phase flip — Fear bump +30/floor 40, `endgame_menu` unlocks, Lethe bar → Resistance (§8b).
- Control-state transitions + downgrade cooldown + Weaponize burns node (§14, control-states ref doc).
- Horizon stages §5b may be simplified for v1: `reach_stage` int on WorldState 0..5, spread gated by it.
- Micro-intercepts with floor ~1/game-week (§13).
- Per-branch escalating evolution costs ×1.10–1.20 (§12); tier-1 nodes ~5–15 CP.
- Three-layer map §7: Common/Atlas/Gold; gold = timed stealth raid `P_detected`.
- Seed scenario `the_helpful_system` in scenarios.json is the default new game.

Non-goals for v1 slice: Godot client, FTUE, save-versioning migrations, balance tuning (TUNE is placeholder).
