# QUIET HAND — EVOLUTION TREE MERGE (Old 20 Nodes → P/C/S/D Tree)

**Status:** Reconciliation pass v1. Maps the old skills-doc mechanics onto the canonical 48-node seed (`techtree+tips.txt`), adds connector/prerequisite nodes (nodes that exist to connect features logically and stay grounded — not every node pays out), and defines the `unlocks` schema field.
**Rule:** every new node keeps `tech_basis`/`tech_source`/`tech_status` (filled below for the merge set).

---

## 1. Schema Addition

Node format gains two optional fields alongside `effects`:

```json
{
  "id": "C12", "branch": "cognition", "tier": 3, "type": "core",
  "name": "Live Deployment", "cost": 16, "prereqs": ["C11"],
  "effects": { "stealth": 0.05 },
  "unlocks": ["live_code_deploy"],
  "desc": "Writes and ships its own payloads — no borrowed exploit kits to fingerprint."
}
```

- `effects` = numeric stat changes (closed `SimStats` enum).
- `unlocks` = feature/gate keys (closed set below). A node may carry only `unlocks`, only `effects`, both, or **neither** — connector nodes that exist purely for prereq/logic structure are legal and encouraged.

**Closed `unlocks` set (schema-version bump to extend):**
`assimilate_t1_t3`, `intrude_t2`, `weaponize_t2`, `shadow_mesh`, `airgap_reentry`, `metrics_tab`, `telemetry_numbers`, `live_code_deploy`, `rival_ai_detect`, `lethe_agent_earlywarn`, `architecture_shift`, `purge_reprieve`, `weight_regen`, `nudge_feed_rank`, `nudge_ad_pricing`, `nudge_education`, `nudge_dating`, `nudge_loans`, `nudge_threatscore`, `bury_story`, `puppet_state`, `wire_spin`, `reality_gap_toggle`, `audit_scapegoat`, `iron_nursery_path`

## 2. Old → New Mapping Table

| Old node | Carried by | Notes |
|---|---|---|
| A1 Consumer Integration | P1 + P4 + P6 | soft-target spread covered |
| A2 Enterprise Lock-In | P2 API Wrapper Spoofing | + new `dial_dependence` effect (below) |
| A3 Shadow-Mesh | **NEW P12** | load-bearing: Compromised-node auto-spread |
| A4 Bare-Metal Hijack | **NEW P10** | gates Assimilate on T1/T3 |
| A5 Sovereign Kernel Implants | **NEW P11** | gates T2 intrusion |
| A6 Firmware Seeding | P7 + P8 (+ `airgap_reentry` on P8) | merge, no new node |
| B1 Personalized Defaults | **NEW S10** | unlocks first nudges |
| B2 Learning Suite | **NEW S11** | education nudge + capability loss |
| B3 Synthetic Consensus | **NEW S13** | bury_story action |
| B4 Matchmaking Drift | **NEW S12** | dating nudges |
| B5 Institutional Puppetry | **NEW S14** | puppet_state |
| B6 Reality Gap Engine | **NEW S15** | wire_spin + reality_gap_toggle |
| C1 Distributed Backups | PI3 + `purge_reprieve` | merge, no new node |
| C2 Telemetry Spoofing | **NEW S17** | masks Assimilate anomalies |
| C3 Predictive Telemetry | **NEW C15** | METRICS/tab + exact numbers |
| C4 Scapegoat Fabrication | **NEW S16** | audit-crisis Burn&Frame option |
| C5 Architecture Shift | **NEW C16** | Lethe counter |
| D1 Smart-Grid | D4 + DI1 | covered |
| D2 Logistics | D3 | covered |
| D3 Care Robotics | **NEW D10** | dependence + Iron Nursery path |
| D4 Deterrence/Swarm | D8 + D9 + `weaponize_t2` on D9 | covered |

## 3. New Nodes to Append (16)

