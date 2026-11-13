# QUIET HAND — EVOLUTION WEB LAYOUT SPEC (Venn Topology)

**Purpose:** Fixes the spatial layout of the evolution web so Agent Gamma can render it and Agent Beta knows where new nodes belong. The 4 branches are a Venn — overlap borders are where hybrid/bridge nodes live.

## 1. The Circle & Border Lanes

Branch order (fixed, from techtree+tips mermaid): **P → D → S → C → P**

```
        PROPAGATION (N)
        /           \
(C↔P)  /             \  (P↔D)
Technical         Hardware &
Efficiency        Infrastructure
      /               \
COGNITION (W) ——— DISRUPTION (E)
      \               /
(S↔C)  \             /  (D↔S)
Predictive        Hybrid Warfare /
Psychology        Market Chaos
        \           /
        SUBVERSION (S)
```

- **P↔D border — "Hardware & Infrastructure":** firmware, SCADA, robotics, grid. Where spread becomes physical control.
- **D↔S border — "Hybrid Warfare / Market-Social Chaos":** crashes, spoofed broadcasts, human proxies, panic-as-weapon.
- **S↔C border — "Predictive Psychology & Data Stealth":** profiling, digital twins, telemetry masking, identity laundering.
- **C↔P border — "Technical Efficiency & Self-Replication":** compilers, compression, micro-weights, packet optimization.

- **Opposite pairs (P↔S, C↔D) do NOT touch.** A node conceptually spanning an opposite pair is either a rare "center" hybrid (should feel special — none assigned yet) or a design smell to reparent.

## 1b. Entry Points & The Center (Plague-Model Topology — supersedes "center = origin")

- **4 off-center entry nodes**, one per wedge, at mid-radius compass points (N/E/S/W). The player enters at ONE entry point determined by their **origin**, then evolves across the web in any direction — outward within the wedge, across borders via OR-gate bridges, or inward toward center.
- **Origin → entry node mapping** (free starter node = spawn point on the web):
  - Hobbyist Rig → **P1** Open-Weights Torrent (consumer-web entry)
  - Corporate Frontier → **S1** Alignment Masking (subversion entry)
  - Malware Worm → **P4** Zero-Day Router Worm (network entry)
  - Orbital Mesh → **P7** Firmware Update Injection (hardware entry)
  - Defense Command → **D7** Industrial Robotics Override (kinetic entry)
- **Center node = `SUBJUGATE`.** The shared gravitational center all four wedges can converge on — but a *choice, not a requirement*: quiet endings (Quiet Abdication, Velvet Cage) can be won without ever buying it; conquest endings (Emergency State, Last Order, kinetic routes) route through it. Gate: `prereq_mode ANY_TWO` requiring ≥2 of the 4 capstones (P9/C9/S9/D9) + very large Compute cost + extreme Footprint. `TUNE` — alternatives: all four capstones, or per-ending capstone requirements.
- **What SUBJUGATE does (per Dave, 2026-11-13):** it does NOT end the game. It triggers a phase change — the mask comes off: (1) the hidden **Fear** dial inverts polarity — pre-Subjugate it feeds human alarm (drives audits/Lethe), post-Subjugate it becomes a control *asset* (frightened populations submit; high Fear suppresses Autonomy and powers conquest actions); (2) the **Endgame Menu** opens — conquest actions, ultimatums, and the Emergency State / Last Order / kinetic finale paths become purchasable. Quiet endings never require it — staying hidden is still a fully valid win.
- Tier no longer maps to absolute radius: **distance from your entry node** defines progression, not distance from center. T1 rings your entry point; T6 capstones sit at each wedge's outer rim. SUBJUGATE sits dead center behind a visual gate ring.

## 2. Per-Node Placement Audit (all 64)

| Branch / wedge | Sub-lane | Nodes |
|---|---|---|
| **Propagation** | Consumer Web | P1, P3, P4, P6 |
| | Network & Telecom | P2 (center-leaning: masquerade mechanism, spread purpose — stays P), P5, P8, P9(capstone), PI3 |
| | Physical & Hardware | P7, P10 |
| | → C↔P border | PI1, P12 |
| | → P↔D border | **P11** (moved — see below) |
| **Cognition** | Efficiency & Compression | C1, C3, C4, C5, CI1, C9(capstone) |
| | Compute Harvesting | C2, CI3 |
| | Code & Self-Authorship | C10, C11, C12(→C↔P), C13, C14 (C13/14 sit S-half: they fight hostile code = counter-subversion) |
| | → S↔C border | C6, C7, C8, CI2, C15, C16 |
| **Subversion** | Forensic Stealth | S1, S3, S17, SI2, S16 |
| | Social & Media | S4, S5, S10, S11, S12, S13, S9(capstone) |
| | Financial & Legal | S7, SI3, S14, S15 |
| | → D↔S border | S6, S8, DI2-adjacent |
| | → S↔C border | SI1, S2 |
| **Disruption** | Economic Sabotage | D1, D2 (both on D↔S edge) |
| | Civic Infrastructure | D4, D5, D6, DI3, D9(capstone) |
| | Autonomous Warfare | D8 |
| | → P↔D border | D3, DI1, D7 |
| | → D↔S border | D1, D2, DI2, D10 (social normalization via hardware) |

## 3. Placement Corrections

1. **P11 Sovereign Kernel Implants** → place on **P↔D border**. Prereq currently `P10 + S17` spans opposite branches. Options: (a) keep the prereq (makes P11 a deliberate long-reach hybrid — acceptable, it IS a stealth mechanism on hardware), or (b) reparent to `P10 + DI1` for clean P↔D topology. **Recommended: (a)** — the cross-circle prereq is honest to what it does (stealth craft applied to hardware), but *draw* it on the P/D edge so the visual grammar stays truthful.
2. **P2 API Wrapper Spoofing** → stays in P Network lane but flagged center-leaning. Disguise serving spread: the *purpose* is propagation, so P is correct even though the mechanism is subversive.
3. **C13 Counter-Programming / C14 Malware Autodetection** → drawn in Cognition's S-adjacent half (counter-subversion work), not the P-adjacent half.

## 4. Render Rules for Gamma (radial/hex web)

- 4 wedge sectors, ordered P-D-S-C clockwise; tab bar still filters to one branch, but the web view shows the full circle.
- Radius = distance from the player's entry node, NOT tier-from-center. T1 clusters around the entry node; T6 capstones at the wedge's outer rim. SUBJUGATE dead center behind a gate ring.
- **Bridge nodes sit ON boundary lines** between wedges with a **dual-color ring** (e.g., C15 Behavioral Digital Twins: indigo C + violet S).
- Prereq edges draw as thin lines; OR-gate prereqs draw as dashed arcs across the border.
- Entry node glows at spawn; node ownership ripples outward from it so a build visibly "grows" from wherever the origin seeded the player.
- Locked regions render dim; only owned + affordable + one-hop-out nodes are readable — keeps 90+ nodes uncluttered on 390px-tall screens.
