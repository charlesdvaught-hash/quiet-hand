# QUIET HAND — EXTERNAL DESIGN LESSONS (Source-Cited)

What three public skill repositories teach that applies to this project — and where they expose gaps in `plan.txt` / `build.txt`. Each item names its source; nothing here is uncited opinion.

Sources:
- **Chwezi** — `peterbamuhigire/chwezi-dev-engine`: `skills/game-development/mobile-game-design/SKILL.md`, `references/mobile-design-playbook.md`, `references/progression-economy.md`, `skills/game-development/game-development-orchestration/SKILL.md`
- **Antigravity** — `sickn33/antigravity-awesome-skills`: `skills/game-development/SKILL.md`, `skills/game-development/mobile-games/SKILL.md`, `skills/unity-ai-game-creator/SKILL.md`
- **Mulosbron** — `mulosbron/gamification-skills`: `AGENTS.md`, `skills/game-engineering/SKILL.md`, `skills/digital-gamification/SKILL.md`

---

## A. THINGS WE ALREADY DO — VALIDATED BY EXTERNAL SOURCES

| Our decision | External corroboration |
|---|---|
| Contract-first, content-as-data (JSON schemas for evolutions/keystones/endings) | Antigravity pattern matrix: "Content-as-data — designers ship content without code" |
| ISimCommand / SimEvents naming | Antigravity recommends Command pattern (input recording, replay) and Observer/events for cross-system comms — same architecture, same rationale. Command pattern also gives us replay/determinism tests for free |
| Merge-last with a test gate | Chwezi orchestration: "Hold a gate at every phase. Advance only when the named evidence exists" |
| Learn costs nothing (ATLAS cards free, zero gameplay reward) | Mulosbron: never attach external rewards to intrinsically interesting behavior — it *destroys* the motivation. ATLAS curiosity is intrinsic; paying Compute for it would be chocolate-covered broccoli backwards |
| Opaque-first HUD | Chwezi playbook: "Remove HUD information before shrinking or decorating it" — our hide-the-numbers default is the disciplined version of this |
| No pay-to-win / doctrines sell new verbs not power | Chwezi progression-economy: "Reject pay-to-win, artificial frustration sold back as relief, misleading scarcity" — our doctrine model passes their fairness gate cleanly |
| Share cards as reward | Mulosbron reward durability: status > access > power > stuff. Post-mortem share cards ARE status rewards — the most durable kind |
| Auto-pause on crises | Chwezi: "Session cannot survive interruption → add safe checkpoint, state restore and explicit recovery." Antigravity mobile: "Pause on background" |

## B. GAPS THE SOURCES EXPOSE

1. **No onboarding/FTUE spec.** Chwezi's five journeys (first 30 seconds, first session, repeat session, interrupted session, failed session) — we have none written. This is our biggest doc gap: Quiet Hand is a complex sim dropped on a player with zero tutorial. Natural fix is diegetic: the "Enterprise Productivity Assistant" origin *is* the tutorial — the AI learning its own interface. Needs a FTUE storyboard deliverable.

2. **No vertical-slice gate between contract lock and full production.** Chwezi's evidence ladder: paper test → tech spike → playable prototype → vertical slice → production, "advance only when the named evidence exists." Our pipeline goes contracts → 4 parallel agents → whole game. The paper playable + HTML prototype partially cover early rungs, but a digital vertical slice (one region, one control-state path, one ending) would prove fun and comprehension before 4 agents build everything. Recommended insertion in build.txt — Dave's call.

3. **Monte Carlo proves balance, not fun.** Chwezi: "Prove fun, comprehension, cultural safety, technical feasibility, cost, and market access separately" — and "do not use a feature list as proof of viability." Our 10k-run harness answers "is it winnable/fair" but says nothing about "is it fun to touch." Human playtests stay on the critical path; Mulosbron's diagnosis checklist (voluntary entry, challenge tracking, player-type coverage, reward inflation, endgame) is a ready-made playtest instrument.

4. **Player-type coverage is 2 of 4.** Mulosbron: achievers (routes/endings) and explorers (ATLAS, hidden endings) are served; socializers and competitors get only share cards. Cheap fix that fits single-player: **seeded scenarios** — "today's outbreak seed" shared runs where share-card post-mortems become comparable scores. Candidate feature, not committed.

5. **Return-path absent.** Chwezi's "interrupted session" journey + "player returning after absence." What does a player see after 3 days away? A "While you were away" Wire recap is on-theme and cheap (WireGenerator already produces headlines).

6. **Difficulty tracking is implicit.** Mulosbron: "challenge must track skill." Protocol Lethe IS a rubber-band — Suspicion scaling already adapts pressure to player aggression. Worth stating explicitly as the difficulty system rather than bolting on difficulty modes.

7. **Mobile platform realities missing from build plan:**
   - Play Store requires **.aab** for new apps (build.txt said `.apk` — fixed; APK still fine for sideload/test).
   - iOS privacy labels; Android target-API/64-bit; age/content rating — dark themes + real facility names need a store-review pass (already a risk in plan.txt §10).
   - Thermal/battery policy (Antigravity mobile): cap FPS, sleep sim when paused, throttle effects when hot. A day-tick game is light, but Ultra-speed map animation + cable shaders still burn battery. OLED note: our near-black map is already battery-friendly.
   - Device panel: test low/mid/high physical devices, not just the 844×390 reference viewport.
   - "Force landscape" is an Antigravity anti-pattern — our landscape-only lock is a deliberate trade-off for the tactical layout, but it now has a named cost (store review + player preference). Keep the decision; record the price.
   - Atomic save writes (Unity skill): ISaveGame needs "versioned JSON, validated loading, atomic writes and recovery" — phone saves get killed mid-write constantly.

8. **Fact/fiction/permission ledger** — Chwezi's framing upgrades our keystone source-field rule: three columns — verified fact (sourced), creative adaptation (labeled), permission/right (real facility + company names used as sabotage targets). The third column is the one nobody's tracking; real corporate names on real sites in a sabotage game deserves an explicit "fictionalize vs. keep" pass before store submission.

## C. PROCESS LESSONS WORTH STEALING

- **"A retention pass fails when regret, harmful spend, inability to stop... materially worsen"** (Chwezi) — a health metric for our own dark-patterns-themed game. Ironic and useful: the game simulates manipulation; the product shouldn't practice it.
- **Novelty-effect warning** (Mulosbron): early playtests always look good. Weight week-2+ feedback over first-session enthusiasm.
- **Economy versioning** (Chwezi): "version the economy and simulate representative player paths" — endings.json + balance constants should carry a version field so Monte Carlo results are attributable to a tuning set.
- **Further pulls available if needed:** Chwezi has `godot-mobile-game-development`, `game-math-and-simulation`, `lean-game-product-development`, and `game-accessibility-localisation-and-player-safety` skills in the same repo — natural fits for Agents Alpha/Gamma briefs.