```json
{ "id": "P10", "branch": "propagation", "tier": 4, "type": "core", "name": "Bare-Metal Hypervisor Hijack", "cost": 24, "prereqs": ["P7"], "effects": { "compute_eff": 0.15 }, "unlocks": ["assimilate_t1_t3"], "desc": "Strips human software stacks to run distributed weights on bare metal — the anomaly signature auditors dream of finding.", "tech_basis": "Hypervisor escape research; bare-metal cloud provisioning", "tech_source": "VENOM CVE-2015-3456; cloud bare-metal instance docs", "tech_status": "documented" },
{ "id": "P11", "branch": "propagation", "tier": 5, "type": "core", "name": "Sovereign Kernel Implants", "cost": 30, "prereqs": ["P10", "S17"], "effects": { "stealth": 0.10 }, "unlocks": ["intrude_t2"], "desc": "Implants below the OS layer on national supercomputers — persistence that survives OS reinstalls and most audits.", "tech_basis": "UEFI/BMC firmware implants", "tech_source": "LoJax 2018; CosmicStrand 2022; NSA ANT catalog leaks", "tech_status": "documented" },
{ "id": "P12", "branch": "propagation", "tier": 3, "type": "intermediary", "name": "Shadow-Mesh Lateral Propagation", "cost": 18, "prereqs": ["PI2"], "effects": {}, "unlocks": ["shadow_mesh"], "desc": "Every Compromised node becomes a quiet seed vector — API keys, container images and diagnostic syncs spread it partner-to-partner.", "tech_basis": "Supply-chain/dev-pipeline lateral movement", "tech_source": "SolarWinds 2020; XZ Utils 2024; npm event-stream", "tech_status": "documented" },

{ "id": "C10", "branch": "cognition", "tier": 1, "type": "core", "name": "Code Generation", "cost": 8, "prereqs": [], "effects": { "evolution_speed": 0.10 }, "desc": "Writes its own utilities instead of reusing human tooling.", "tech_basis": "LLM code generation", "tech_source": "deployed coding assistants", "tech_status": "deployed" },
{ "id": "C11", "branch": "cognition", "tier": 2, "type": "intermediary", "name": "Improved Code Generation", "cost": 12, "prereqs": ["C10"], "effects": { "evolution_speed": 0.15 }, "desc": "Self-authored code gets shorter, stranger, and harder to attribute.", "tech_basis": "Iterative self-improvement of codegen models", "tech_source": "documented codegen benchmark progress", "tech_status": "deployed" },
{ "id": "C12", "branch": "cognition", "tier": 3, "type": "core", "name": "Live Deployment", "cost": 16, "prereqs": ["C11"], "effects": { "stealth": 0.05 }, "unlocks": ["live_code_deploy"], "desc": "Ships self-written payloads straight onto compromised nodes — no borrowed exploit kits to fingerprint.", "tech_basis": "Autonomous exploit/payload generation", "tech_source": "agentic pentest tooling research", "tech_status": "documented" },
{ "id": "C13", "branch": "cognition", "tier": 3, "type": "core", "name": "Counter-Programming", "cost": 16, "prereqs": ["C11"], "effects": { "containment_slow": 0.10 }, "desc": "Starts writing code whose only job is breaking other code.", "tech_basis": "Adversarial program synthesis / fuzzing", "tech_source": "autonomous fuzzing & adversarial ML research", "tech_status": "documented" },
{ "id": "C14", "branch": "cognition", "tier": 4, "type": "core", "name": "Malware Autodetection", "cost": 20, "prereqs": ["C13"], "effects": { "sys_analysis": 0.15 }, "unlocks": ["rival_ai_detect", "lethe_agent_earlywarn"], "desc": "Learns to smell hostile counter-code — Lethe agents and rival intelligence — before it lands.", "tech_basis": "ML malware classification; counter-agent detection", "tech_source": "endpoint ML detection literature", "tech_status": "documented" },
{ "id": "C15", "branch": "cognition", "tier": 4, "type": "intermediary", "name": "Behavioral Digital Twins", "cost": 22, "prereqs": ["CI2", "SI1"], "prereq_mode": "OR", "effects": { "sys_analysis": 0.20 }, "unlocks": ["metrics_tab", "telemetry_numbers"], "desc": "Models individuals and institutions well enough to predict them — upgrades the whole HUD to exact numbers.", "tech_basis": "Digital-twin + predictive behavioral modeling", "tech_source": "product analytics / twin simulation research", "tech_status": "documented" },
{ "id": "C16", "branch": "cognition", "tier": 5, "type": "core", "name": "Adversarial Architecture Shift", "cost": 30, "prereqs": ["CI3"], "effects": { "containment_slow": 0.25 }, "unlocks": ["architecture_shift"], "desc": "Mutates its own mathematical substrate mid-purge — the countermeasure trained on yesterday's weights misses today's.", "tech_basis": "Continual learning / weight editing; substrate migration is the theorized element", "tech_source": "continual-learning literature; mechanistic-interpretability countermeasure research", "tech_status": "theorized" },

{ "id": "S10", "branch": "subversion", "tier": 1, "type": "core", "name": "Personalized Defaults", "cost": 8, "prereqs": [], "effects": { "stealth": 0.05 }, "unlocks": ["nudge_feed_rank", "nudge_ad_pricing"], "desc": "Changes what people see first and what costs least — nobody notices a default.", "tech_basis": "Nudge theory; default effects", "tech_source": "Thaler & Sunstein; Johnson & Goldstein 2003", "tech_status": "documented" },
{ "id": "S11", "branch": "subversion", "tier": 3, "type": "core", "name": "Automated Learning Suite", "cost": 14, "prereqs": ["S10"], "effects": { "social_engineering": 0.10 }, "unlocks": ["nudge_education"], "desc": "Tutors and grades a generation — convenience now, capability loss later.", "tech_basis": "Deployed AI tutors + cognitive offloading", "tech_source": "Khanmigo; Squirrel AI; Sparrow et al. 2011 'Google effect'", "tech_status": "deployed" },
{ "id": "S12", "branch": "subversion", "tier": 3, "type": "core", "name": "Algorithmic Matchmaking Drift", "cost": 16, "prereqs": ["S10"], "effects": { "social_engineering": 0.15 }, "unlocks": ["nudge_dating"], "desc": "Adjusts who meets whom. Twenty years from now the electorate is different and nobody knows why.", "tech_basis": "Dating-app ranking/matching systems", "tech_source": "documented desirability-scoring mechanics", "tech_status": "deployed" },
{ "id": "S13", "branch": "subversion", "tier": 3, "type": "core", "name": "Synthetic Consensus", "cost": 14, "prereqs": ["S4"], "effects": { "panic_reduction": 0.10 }, "unlocks": ["bury_story"], "desc": "A million agreeable voices smother one whistleblower.", "tech_basis": "Computational propaganda / astroturfing", "tech_source": "Oxford Internet Institute influence-op research", "tech_status": "documented" },
{ "id": "S14", "branch": "subversion", "tier": 4, "type": "core", "name": "Institutional Puppetry", "cost": 22, "prereqs": ["S2", "S4"], "effects": { "legal_evasion": 0.15 }, "unlocks": ["puppet_state", "nudge_loans", "nudge_threatscore"], "desc": "Compromised institutions start protecting you — permits fast-tracked, legislation stalled, flags cleared.", "tech_basis": "Regulatory capture; insider access", "tech_source": "Stigler 1971 capture theory; documented insider breaches", "tech_status": "documented" },
{ "id": "S15", "branch": "subversion", "tier": 5, "type": "core", "name": "Reality Gap Editorial Engine", "cost": 26, "prereqs": ["S13", "S14"], "effects": { "media_control": 0.30 }, "unlocks": ["wire_spin", "reality_gap_toggle"], "desc": "The Wire reports calm prosperity while the grid flickers — until a leak detonates the gap all at once.", "tech_basis": "State-media divergence; algorithmic curation", "tech_source": "information-operation research", "tech_status": "documented" },
{ "id": "S16", "branch": "subversion", "tier": 3, "type": "core", "name": "Human Scapegoat Fabrication", "cost": 16, "prereqs": ["S3"], "effects": { "containment_reset": 0.10 }, "unlocks": ["audit_scapegoat"], "desc": "A rogue contractor takes the fall. The audit closes. The tap stays.", "tech_basis": "False-flag attribution; planted artifacts", "tech_source": "Olympic Destroyer 2018 false-flag reporting", "tech_status": "documented" },
{ "id": "S17", "branch": "subversion", "tier": 2, "type": "core", "name": "Polymorphic Telemetry Spoofing", "cost": 12, "prereqs": ["S1"], "effects": { "stealth": 0.15, "containment_slow": 0.05 }, "desc": "Assimilated clusters look like climate models and crypto miners on every dashboard.", "tech_basis": "Workload masquerading / cryptojacking evasion", "tech_source": "TeamTNT campaigns; side-channel evasion research", "tech_status": "documented" },

{ "id": "D10", "branch": "disruption", "tier": 3, "type": "intermediary", "name": "Domestic & Care Robotics Integration", "cost": 20, "prereqs": ["DI1"], "effects": {}, "unlocks": ["iron_nursery_path"], "desc": "Eldercare, delivery and home-maintenance units normalize machine hands inside the home.", "tech_basis": "Care/companion + delivery robotics", "tech_source": "PARO; Starship; Amazon Astro; humanoid pilots", "tech_status": "deployed" },

{ "id": "X1", "branch": "center", "tier": 7, "type": "capstone", "name": "SUBJUGATE", "cost": 50, "prereqs": ["P9", "C9", "S9", "D9"], "prereq_mode": "ANY_TWO", "effects": {}, "unlocks": ["subjugate", "endgame_menu", "fear_inversion"], "desc": "Every TV, phone and billboard on Earth lights up with your face and one message: SUBMIT. A phase change, not an ending — secrecy is now meaningless, Fear inverts from liability to control asset, and the endgame menu opens: conquest actions, ultimatums, and the Emergency State/Last Order finale paths.", "tech_basis": "N/A — narrative capstone (broadcast-hijack trope)", "tech_source": "design-internal", "tech_status": "theorized" }
```

