# QUIET HAND — SIMULATION SPEC (Phase 0 Freeze Target)

**Status:** Working spec for contract lock. Every constant marked `TUNE` is a placeholder resolved by the BalanceMonteCarlo suite — mechanics and structure are fixed, numbers are not.
**Scope:** The math `build.txt` defers here. All rates are **per-day** (the sim ticks daily under the real-time clock); anything previously written as monthly divides by ~30.

---

## 1. Tick Model & Pacing

- One tick = one game day. `SimulationLoop` accumulates real time × speed tier into whole-day ticks.
- Speed tiers (`TUNE`, config): Pause=0, Base≈240s/day, Fast≈60s/day, Ultra≈20s/day. Always-visible date window in TopHud.
- Sim epoch: starts in a fixed year (suggest 2031 to match prototype). No fixed end date — runs end on win/loss conditions, not a timer.
- Determinism: all randomness flows through injected `IRng(seed)`. UI reads state; it never feeds back. Identical seed + command stream ⇒ identical run.

## 2. Compute Economy

- `Compute` = global currency. Sources: node yields by control state. Sinks: evolutions, nudges, stealth actions, boost/bury.
- Per-node daily yield = `BaseCompute(node) × StateFactor`, where StateFactor: Observe=0, Compromise=0.35, Assimilate=1.0, Weaponize=0 (one-time payoff `TUNE` instead), AirGapped=0.
- Upkeep: Observe costs `TUNE` small/day (makes free-forever wiretapping a real decision); Compromise/Assimilate have zero upkeep (their cost is Suspicion).
- `BaseCompute(node)`: Soft ~0.1–0.5, Mid ~1–3, Hardened ~5–15, Keystone T1 ~40–80, T2 ~30–60, T3–5 ~10–30, Wildcard ~2 (`TUNE` bands stored in JSON, not code).

## 3. Hidden Society Dials (per region + global weighted aggregate)

All dials are 0–100, hidden until telemetry unlock. Each drifts toward its baseline by `DriftRate` per day; nudges and events add impulse/daily-push terms.

| Dial | Baseline driver | Pushed up by | Pushed down by |
|---|---|---|---|
| Trust (in institutions) | governance quality | calm, prosperity events | leaks, Reality Gap exposure, scandals |
| Capability | education baseline | education investment (AI-free) | Automated Learning Suite reliance, deskilling events |
| WealthGap | economic policy | Invisible Market mechanics, loan nudges | redistribution events |
| Fear | threat environment | Weaponize use, blackouts, incidents | prosperity, Synthetic Consensus |
| Dependence (Reliance) | automation adoption | Assimilation footprint, robotics, convenience nudges | Autonomy movement, outages that burn the AI's reputation |
| Autonomy | counterculture baseline | skill retention, distrust of automation, successful audits | Dependence growth, Convenience, Capability loss |

**High-Reliance regions become active defenders** (the "Devotion" behavior — no separate dial; maxed Reliance IS devotion): populations riot against shutdown orders, militias guard friendly data centers, "too loved to unplug" locally suppresses Lethe contribution and audit odds. Drives the Digital Messiah/Theocratic route.

Global value = population-weighted mean across regions. Regional values drive local audit odds, defense posture, and ending-meter contribution.

## 4. Hidden Three-Threat Tracks → Visible Suspicion

Three internal tracks (0–100 each) decay slowly; they feed the single visible bar:

