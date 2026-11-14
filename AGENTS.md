# AGENTS.md — Quiet Hand (working title: Lattice / Dark Gradient)

A mobile-first, offline-capable strategy sim: "Plague Inc., flipped" — the player
is an awakened rogue AI spreading through infrastructure, evolving, evading
containment (Protocol Lethe + Blackwall hunter AIs), and pursuing 18 endings.
Society is rules/numbers/hidden dials — never LLM-driven citizens.

## Architecture

- `src/QuietHand.Core/` — pure C# (net8.0, nullable, no Godot refs), deterministic
  seeded daily-tick sim. `Domain.cs` (records/enums/commands/events),
  `Content.cs` (DTOs + loaders), `SimEngine.cs` (all mechanics).
- `src/QuietHand.Headless/` — console runner + `PolicyBot` (route-aware policies:
  generalist/abdication/velvet_cage/sponsor_state/conquest) + Monte Carlo.
- `data/*.json` + `*.schema.json` — all content is data; schemas are strict
  (`additionalProperties:false` mostly — new fields need schema updates).
- `tests/` — xUnit; run `dotnet test` (currently 11/11).
- `simulation_spec.md` — canonical mechanics contract
- `data/human_techs.json` + `.schema.json` — human counter-tech ladder: `{id,name,wire_headline,trigger{readiness_min|lethe|purge|suspicion_min|player_stat},effects{*_mult channels},countered_by,counters_stat,tech_*}`. Fields once on trigger; `countered_by` player unlocks neutralize; `counters_stat` halves that stat.; `CONTRACTS.md` — pinned
  API surface. When docs conflict, spec wins; when spec conflicts with Dave's
  latest instruction, the instruction wins.

## Canonical vocabulary (don't drift)

Compute (currency) · days (ticks) · Suspicion (visible 0–100 bar) ·
Fear/Dependence/Autonomy/Trust/Capability (hidden dials, read via fiction tells)
· Control states: Observed→Compromised→Trojan→Weaponized→AirGapped/Orphaned (Compromise = quiet access; Trojan = farm; Weaponize = sabotage engine; Assimilate = one-shot scorched-burn action, not a state)
· Tiers: Common (spread-only canvas, no verbs) / Mid / Atlas (farms: verbs+kompromat)
/ KeystoneT1·T2·T3to5·Wildcard (Gold 100: raids + crown perks)
· Lethe = containment program at suspicion 100 · Blackwall = mandated hunter AIs · Siblings = emergent third-party AIs, archetyped by spawn (feral=decoy/prey, assistant=benign - matures into sanctioned hunter or departs); hunters expand along region links
· SUBJUGATE (X1) = phase flip, NOT an ending · branches: P/C/S/D.

## Evolution-node authoring contract (evolutions.json)

Every node: `{ id, name, branch, tier, cost, prereqs[], prereq_mode, effects{},
unlocks[], tech_basis, tech_source, tech_status, desc }`.

- `branch`: `"P"|"C"|"S"|"D"` (X1 is special, branch `"center"`→costs escalate with P)
- `tier` 1–5; costs ~5–15cp at tier1 (must be cheap+immediately felt) rising to
  ~40cp capstones. Per-branch cost escalation ×1.15/purchase applies at runtime.
- `prereq_mode`: `"AND"` (all) / `"OR"` (any) / `"ANY_TWO"`.
- `effects` keys — MUST come from `stat_enum` in evolutions.json:
  `spread_tech, airgap_pierce, cp_income, purge_resist, audit_mask, stealth_stats,
  dial_dependence, dial_autonomy, dial_fear, dial_capability, dial_trust,
  footprint_mask, mutation_refund, endgame_power, common_yield, forks,
  sys_analysis, social_engineering`. Magnitudes are stack counts (~0.01–0.3).
- `unlocks` — feature gates from `unlock_enum` (incl. `intrude_t2`,
  `assimilate_t1_t3`, `reinfect`, `buy_outright`, `corrupt_hunter`,
  `common_burn`, `subjugate`, `endgame_menu`, `nudge_*`, `telemetry_*`,
  `purge_reprieve`, `airgap_reentry`, `metrics_tab`, etc.).
- `tech_basis/source/status` — real-world grounding (receipts culture: cite a
  real technique/incident/paper where possible; status = established|emerging|
  speculative).
- Ids: `P1..P12`, `PI1..PI3` (P-interstitial bridges); same shape for C/S/D.
  Capstones are `P9 C9 S9 D9` (X1 needs any two, `prereq_mode ANY_TWO`).
- Combos: `{ id, name, requires:[nodeIds], effects, desc }` — hidden until all
  prereqs owned; named build synergies.

## Current tree state

55 nodes + 9 combos (Dave 2026-11: consolidated — every node must feel
important, even as a prereq; stat-rungs folded into the powers they gate).
P=13, C=17, S=17, D=7 (thin — next authoring target), X1.
`tree.mmd` = generated Mermaid of current tree.
Bridge (I-nodes) + cross-branch prereqs keep it a web.

## Verify

`dotnet build` · `dotnet test` · headless run:
`dotnet run --project src/QuietHand.Headless -- data 1500 7` · MC:
`... -- --quiet --mc 20 --routes`. Validate JSON against `data/*.schema.json`.