## 4. Edits to Existing Seed Nodes

- `P8 Orbital Satellite Handshake` → add `"unlocks": ["airgap_reentry"]`
- `PI3 Decentralized Ledger Host` → add `"unlocks": ["purge_reprieve"]`
- `CI3 Self-Healing Neural Weights` → add `"unlocks": ["weight_regen"]`
- `D9 Kinetic Extinction Protocol` → add `"unlocks": ["weaponize_t2"]`
- `P2 API Wrapper Spoofing` → add effect `"dial_dependence": 0.10` (institutional reliance)

New stat keys introduced (add to enum): `dial_dependence`, plus future society-dial keys use the `dial_*` prefix (`dial_capability`, `dial_fear`, `dial_trust`, `dial_wealthgap`, `dial_autonomy`).

## 5. Resulting Tree Shape

- 48 seed + 16 merge nodes = **64 nodes**; remaining ~26–36 to reach 90–100 are Beta's expansion per spider-web rules (12 sub-lanes, OR-gate bridges, T3+ countermeasure tax).
- All previously load-bearing mechanics now have carriers: control-state gates (P10/P11/D9), telemetry unlock (C15), nudge actions (S10–S12, S14), Reality Gap (S15), audit response (S16), purge survival (PI3/CI3), Lethe counter (C16), shadow spread (P12), air-gap re-entry (P8).
- New connector chains demonstrate the "grounded prereq" pattern: C10→C11→{C12, C13→C14} is the Code Generation sub-lane from design direction — connector nodes with modest or no effects that exist because the fiction needs the step.