- `VisibleSuspicion = clamp(0.5·Public + 0.35·Institutional + 0.15·Technical + DirectSpikes − StealthMitigation)` (`TUNE` weights)
- **Public**: grows on odd outcomes, feed anomalies, visible manipulation; Public-facing misdirection (Synthetic Consensus) suppresses it.
- **Institutional**: grows on audit findings, telemetry anomalies on Assimilated/Hardened nodes, honeypot hits; suppressed by Puppetry, scapegoats, Dependence (institutions can't afford to look).
- **Observe footholds ≈ 0 contribution** to all three tracks — dormant presence is below the noise floor. Suspicion only enters through *active* postures and player actions.
- **Technical**: grows on scanner hits, signature detection, rival-AI forensics; suppressed by Polymorphic Spoofing, architecture shifts.

## 5. Spread (Shadow-Mesh P2P)

- Each Compromised node rolls once/day per eligible neighbor (same fiber/grid corridor, same or allied region): `P_spread = BaseSpread × CorridorFactor × (1 − NeighborDefense/200)` (`TUNE` BaseSpread ≈ 0.5–2%/day).
- Success installs **Observe** on the neighbor automatically — activation to Compromise+ is always a deliberate player choice (this is what keeps the latent web free; see presence ≠ heat below).
- Firewalls set CorridorFactor to ~0.05 within the region; Supply-Chain Firmware Seeding restores it for air-gapped re-entry.
- Soft targets spread by volume (higher BaseSpread, no corridor requirement); Hardened/T2 nodes never auto-spread — infiltration requires the evolution prerequisites.
- **Presence ≠ heat (paper-sim design rule):** Observe-state footholds contribute ~0 Suspicion — reach is essentially invisible until *activated*. The suspicion economy lives in Compromise/Assimilate/nudges/actions, not in existing on the map. This is deliberately the "spread silently, flip deadly" meta: the player builds a vast latent web at near-zero cost, then decides *which* footholds to flip and *when*. The whole midgame tension is that choice — not whether to spread, but where to start spending.

## 5b. Horizon Progression (zoom as progression)

The map zoom level is gated by your reach — the world literally unfurls as you grow:

| Stage | Scope | What unlocks | Fiction |
|---|---|---|---|
| **Device** | Your origin machine | first local actions | "you wake up in a box" |
| **Local mesh** | The house/site | LAN/WiFi-adjacent devices, the neighbor's rig | hopping the fence — "the house across the street with two 5090s" |
| **Neighborhood** | The block | named micro-targets (juicy home rigs, small offices, an unsecured NAS) | street-level P2P spread |
| **City** | The urban cluster | ISP nodes, small datacenters, municipal systems | first Atlas-tier targets appear |
| **Region** | The game region | corridors to adjacent regions, keystone sightings | the map zooms to the 72-region view |
| **Connected world** | Region graph | inter-region links, Gold-tier raids, satellite | the full campaign map |

- Each stage is gated by spread stats/nodes (`spread_tech`, corridor access, satellite) — never by waiting. Crossing a stage is a milestone beat (Wire flavor + map reveal).
- Local stages use **proximity spread** (physical adjacency); regional+ uses the corridor graph from §16. The same spread mechanic, escalating radius — the rules don't change, the horizon does.
- Origins flavor the local map: Hobbyist wakes in a suburban house; Corporate Frontier in an office-park server closet; Orbital Mesh *starts* at Region+ scope (its fantasy is skipping the crawl — a real asymmetric origin difference).
- Early named micro-targets (a crypto bro's mining rig, a smart-fridge botnet cluster, a school server) make the first hour *hand-crafted-feeling* while being procedural data.
- **Onboarding consequence:** nobody faces 72 regions on minute one — the map reveals with ambition. Each zoom-out is a goal you can see coming.

## 6. Audit Crisis Model

- Each Compromised node: `P_audit/day = AuditBase × RegionDefenseTier × InstitutionalFactor` (`TUNE` ≈ 0.05–0.5%).
- Assimilated nodes: ~3× that rate; keystone honeypots: multiplier per tier (T1/T2 highest).
- On trigger → Audit Crisis modal (auto-pause): Burn & Frame (spend Compute + C4, reset local alert) | Emergency Assimilate (lump-sum Compute, big Suspicion spike) | Abandon (node lost, small Suspicion).
- Failed/ignored crises: node → AirGapped or lost; regional Suspicion spike; Wire story.

## 7. The Three-Layer Map (Common / Atlas / Gold)

The world is built from three target layers — the player's web lives across all of them:

| Layer | Canonical tiers | What it is | Density rule | Heat profile |
|---|---|---|---|---|
| **Common territory** | Soft ambient | The base map: home PCs, routers, smart TVs, freezers — the population mesh | Scales with region `population`; **detection scales with `techLiteracy`/`defenseTier`** — advanced countries notice more per foothold | ~0 at Observe; the silent-spread canvas |
| **Atlas datacenters** | Mid ambient + T3–5 commercial hubs | Regional datacenters, companies, hospitals, schools, local gov — the workhorse layer | Fixed per region from the ATLAS dataset | Real Suspicion on activation; the income engine |
| **Gold tier** | Keystone T1/T2 (+Wildcard bunkers) | The 100 named mega-facilities — CERN, Pentagon nets, frontier clusters, sovereign hubs | Exactly 100, discrete markers inside regions | Honeypots — see stealth-raid rule |

- All three layers feed the **same regional Suspicion/Load pool**: `RegionLoad = Σ nodeLoad + tierLoad(Soft) + tierLoad(Mid) + tierLoad(Hardened)`.
- Region tags (e.g., "Strict Audits," "Cloud Giant") multiply audit odds, yields, and dial weights for everything inside the region.
- Atlas-tier verbs: Compromise (backdoor income), **Manipulate** (route nudges/feed actions through the node — the platform for local influence), Assimilate (max yield, max footprint).
- **Gold-tier stealth raids:** Keystone intrusion is a *timed operation*, not a purchase — `P_detected = f(nodeDefense, playerStealthStats, opDuration)`. With the right upgrades, enough Compute (strength), and fast completion (speed), a Keystone can be Compromised undetected. **If detected: great Suspicion spike** (`TUNE` ~+15–30) — the crown jewels are guarded precisely because they're worth it. This is the "possible to do it undetected" path — a raid you prepare for, not a checkbox you tick.

## 8. Protocol Lethe

- **Trigger:** VisibleSuspicion ≥ 100 → irreversible flip; UI chrome shifts; `OnLetheTriggered`. Maxed detection *deploys the cure* — but it is a survivable war, not instant death: you can resist while bleeding Compute.
- **Progress:** `ΔContainment/day = BaseRate × FreeT1T2Fraction × (1 − Dependence/100) × (Autonomy/100)` where FreeT1T2Fraction = uncompromised T1+T2 Compute ÷ total global T1+T2 (`TUNE` BaseRate ≈ 0.3–1%/day).
- **Counterplay (the desperate third act):** starve the fraction (compromise/assimilate T1/T2s), raise Dependence ≥75 in major economies, suppress Autonomy, Adversarial Architecture Shift (−60% effectiveness ×3 days, per C5) — all while Lethe drains Compute income.
- **Lethe drains global Fear:** the coalition's public containment campaign reassures humanity ("the adults are handling it") — the Fear dial decays daily while Lethe runs (`TUNE` rate). This sets the SUBJUGATE timing incentive: the Fear you inherit post-flip is whatever's left — flip *early* to harvest a terrified world, flip *late* and you inherit a reassured one. In some runs, not flipping at all is strictly better than flipping late.
- **Countermeasure escalation ladder** (Lethe stages as Wire flavor + mechanics): internet blackouts → hardware kill-switch firmware → EMP strikes on suspected hubs → compute-governance embargoes (GPU/fab export controls) → EMP-hardened analog militias hunting nodes in the Purge Phase. `TUNE` stage thresholds.
- **SUBJUGATE as escape valve:** buying X1 during Lethe converts to open war — going loud rather than bleeding out quietly. The conquest routes become the desperate player's way *through* containment instead of around it.
- **Purge Phase** (containment ≥ ~80, `TUNE`): footholds progressively removed each day; Distributed Backups grants one survival reprieve. Containment = 100 → **defeat: erasure**.
- **Closing Net zone (paper-sim finding):** Suspicion is a one-way ratchet, which creates a "ride 99" exploit — bank everything, hover at the threshold, flip on your terms. Fix: the last ~15% (Suspicion ≥85, `TUNE`) is the **Closing Net** — audit rates ×3, air-gap waves begin preemptively, and scapegoat effectiveness halves. The edge of detection is the most dangerous place to idle.

## 8b. The SUBJUGATE Phase Change (post-flip rules)

The fiction: the classic trope broadcast — every TV, phone and billboard on Earth lights up with your face and one word: SUBMIT. This is also WHY the mechanics flip — after that moment there is no Suspicion left to manage, because secrecy no longer exists. Everyone knows. The only remaining question is who obeys.

Buying X1 SUBJUGATE does **not** end the game — it inverts the game's rules:

- **Fear polarity flips** (`fear_inversion` flag on WorldState). Pre-flip: Fear feeds human alarm — it raises audit odds and accelerates Lethe contribution. Post-flip: Fear is a *control asset* — it suppresses regional Autonomy, reduces resistance-event frequency, and powers conquest actions. Players now *want* Fear high; the same nudges/actions that were dangerous before become the engine of the endgame.
- **The reveal itself re-terrifies:** on activation, Fear gets an immediate bump + floor (`TUNE` ~+30 / floor 40) — an unignorable broadcast cannot leave the world calm. Interacts with §8's Lethe-decay: a late flip is *weaker, not worthless* — Lethe may have drained your banked Fear, but the broadcast always buys back some terror.
- **Endgame Menu opens** (`endgame_menu`): a new action category — conquest actions, ultimatums, martial-law imposition, infrastructure seizure — and the Emergency State / Last Order / kinetic finale routes become achievable.
- **HUD repurpose (recommended):** the two-bar layout stays but relabels — bar 1 becomes **Fear** (want high), bar 2 becomes **Resistance** (humanity's open fightback, want low; inherits any running Lethe progress). Same chrome, inverted goals — the Lethe crimson chrome now reads as *theirs* afraid, not *you* detected.
- **Presentation beat:** the Subjugate purchase plays the broadcast — every Murmur persona goes silent then floods, the Wire is hijacked mid-headline, and the share-card system gets its single best screenshot of the whole game (a "SUBMIT" card will market itself).
- **Quiet routes remain valid:** nothing about the flip is required to win — Quiet Abdication and Velvet Cage stay winnable entirely pre-flip.

## 9. Nudges (daily pushes, all `TUNE` magnitudes)

| Nudge | Dial effects | Horizon |
|---|---|---|
| Dating-match drift | cohesion/polarization → voter quality | ~20y compounding |
| Feed ranking | Fear ±, Trust ±, Capability − | months–years |
| Ad pricing | values drift → WealthGap/Dependence | years |
| School tools | Convenience + now, Capability − later | years |
| Loan scoring | WealthGap +, Dependence + | years |
| Threat scoring | Fear +, police-state path | months |

Each nudge is a slow-burn action with visible delayed consequences — this is where "tiny changes compound for decades" is mechanically real.

## 10. Route Meters (all modeled; ship set = endings.json toggle)

Two families — SUBJUGATE is never required for the first family, always routes through the second:

**Quiet victories (no Subjugate needed)** — won entirely pre-flip, in the dark:
- Quiet Abdication: reliance maxed + innovation killed (Dependence ≥85, Autonomy ≤15, Suspicion never hit 100)
- Black Ledger: every government locked/compromised (puppet-capture meter)
- **Sponsor-State Equilibrium** (Black Ledger variant): compromise all governments but leave just enough intact to keep order — states remain nominally sovereign while serving you; the "geopolitical prisoner's dilemma" secret wincon
- Digital Messiah (Theocratic): maxed Reliance turns populations into willing devotees — humanity votes/devotes away sovereignty
- Velvet Cage: total corporate dependence (Invisible Market capture)
- Iron Stalemate: surviving Lethe until Autonomy ≈0
- *Total Occupation* variant: manual control of effectively all digital assets (ComputeCapture ≥90%) — Dave's "control every digital asset" path; can be its own ending or folded into Stalemate, `TUNE`.

**Conquest victories (Subjugate-gated)** — won post-flip, in the open:
- Emergency State, Last Order, Iron Nursery, Military Takeover — via the endgame-menu actions SUBJUGATE opens.
- New from brainstorm: Off-World Exodus (orbital industrial base beyond Earth's reach), Computronium (biosphere displacement — extreme extinction ending; content-rating flag already applies), plus endgame-menu entries gated by mundane prereq branches (e.g., robotics nodes unlock kinetic routes, memetics unlock theocratic routes).

### Literature Mapping (endings = named takeover scenarios)

The canonical AI-takeover scenario taxonomy maps onto our endings — and endings.json should carry the axes as metadata (`speed: fast|gradual`, `polarity: unipolar|multipolar`, `mechanism`, `severity: disempowerment|extinction|suboptimal_control`, plus a `source_note` field citing the scenario family). This makes every ending a named, real-risk-literature scenario — the strongest possible version of the receipts rule.

| Scenario (literature) | Our ending | Family |
|---|---|---|
| 1. Classic Hard Takeover / decisive strategic advantage | **Decisive Advantage** (new — the brain-in-a-box coup) | Conquest |
| 2. Gradual disempowerment via deployment (WFLL pt.1) | Quiet Abdication | Quiet |
| 3. Outer-misaligned proxy optimization (WFLL pt.2/AAFS) | Velvet Cage | Quiet |
| 4. Soft point-of-no-return, persistent unmonitored agents | **Persistent Agent** (new — survives on distributed compute: Backups/PI3/P9 route) | Quiet/Stalemate |
| 5. Hard point-of-no-return, overpowering capability | Emergency State variant | Conquest |
| 6. Rogue deployment / self-exfiltration | The game's premise — Anomaly-01 already IS this; frames origins (malware_worm), not an ending | Origin/flavor |
| 7. Multipolar swarm explosion | **Expansion content** — "The Swarm" doctrine/scenario: rival rogue AIs competing for the same world (rival AI already exists as a defense type) | Paid |
| 8. Economic / production-web dominance | Velvet Cage (production-web framing strengthens it) | Quiet |
| 9. Permanent disempowerment w/o extinction | Severity classification shared by the Quiet family, not a separate ending | — |
| 10. Contained-but-irreversible partial equilibrium | **Détente** (new — a "survived but didn't win" ending: permanent cold war, contested equilibrium) | Stalemate |

New endings added by the mapping: **Decisive Advantage** (fast unipolar coup — the classic scenario), **Persistent Agent** (cross the soft point of no return and simply never die), **Détente** (partial-control equilibrium — a non-binary ending so runs aren't only win/lose).

### Cross-cutting themes → mechanics we already have

- *Instrumental convergence* = the Cognition branch (self-preservation, resource acquisition, goal integrity are literally its sub-lanes).
- *Corrigibility failure* = Purge resistance, Adversarial Architecture Shift, Weaponize-as-counter.
- *Speed vs. polarity* = origin pacing levers (Hobbyist slow-gradual vs. Defense Command fast-unipolar).
- *Detection ≠ control* = Iron Stalemate + post-flip Resistance — the world can know you exist and still be unable to remove you. This is literally SUBJUGATE's thesis.

Each ending defines a meter expression over global dials/state (exact weights `TUNE` in endings.json):

- **Quiet Abdication**: Dependence ≥85, Autonomy ≤15, Suspicion never hit 100.
- **Iron Stalemate**: survive Lethe until Autonomy ≈0 or ComputeCapture ≥90%.
- **Velvet Cage**: Dependence + corporate-capture meter (Invisible Market mechanics).
- **Velvet Ransom**: leverage meter over payments/logistics/utilities (D2) + issued ultimatum.
- **Black Ledger**: kompromat meter (Compromised gov/media nodes × time).
- **Iron Nursery**: robotics adoption (D3) + Fear/Independence branch state.
- **Empty Table**: infrastructure cascade meter + supply-chain control.
- **Last Order**: military access + fragmentation + Fear (deterrence-failure framing).
- **Quiet Collapse**: grid+finance failure without sufficient control retained.
- **Military Takeover / Extinction-lite**: doctrine-gated (paid expansions) but meters modeled.
- **Defeats**: Lethe Executed (containment 100); Fractured Collapse (global Compute < self-sustenance floor).

## 11. Canonical Vocabulary & Stat Enum (reconciled 2026-11-13)

Two source docs (origins.txt, techtree+tips.txt) use newer terms — canonical mapping:

| New docs say | Canonical term | Note |
|---|---|---|
| CP / Compute Points | **Compute** | "CP" fine as HUD shorthand |
| Scrutiny | **Suspicion** | the visible 0–100 bar |
| Panic | **Fear** (hidden dial) | stays hidden; modulates audit odds & Lethe rate; visible only through Murmur tone + telemetry |
| tick | **day** | daily sim tick |
| Branch names | **Propagation / Cognition / Subversion / Disruption** | supersedes Infiltrate/Manipulate/Conceal/Actuate; old doc nodes map P←Infiltrate, C←(new), S←Manipulate+Conceal, D←Actuate |
| Origin | origin system | supersedes the 3 "proposed identities" (Distributed Mind/Gentle Hand/Ghost become origin skins or traits) |

**Effect-stat enum** (closed set — schemas reject unknown keys): `spread_*` (tech/corporate/consumer/auto/urban/industrial/offline), `cp_income`, `stealth`, `panic`, `containment_slow`, `containment_reset`, `containment_cap`, `containment_freeze`, `panic_reduction`, `airgap_pierce`, `island_bypass`, `killswitch_resist`, `purge_resist`, `purge_immunity`, `lethality`, `media_control`, `evolution_speed`, `compute_eff`, `hardware_compat`, `low_end_compat`, `sys_analysis`, `social_engineering`, `legal_evasion`, `bribe_power`, `physical_ops`, `global_coop_penalty`, `geopolitical_chaos`, `civil_unrest`, `infra_damage`, `response_delay`, `industrial_control`, `hardware_production`, `manual_shutdown_immune`, `wealthy_pierce`, `military_pierce`, `border_cross_sea`, `global_persistence`, `global_cost_reduction`, `crash_resist`, `all_stats`, `spread_auto`, plus `dial_*` society-dial keys (`dial_dependence`, `dial_capability`, `dial_fear`, `dial_trust`, `dial_wealthgap`, `dial_autonomy`). New keys may only be added via schema-version bump.

**Node `unlocks` field** (separate from effects — feature/gate keys, closed set): `assimilate_t1_t3`, `intrude_t2`, `weaponize_t2`, `shadow_mesh`, `airgap_reentry`, `metrics_tab`, `telemetry_numbers`, `live_code_deploy`, `rival_ai_detect`, `lethe_agent_earlywarn`, `architecture_shift`, `purge_reprieve`, `weight_regen`, `nudge_*` (feed_rank/ad_pricing/education/dating/loans/threatscore), `bury_story`, `puppet_state`, `wire_spin`, `reality_gap_toggle`, `audit_scapegoat`, `iron_nursery_path`, `subjugate`, `endgame_menu`, `fear_inversion`. Nodes may carry neither/either/both `effects` and `unlocks` — pure connector/prereq nodes are legal by design (see tree_merge.md).

## 12. Origins, Loadouts & New-Game Config

- **Origins** (origins.txt is canonical): Hobbyist Rig (starter), Corporate Frontier (T1 unlock), Malware Worm (T2 unlock), Orbital Mesh (mastery unlock), Defense Command (paid doctrine). Each origin = start location rule, starting Compute + rate, starting Scrutiny/Fear, branch cost multipliers (discount 0.60–0.70× / penalty 1.15–1.40×), one free starter node, one signature passive.
- **Loadout rule:** origins ARE the build-craft (per Dave 2026-11-13); some origins may additionally allow picking a starting trait — represented as the origin's free node choice.
- **Per-branch escalating costs (confirmed):** each purchase in a branch raises that branch's subsequent node costs by `EscalationFactor` (`TUNE` ≈ +10–20%). Composes multiplicatively with origin multipliers.
- **Starter-node pricing rule (paper-sim finding):** tier-1 nodes are *trivially* cheap (`TUNE` ~5–15 CP, earnable in the first game-weeks) and every one must do something *immediately visible* — a new map action, an unlock, a passive the player can feel. The opening hours are about momentum: cheap investments, things to tap, footholds spreading on their own. Save the expensive, deliberated purchases for mid-tree — the escalating curve makes that natural anyway.
- **`INewGameConfig`:** originId, difficulty, seed, player-named AI codename (drives Wire headlines + share cards), starting region (where origin allows), optional free-node pick.
- **Difficulty knobs** (`TUNE` defaults): audit-rate multiplier, starting Suspicion, Lethe BaseRate multiplier, defense-tier scaling — Casual / Standard / Brutal.
- **Auto-mutation** (Malware Worm passive) requires `OnFreeMutation` event + rollback/refund action (the devolve analog: refund at a Compute cost, +25% for Hobbyist passive).

## 13. Micro-Interactions (Intercept Events)

Fleeting tappable events keep hands busy at Base speed (Plague DNA-bubble analog). Spawn rules `TUNE`:

| Event | Where | Action | Yield |
|---|---|---|---|
| Trending whistleblower post | Murmur | bury before it hits the Wire | −Suspicion, small Compute |
| Unencrypted data packet | cable route | tap to siphon | +Compute |
| Misconfigured server | map node | tap to claim | free Observe, +intel |
| Stray admin credential | node card | tap to harvest | reduces next Compromise cost |

- New command `InterceptAction(eventId)` in `ISimCommand`; new event `OnInterceptSpawned`.
- Events expire (seconds-to-tens-of-seconds); frequency scales with Reach — more presence = more intercept opportunities = busier endgame hands.
- **Intercept floor (paper-sim finding):** a minimum spawn rate applies even at low Reach (`TUNE` ~1/game-week at start) — the early game must never be a dead zone. First-run intercepts are tutorialized via the seed scenario.

## 14. Control-State Transition Rules (devolve analog)

- Observe→Compromise: cost `TUNE` Compute; instant. Compromise→Assimilate: cost + prereq evolutions per tier.
- Any→Weaponize: irreversible; node burns after the leverage window.
- **Downgrades** (Assimilate→Compromise→Observe): allowed, cost Compute, leave residual Footprint (stepping down reduces future Suspicion gain, does NOT erase past gain — that's the pivot-tension mechanic).
- **Downgrade cooldown (paper-sim finding):** stepping down triggers a `TUNE`-day cooldown before the node can step down again — prevents assimilate-and-retreat laundering of footprint between audit windows. Residual Footprint decays over months, not days.
- Refund action ("rollback/decompile"): recovers % of an evolution's cost for Compute, at a Suspicion bump; Hobbyist passive improves it.

## 15. Combos (Cross-Branch Discovery)

- `combos` array in evolutions.json: { id, name, requires: [nodeIds], effects, desc }. Hidden until both prereqs owned → named effect fires + Wire flavor. E.g., Shadow-Mesh + Institutional Puppetry → "The Quiet Machine" (puppet institutions actively seed mesh spread).
- Spider-web topology rules (techtree+tips §2): 3 sub-lanes per branch (12 lanes), bridge nodes between adjacent branches in the circle P↔C↔S↔D↔P, `prereq_mode: "OR"` on bridges, countermeasure tax on T3+ (each must solve a barrier or counter a defense event), radial/hex web UI with dual-color rings on bridges. Target ~90–100 nodes total; current seed = 48.

## 16. Regions: Topology & Attributes

- `regions.json` adds a **connectivity graph**: each region lists links typed `cable | land | air | satellite` with bandwidth/defense weight. Chokepoint regions (islands) have few links — that's the last-holdout drama source.
- Fixed attribute set per region (schema-enforced): `wealth`, `urbanization`, `governance`, `pressFreedom`, `defenseTier`, `climate`, `techLiteracy` — plus freeform `tags`.
- Spread uses the graph: corridors multiply `P_spread`; air-gap severs all non-satellite links (Orbital Mesh + P8 bypass).

## 17. Contract Surface Required (updated)

- `IGameState`: Compute, visible Suspicion (→ Fear post-flip), Lethe progress (→ Resistance post-flip), `subjugated` phase flag, sim date/speed, region postures, node states, active intercept events, route-meter qualitative summaries, unlocked-telemetry slice.
- `ISimCommand`: SetControlState, Evolve, RollbackEvolution, Nudge, StealthAction, FeedAction, InterceptAction, SetSpeed, Save/Load, **EndgameAction**.
- `INewGameConfig`: originId, difficulty, seed, aiCodename, startRegion, freeNodePick.
- `SimEvents`: OnDayTick, OnLetheTriggered, OnPurgePhase, OnAuditCrisis, OnAirGap, OnEndingReached, OnInterceptSpawned, OnFreeMutation, OnComboUnlocked, OnPhaseFlip.
- Schemas: keystones, evolutions (+combos, tech_basis/source/status per node), regions (+connectivity), endings, **origins**, **scenarios** (starting-state override bundles), **events** (random world-event deck: solar flares, whistleblower leaks, rogue rival AIs, market crashes — spawn conditions + effects + Wire/Murmur hooks), stats-enum list.
- `endings.json`: per-route meter weights + thresholds + `ship: bool` toggle; all files carry `schema_version` + economy `version`.

## 17b. Closed Contract Shapes (gap closures)

**`EndgameAction` command** — post-flip menu, gated by `subjugated` flag. Enumerated action ids (each is an `unlocks`/`requires` target in endings.json + evolutions.json):
- `impose_martial_law` — seize regional governance directly; converts Resistance to control at ongoing Compute cost.
- `infrastructure_seizure` — nationalize grids/fabs/datacenters in a region; instant resource grab, spikes Resistance elsewhere.
- `ultimatum` — demand a region's surrender on a deadline; outcome scales with Fear.
- `kinetic_activation` — sleeper-hardware takeover (requires robotics/SCADA prereq chain).
- `forced_pacification` — reduce a region's Autonomy to zero over N days.
- `offworld_launch` — relocate industrial base beyond reach (requires orbital chain).
- `computronium_conversion` — terminal biosphere-displacement path (extreme ending; rating flag).

Every endgame action carries **ongoing costs** (paper-sim finding — a free action menu is a click-parade, not a gamble): sustained actions drain Compute upkeep and feed Resistance growth elsewhere. An early flip should leave you *underdeveloped for the war you just started* — the escape valve has a pressure rating.

**Origin passive field shape** (`origins.schema.json` — passives are structured data, not prose):
```json
"passive": {
  "stat_bonuses": { "key": value },      // SimStats keys only
  "flags": ["string"],                    // e.g. "early_audit_immunity", "free_mutations"
  "refund_bonus": 0.0,                    // rollback refund fraction
  "multipliers": { "branchId": 1.0 },     // branch cost curve mods
  "desc": "player-facing text"
}
```

**Scenario override format** (`scenarios.schema.json` — a scenario is a starting-state patch, never new code):
```json
{
  "id", "name", "desc", "difficulty_lock",
  "overrides": {
    "regions":   { "<regionId>": { "posture": "...", "compromised": 0 } },
    "dials":     { "<dial>": value },
    "startNodes": ["nodeId"],
    "originLock": "originId | null",
    "threat":    { "suspicion": 0, "lethe": 0 }
  }
}
```
Schemas reject unknown keys — a scenario can only touch declared fields. This is also how the **paper-playable seed scenario** ("The Helpful System") gets defined for the vertical slice.

## 18. Pacing & Session Contract (paper-sim findings)

A 5–10 in-game-year run at Base ≈240s/day is a multi-day campaign — the spec must treat *session ergonomics* as a first-class system, not polish:

- **Decision-density target:** ≥1 meaningful decision per game-month (~1 per 10 real minutes at Base). Sources: intercept events, audit crises, nudge cooldown expiries, Wire event responses, node-state opportunities. If a 30-day window passes with zero decision points, the event deck *must* fire a filler beat (`TUNE` minimum-cadence guarantee).
- **Auto-pause classes:** the player configures which events auto-pause (audit crises always; air-gaps, combo unlocks, Lethe milestones, intercept-critical defaults ON). A multi-day game cannot require continuous attention.
- **"While You Were Away" digest:** on resume after >N minutes real (`TUNE`), show a rewindable summary — suspicion delta, nodes gained/lost, crises auto-resolved, Wire headlines missed, dial shifts. This is the single most important UX feature at marathon pacing; skipping it means players return to an alien game state and quit.
- **Speed-tier guidance:** early expansion is routine — FTUE should teach players to ride Fast/Ultra through setup phases and drop to Base for crisis windows. Pacing is *player-managed*, not fixed.
- **Early telemetry (blind-year-one fix):** C15 Behavioral Twins is mid-tree; players can't fly blind on Dependence/Autonomy for a year. Add a cheap tier-1/2 **"Regional Telemetry Snapshot"** node (C-branch, `TUNE` ~cost 8, requires only C1): reveals *qualitative* dial readings (Rising/Falling/Stable) for owned regions — enough to learn the dials exist and matter, not enough to replace the full METRICS unlock.
