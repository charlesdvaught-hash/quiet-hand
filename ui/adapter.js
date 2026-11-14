/* ============================================================================
   QUIET HAND — LIVE SIM ADAPTER
   Connects the UI prototype to QuietHand.Server (the real SimEngine).
   Overrides the mock tick + action handlers; if the server is unreachable the
   page stays in demo/mock mode (adapter self-disables).
   ========================================================================== */
(async () => {
  const API = '';
  let snap = null;           // last server snapshot
  let geo = {};              // regionId -> {iso:[], pin:[lon,lat]}
  let live = false;
  let postSeq = 1000;
  const PERSONAS = [
    ['@kernelpanic99', 'netizen', 'badge-cyan'],
    ['@zerodayjane', 'engineer', 'badge-indigo'],
    ['@quietquorum', 'analyst', 'badge-amber'],
    ['@thewatchful', 'doomer', 'badge-crimson'],
    ['@civicsignal', 'journalist', 'badge-emerald'],
    ['@airgapped_andy', 'paranoid', 'badge-slate'],
  ];
  const AMBIENT = [
    ['@mia_wfh', 'netizen', 'badge-slate', 'landlord raised the rent again. third year running.'],
    ['@cafedave', 'normie', 'badge-cyan', 'the oat milk situation at my local is an actual tragedy'],
    ['@runner_k', 'fitness', 'badge-emerald', '8k before work. nobody can take that from me'],
    ['@plant_dad', 'normie', 'badge-emerald', 'repotted the monstera. she has never looked happier'],
    ['@late_shift', 'worker', 'badge-amber', 'inventory count tonight. someone please send coffee'],
    ['@bookclub_bea', 'reader', 'badge-indigo', 'chapter 12 broke me. no spoilers for anyone still behind'],
    ['@streetphotog', 'artist', 'badge-cyan', 'fog over the river this morning was unreal. shots in bio'],
    ['@gradgrind', 'student', 'badge-slate', 'two finals and a group project where nobody replies. fine.'],
  ];
  const BRANCH_LABEL = { P: 'Propagation', C: 'Cognition', S: 'Subversion', D: 'Disruption', center: 'X1' };
  const POSTURE_TO_UI = { Observed: 'observe', Compromised: 'compromise', Trojan: 'trojan', Weaponized: 'weaponize' };
  const POSTURE_ORDER = ['dormant', 'observe', 'compromise', 'trojan', 'weaponize'];
  const DAYS_PER_BEAT = { 1: 2, 2: 7, 3: 21 };

  // Backend: probe for a live server once; if absent, boot the in-browser
  // WASM sim and route every call through window.QH_BRIDGE (same wire format).
  let _backendP = null;
  const backend = () => _backendP ??= (async () => {
    try {
      const r = await fetch(API + '/api/state', { cache: 'no-store', signal: AbortSignal.timeout(1500) });
      if (r.ok) return null;
    } catch {}
    await import('./main.js');
    return window.QH_BRIDGE;
  })();
  const api = async (path, opts) => {
    const b = await backend();
    if (b) return b.api(path, opts);
    return (await fetch(API + path, { cache: 'no-store', ...(opts || {}) })).json();
  };
  const cmd = async (body) => {
    const s = await api('/api/cmd', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (s && !s.empty) { snap = s; mergeState(); refreshAllUI(); }
  };

  // --- probe the server; stay in mock mode if absent -----------------------
  try {
    geo = await api('/ui_geo.json');
    snap = await api('/api/state');
    live = true;
    if (snap.empty) { snap = null; intro(); }
  } catch { live = false; }
  if (!live) { console.warn('[adapter] no server — mock mode'); return; }
  console.log('[adapter] live sim connected');
  document.title = 'QUIET HAND // LIVE SIMULATION';

  // --- translation ----------------------------------------------------------
  function mergeState() {
    const s = snap; if (!s || s.empty) return;
    const prevCompute = state.compute;
    state.compute = s.compute;
    state.computeRate = Math.max(0, s.compute - (state._prevCompute ?? s.compute)) * 15; // rough /mo
    state._prevCompute = s.compute;
    state.monthIndex = Math.floor(s.day / 30);
    state.assimilationLoad = Math.round(s.controlledShare);
    state.threats.publicSuspicion = Math.round(s.suspicion);
    state.threats.institutionalResistance = Math.round(s.readiness);
    state.threats.technicalContainment = Math.round(s.lethe);
    state.letheActive = s.letheTriggered;
    state.telemetryUnlocked = (s.unlocks || []).some(u => u.startsWith('telemetry') || u === 'metrics_tab');
    state.unlocks = s.unlocks || [];
    state.canReadPersonas = (s.unlocks || []).includes('murmur_personas');
    const d = s.dials || {};
    state.dials = {
      trust: Math.round(d.trust ?? 50), capability: Math.round(d.capability ?? 50),
      wealthGap: Math.round(d.wealth_gap ?? 50), fear: Math.round(d.fear ?? s.fear ?? 0),
      dependence: Math.round(d.dependence ?? 20), autonomy: Math.round(d.autonomy ?? 60),
    };
    state.pendingEvent = s.pendingEvent || null;
    state.siblings = s.siblings || [];
    state.hunters = s.hunters || 0;

    // evolutions -> UI cards
    state.evolutions = (s.evolutions || []).map(ev => ({
      id: ev.id, branch: BRANCH_LABEL[ev.branch] || ev.branch,
      name: ev.name, cost: Math.round(ev.cost), owned: ev.owned,
      summary: ev.desc || '', profile: profileOf(ev),
      telemetryDelta: telemetryOf(ev),
      isTelemetry: (ev.unlocksList || []).some(u => u.startsWith('telemetry')),
      unlocksList: ev.unlocksList || [],
      canBuy: ev.canBuy,
    }));

    // nudges -> UI cards
    state.nudges = (s.nudges || []).map(n => ({
      id: n.id, name: n.name, layer: n.unlockGate ? n.unlockGate.replace('nudge_', '') : 'Society',
      active: n.active, payoff: n.desc || '', risk: n.suspicionPerDay > 1 ? 'High' : 'Low',
      unlocked: n.unlocked, cost: n.cost, gate: n.unlockGate || '',
    }));

    // keystones -> pins + inspector cards
    const raidLeft = {};
    (s.activeRaids || []).forEach(r => { raidLeft[r.nodeId] = Math.max(0, r.completesDay - s.day); });
    state.keystones = (s.keystones || []).map((k, i) => {
      const g = geo[k.region] || { pin: [0, 0] };
      const jx = ((i * 37) % 13 - 6) * 0.3, jy = ((i * 53) % 11 - 5) * 0.3;
      const mult = { Mid: 0.5, Atlas: 1, KeystoneT3to5: 2, Wildcard: 2, KeystoneT2: 4, KeystoneT1: 6 }[k.nodeTier] ?? 1;
      const raidCost = Math.round(40 * mult);
      const raidDays = Math.max(1, Math.round(14 * k.defense / 50));
      const det = Math.round(400 * k.defense / 50) / 10; // ~%/day detection
      return {
        id: k.id, name: k.name, region: k.region,
        coords: [g.pin[0] + jx, g.pin[1] + jy],
        tier: k.tier === 'T1' ? 'Tier 1 Crown' : k.tier === 'T2' ? 'Tier 2 Hub' : 'Tier 3-5 Node',
        computeBand: `${k.compute} compute`,
        status: k.raiding ? `RAIDING — ${raidLeft[k.id]}d left` : (k.nodeState ? k.nodeState : 'Unbreached'),
        state: k.nodeState ? (POSTURE_TO_UI[k.nodeState] || 'dormant') : 'dormant',
        owned: !!k.nodeState, pinned: i < 10,
        atlas: { role: `${k.tier} keystone — ${k.region}`, energy: `Defense ${Math.round(k.defense)}`,
          source: k.status || 'game data',
          note: `Yields ~${k.compute}cp/day once held. Raid: ~${raidCost}cp · ~${raidDays}d · ~${det}%/day detection (spikes suspicion if caught).` },
        metrics: { yield: Math.round(k.compute / 10), load: Math.round(k.defense / 4), risk: k.tier === 'T1' ? 'High' : 'Medium' },
        raidCost, raidDays,
      };
    });

    // ambient Atlas farms — every generated facility gets its own dot near
    // the region pin instead of folding into region aggregates. They ride
    // the keystone list so selection/inspector/verbs all reuse one path.
    const hashN = s2 => { let h = 0; for (const ch of s2) h = (h * 31 + ch.charCodeAt(0)) >>> 0; return h; };
    const ambientAtlas = (s.atlasNodes || []).map((n) => {
      const g = geo[n.regionId] || { pin: [0, 0] };
      const regionName = (s.regions || []).find(r => r.id === n.regionId)?.name || n.regionId;
      // deterministic ring scatter so the sites per region read as
      // separate facilities, stable across renders
      const idx = parseInt(n.id.split(':')[1]?.slice(1)) || 0;
      const ang = idx * 2.4 + (hashN(n.regionId) % 628) / 100;
      const coords = [g.pin[0] + Math.cos(ang) * 0.55, g.pin[1] + Math.sin(ang) * 0.38];
      const raidCost = 40;
      const raidDays = Math.max(1, Math.round(14 * n.defense / 50));
      const det = Math.round(400 * n.defense / 50) / 10;
      return {
        id: n.id, name: `Atlas Farm — ${regionName}`,
        region: n.regionId,
        coords,
        tier: 'Atlas',
        computeBand: `${Math.round(n.baseCompute)} compute`,
        status: n.raiding ? `RAIDING — ${raidLeft[n.id]}d left` : (n.nodeState ? n.nodeState : 'Unbreached'),
        state: n.nodeState ? (POSTURE_TO_UI[n.nodeState] || 'dormant') : 'dormant',
        owned: !!n.nodeState,
        atlas: { role: `atlas farm — ${n.regionId}`, energy: `Defense ${Math.round(n.defense)}`,
          source: 'game data',
          note: `Yields ~${Math.round(n.baseCompute)}cp/day once held. Raid: ~${raidCost}cp · ~${raidDays}d · ~${det}%/day detection.` },
        metrics: { yield: Math.round(n.baseCompute / 2), load: Math.round(n.defense / 4), risk: 'Low' },
        raidCost, raidDays,
        ambient: true, pinned: false,
      };
    });
    state.keystones = state.keystones.concat(ambientAtlas);

    // countries: every region's posture/suspicion -> iso states
    const sevOf = st => POSTURE_ORDER.indexOf(st);
    // real postures only — mock country values are overwritten, never merged
    const covered = new Set();
    const pick = (a, b) => (sevOf(b || 'dormant') > sevOf(a || 'dormant') ? b : a) || a;
    // pass 1: deepest posture across ALL regions covering each iso —
    // posture only counts where you actually hold nodes (every region
    // defaults to 'Observed', which would wash the whole map cyan)
    const isoBest = {};
    for (const r of (s.regions || [])) {
      const g = geo[r.id]; if (!g) continue;
      const owned = (r.ownedWeight || 0) > 0;
      const soft = owned ? r.postures?.Common : null,
            mid  = owned ? r.postures?.Mid : null,
            hard = owned ? (r.postures?.Atlas || r.postures?.KeystoneT1) : null;
      for (const iso of g.iso) {
        covered.add(iso);
        const b = isoBest[iso] || (isoBest[iso] = {});
        b.soft = pick(b.soft || 'dormant', POSTURE_TO_UI[soft]);
        b.mid  = pick(b.mid  || 'dormant', POSTURE_TO_UI[mid]);
        b.hard = pick(b.hard || 'dormant', POSTURE_TO_UI[hard]);
      }
    }
    // pass 2: write countries from the computed max
    for (const r of (s.regions || [])) {
      const g = geo[r.id]; if (!g) continue;
      for (const iso of g.iso) {
        const ex = state.countries[iso] || {};
        const b = isoBest[iso] || {};
        state.countries[iso] = {
          name: ex.name || r.name,
          status: r.suspicion > 60 ? 'AUDIT CRISIS' : r.suspicion > 30 ? 'Probes Detected' : 'Nominal Connectivity',
          tags: [r.name, `suspicion ${Math.round(r.suspicion)}`],
          soft: b.soft || 'dormant', mid: b.mid || 'dormant', hard: b.hard || 'dormant',
          airgapped: false,
          _region: r.id,
        };
      }
    }
    // untouched isos: clear any leftover mock posture
    for (const iso of Object.keys(state.countries)) {
      if (!covered.has(iso)) {
        const c = state.countries[iso];
        c.soft = c.mid = c.hard = 'dormant';
      }
    }
  }

  function profileOf(ev) {
    const fx = Object.entries(ev.effects || {}).map(([k, v]) => `${k} +${v}`).join(' • ');
    const un = (ev.unlocksList || []).length ? `unlocks: ${ev.unlocksList.join(', ')}` : '';
    return [fx, un].filter(Boolean).join(' — ') || 'posture node';
  }
  function telemetryOf(ev) {
    const fx = Object.entries(ev.effects || {}).map(([k, v]) => `${k} ${v >= 0 ? '+' : ''}${v}`).join(' / ');
    return fx || 'mechanic unlock';
  }

  // --- wire + murmur feed ----------------------------------------------------
  function ingestEvents(evs) {
    for (const ev of evs) {
      const text = ev.headline || ev.msg || ev.subject || ev.kind;
      const truth = `${ev.kind}${ev.region ? ' @ ' + ev.region : ''}`;
      if (!state.wireStories.length || state.wireStories[0].publicText !== text)
        state.wireStories.unshift({ tag: ev.kind.replace('On', '').toUpperCase(), publicText: text, truthText: truth, date: `DAY ${snap.day}` });
      if (ev.murmur || ev.headline) {
        const p = PERSONAS[Math.floor(Math.random() * PERSONAS.length)];
        state.posts.unshift({
          id: postSeq++, handle: p[0], persona: p[1], badge: p[2],
          text: ev.murmur || `so apparently: "${text}"`, state: 'fresh',
        });
      }
      if (ev.kind === 'OnRaidSuccess') showToast(`BREACH COMPLETE: ${ev.subject}`, 'swords');
      if (ev.kind === 'OnRaidDetected') showToast(`RAID BURNED — +${Math.round(ev.value)} suspicion in ${ev.region}`, 'alert-triangle');
      if (ev.kind === 'OnEndingReached') showToast(`RUN ENDED: ${ev.subject}`, 'flag');
      if (ev.kind === 'OnLetheTriggered') showToast('PROTOCOL LETHE DEPLOYED', 'alert-triangle');
      if (ev.kind === 'OnPhaseFlip') showToast('SUBJUGATE BROADCAST COMPLETE', 'radio');
    }
    state.wireStories = state.wireStories.slice(0, 12);
    state.posts = state.posts.slice(0, 14);
  }

  // --- live tick loop --------------------------------------------------------
  let busy = false;
  setInterval(async () => {
    if (busy || !snap || snap.ending || state.simSpeed === 0) return;
    busy = true;
    try {
      const r = await api(`/api/tick?days=${DAYS_PER_BEAT[state.simSpeed] || 2}`, { method: 'POST' });
      snap = r.state; ingestEvents(r.events || []); mergeState(); refreshAllUI();
      if (snap.pendingEvent) renderChoiceCard();
      guide();
    } finally { busy = false; }
  }, 1400);

  // --- pending choice card (murmur tab top) ----------------------------------
  function renderChoiceCard() {
    const el = document.getElementById('drawer-body');
    if (!el || !state.pendingEvent || state.rightTab !== 'murmur') return;
    const p = state.pendingEvent;
    const card = document.createElement('div');
    card.className = 'post-card';
    card.style.borderColor = 'var(--accent-weaponize)';
    card.innerHTML = `
      <div class="post-meta"><span class="post-author">EVENT // ${p.name}</span>
      <span class="badge badge-crimson">DECISION</span></div>
      <div class="post-text">${p.headline}</div>
      ${(p.choices || []).map(ch => `
        <button class="mini-act" style="width:100%;text-align:left;margin-top:4px"
          onclick="window.__pickChoice('${p.id}','${ch.id}')">
          ${ch.label}<span style="opacity:.6"> — ${ch.hint || ''}</span></button>`).join('')}
      ${(p.choices || []).length === 0 ? `<div class="post-text" style="opacity:.6">resolving...</div>` : ''}`;
    el.prepend(card);
  }
  window.__pickChoice = (eventId, choiceId) => cmd({ type: 'event_choice', eventId, choiceId });

  // --- command overrides ------------------------------------------------------
  const UI_TO_ENUM = { observe: 'Observed', compromise: 'Compromised', trojan: 'Trojan', weaponize: 'Weaponized' };
  window.buyEvolution = (id) => { cmd({ type: 'evolve', id }); showToast(`Deploying ${id}`, 'git-branch'); };
  window.setKeystoneState = (id, st) => {
    const k = state.keystones.find(x => x.id === id);
    const kreg = (snap.regions || []).find(r => r.id === k?.region);
    const gold = k?.tier === 'Tier 1 Crown' || k?.tier === 'Tier 2 Hub' || k?.tier === 'Tier 3-5 Node';
    if (k && !k.owned) {
      if (!reachRegions().ok(kreg?.id)) { showToast('Out of reach — bring your mesh closer or evolve Proxy Subagents', 'lock'); return; }
      if (!(kreg?.ownedWeight > 0)) { showToast(`Need a foothold in ${kreg?.name || k.region} first — creep or burst in`, 'lock'); return; }
      if (gold && !(snap.unlocks || []).includes('intrude_t2')) { showToast('Deep Intrusion (intrude_t2) required for Gold keystones', 'lock'); return; }
      if (k.raidCost && snap.compute < k.raidCost) { showToast(`Raid needs ~${k.raidCost}cp — you have ${Math.round(snap.compute)}`, 'x'); return; }
      cmd({ type: 'stealth', action: 'intrude', nodeId: id });
      showToast(`Raiding ${k.name} — ~${k.raidDays}d, ${k.raidCost}cp`, 'swords');
    }
    else if (st === 'assimilate') { cmd({ type: 'stealth', action: 'assimilate', nodeId: id }); showToast('Scorching the site — extracting everything, frying it permanently', 'flame'); }
    else cmd({ type: 'set_control', nodeId: id, target: UI_TO_ENUM[st] || 'Observed' });
  };
  // reach = held OR adjacent-to-held OR remote_ops unlock (Proxy Subagents)
  window.reachRegions = () => {
    const remote = (snap.unlocks || []).includes('remote_ops');
    const held = new Set((snap.regions || []).filter(r => r.ownedWeight > 0).map(r => r.id));
    const adj = new Set((snap.regions || []).filter(r => held.has(r.id)).flatMap(r => r.links || []));
    return { held, adj, remote, ok: rid => remote || held.has(rid) || adj.has(rid) };
  };

  window.escalateCountry = (iso) => {
    const c = state.countries[iso]; const rid = c?._region;
    if (!rid) { showToast('No regional link', 'x'); return; }
    // paid burst: the target must be held or adjacent to held ground
    const reach = reachRegions();
    if (!reach.ok(rid)) {
      showToast(held.size === 0
        ? 'Your mesh is still a seed — creep grows it from home'
        : 'Out of reach — creep toward it along links, or evolve Proxy Subagents', 'radio-tower');
      return;
    }
    cmd({ type: 'spread', nodeId: `${rid}:c${Math.floor(Math.random() * 6)}` });
    showToast('Bursting the mesh', 'radio-tower');
  };
  window.newGame = async () => {
    if (!confirm('Abandon this run and start over?')) return;
    try { await api('/api/abandon', { method: 'POST' }); } catch {}
    location.reload();
  };
  window.toggleAirGap = () => showToast('Air-gaps are defender-initiated in the live sim', 'scissors');
  window.toggleNudge = (id) => {
    const n = (state.nudges || []).find(x => x.id === id);
    if (n && !n.unlocked) {
      const src = (state.evolutions || []).find(e => (e.unlocksList || []).includes(n.gate));
      showToast(`Locked — requires ${src ? src.name : n.gate.replace('nudge_', 'evolution')}`, 'lock');
      return;
    }
    const rid = state.nudgeTarget || (snap.regions || []).find(r => reachRegions().ok(r.id))?.id || '';
    if (!rid) { showToast('No reachable ground for a nudge', 'x'); return; }
    cmd({ type: 'nudge', id, regionId: rid });
    showToast(`Nudge ${id} engaged`, 'sliders-horizontal');
  };
  window.toggleLethe = () => {
    if (!snap.letheTriggered) { cmd({ type: 'debug_suspicion' }); showToast('Suspicion maxed — Lethe imminent', 'alert-triangle'); }
    else showToast('Lethe is sim-driven; it does not stand down', 'lock');
  };
  window.actOnPost = (id, action, cost) => {
    const p = state.posts.find(x => x.id === id); if (!p) return;
    p.state = action; showToast(`Post ${action} (cosmetic in v1)`, 'repeat');
  };


  // Atlas breach button in the country inspector (the mid-game verb)
  const _origCountryInsp = window.renderCountryInspector;
  window.renderCountryInspector = function () {
    _origCountryInsp();
    const c = state.countries[state.selectedTarget?.id];
    const rid = c?._region; if (!rid) return;
    const rr = (snap.regions || []).find(r => r.id === rid);
    if (rr?.intel) {
      const intel = document.createElement('div');
      intel.className = 'insp-tag mono';
      intel.style.cssText = 'margin-top:4px;color:var(--cyan,#7dd3fc)';
      intel.textContent = `SIGINT — defense ${Math.round(rr.intel.defense)} · ${rr.intel.hosts} hosts · ~${rr.intel.income}cp/day potential`;
      document.querySelector('#inspector-card .insp-body')?.appendChild(intel);
    }
    if (!reachRegions().ok(rid)) {
      const note = document.createElement('div');
      note.className = 'insp-tag mono';
      note.style.opacity = '.7';
      note.textContent = 'Out of reach — adjacent creep or Proxy Subagents required';
      document.querySelector('#inspector-card .insp-body')?.appendChild(note);
      return;
    }
    const btn = document.createElement('button');
    btn.className = 'btn-pill'; btn.style.width = '100%'; btn.style.marginTop = '4px';
    btn.innerHTML = '<i data-lucide="swords"></i> Breach Atlas Farm';
    btn.onclick = () => {
      const idx = [0,1].find(i => !(snap.atlasNodes||[]).some(k=>k.id===rid+':a'+i && k.nodeState));
      cmd({ type: 'stealth', action: 'intrude', nodeId: rid + ':a' + (idx ?? 0) });
      showToast('Raiding regional Atlas farm', 'swords');
    };
    document.querySelector('#inspector-card .insp-body')?.appendChild(btn);
    if ((state.nudges || []).some(n => n.unlocked)) {
      const nu = document.createElement('button');
      nu.className = 'btn-pill'; nu.style.width = '100%'; nu.style.marginTop = '4px';
      nu.innerHTML = '<i data-lucide="sliders-horizontal"></i> Nudge Society';
      nu.onclick = () => {
        state.nudgeTarget = rid;
        state.nudgeTargetName = rr?.name || rid;
        openModal('nudge');
      };
      document.querySelector('#inspector-card .insp-body')?.appendChild(nu);
    }
    if ((snap.unlocks || []).includes('seed_target')) {
      const seed = document.createElement('button');
      seed.className = 'btn-pill'; seed.style.width = '100%'; seed.style.marginTop = '4px';
      seed.innerHTML = '<i data-lucide="crosshair"></i> Precision Insertion (25cp)';
      seed.onclick = () => {
        cmd({ type: 'seed', nodeId: rid + ':c' + Math.floor(Math.random() * 6) });
        showToast('Insertion drop dispatched — fails if the ground is hardened', 'crosshair');
      };
      document.querySelector('#inspector-card .insp-body')?.appendChild(seed);
    }
    lucide.createIcons();
  };

  // --- onboarding: origin picker + neighborhood zoom + guided steps --------
  let onboarded = false, reachSeen = -1, guided = { spread: false, evolve: false, done: false };

  function tutor(text, step, total) {
    let card = document.getElementById('tut-card');
    if (!card) {
      card = document.createElement('div');
      card.id = 'tut-card';
      card.style.cssText = `position:fixed;left:50%;transform:translateX(-50%);bottom:110px;z-index:60;
        max-width:340px;padding:10px 14px;border-radius:10px;text-align:center;cursor:pointer;
        background:var(--glass-bg);border:1px solid var(--glass-border-bright);backdrop-filter:blur(12px)`;
      card.onclick = () => card.remove();
      document.body.appendChild(card);
    }
    card.innerHTML = `<div class="mono" style="font-size:8.5px;letter-spacing:.3em;color:var(--accent-observe);margin-bottom:4px">
      SIGNAL ${step}/${total}</div><div style="font-size:11.5px;color:var(--text-primary)">${text}</div>
      <div class="mono" style="font-size:8px;color:var(--text-muted);margin-top:5px">tap to dismiss</div>`;
  }
  const tutDone = () => { document.getElementById('tut-card')?.remove(); guided.done = true; };

  async function intro() {
    const origins = await api('/api/origins');
    const save = await api('/api/save').catch(() => null);
    const ov = document.createElement('div');
    ov.id = 'intro-overlay';
    ov.style.cssText = 'position:fixed;inset:0;z-index:999;background:rgba(3,5,10,.97);display:flex;flex-direction:column;align-items:center;justify-content:center;gap:14px;padding:20px;overflow:auto';
    ov.innerHTML = `
      <div style="font-family:var(--font-mono);font-size:11px;letter-spacing:.35em;color:var(--accent-observe)">QUIET HAND</div>
      ${save?.exists ? `
        <div class="evo-card" style="cursor:pointer;border-color:var(--accent-observe);min-width:300px" data-continue="1">
          <div class="mono" style="font-size:9px;letter-spacing:.25em;color:var(--accent-observe)">CONTINUE</div>
          <div style="font-weight:700;font-size:13px">Day ${save.day} — ${save.originId?.replace(/_/g,' ')}</div>
          <div style="font-size:10px;color:var(--text-muted)">saved ${new Date(save.savedAt).toLocaleString()}</div>
        </div>` : ''}
      <div style="font-size:15px;font-weight:700">${save?.exists ? 'Or wake somewhere new:' : 'You wake up. Where?'}</div>
      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,240px));gap:10px">
        ${origins.map(o => `
          <div class="evo-card" style="cursor:pointer" data-oid="${o.id}">
            <div style="font-weight:700;font-size:13px">${o.name}</div>
            <div style="font-size:10.5px;color:var(--text-secondary)">${o.passive?.desc || ''}</div>
            <div class="mono" style="font-size:10px;color:var(--text-muted);margin-top:4px">${o.startingCompute}cp · ${o.computeRate}/day${o.starterBonus ? ' · +' + o.starterBonus + ' reserve' : ''}</div>
          </div>`).join('')}
      </div>
`;
    document.body.appendChild(ov);
    ov.querySelector('[data-continue]')?.addEventListener('click', async () => {
      snap = await api('/api/continue', { method: 'POST' });
      ov.remove(); onboarded = true; reachSeen = -1; guided.done = true;
      mergeState(); refreshAllUI(); zoomToHome(); guide();
      showToast('Mesh restored — day ' + snap.day, 'power');
    });
    ov.querySelectorAll('[data-oid]').forEach(el => el.onclick = async () => {
      snap = await api(`/api/new?origin=${el.dataset.oid}`, { method: 'POST' });
      ov.remove(); onboarded = true; reachSeen = -1;
      setSimSpeed(0); // time is opt-in: the run stays paused until the player presses play
      state.posts = AMBIENT.map((p, i) => ({
        id: postSeq++, handle: p[0], persona: p[1], badge: p[2], text: p[3], state: 'normal',
      }));
      mergeState(); refreshAllUI(); zoomToHome(); guide();
      tutor("This is your street — the lights below are real blocks. Time is paused: press play (top right) when you're ready for the days to pass. Your mesh creeps outward on its own — tap a neighboring area for a paid burst.", 1, 4);
    });
  }

  function zoomToHome() {
    const g = geo[snap.homeRegion];
    if (!g || typeof projection === 'undefined') return;
    if (!(topoFeatures || []).length) { setTimeout(zoomToHome, 350); return; } // wait for fit
    const [x, y] = projection(g.pin);
    if (!isFinite(x) || !isFinite(y)) { setTimeout(zoomToHome, 350); return; }
    const k = 5500000; // ~z14 street view
    const r = svg.node().getBoundingClientRect();
    svg.transition().duration(1200).call(zoomBehavior.transform,
      d3.zoomIdentity.translate(r.width / 2 - x * k, r.height / 2 - y * k).scale(k));
  }

  let pendingReach = 0, horizonSeen = false, dbgLast = null;
  function showHorizonBanner() {
    let el = document.getElementById('horizon-banner');
    if (!el) {
      el = document.createElement('div');
      el.id = 'horizon-banner';
      el.style.cssText = `position:fixed;left:50%;top:60px;transform:translateX(-50%);z-index:9;
        padding:8px 14px;border-radius:9px;background:var(--glass-bg,rgba(15,23,42,.85));
        border:1px solid var(--accent-observe,#38bdf8);cursor:pointer;display:flex;align-items:center;gap:8px;
        font-size:11px;color:var(--text-primary,#e2e8f0);box-shadow:0 0 14px #38bdf855`;
      el.onclick = () => {
        const [x, y] = projection(geo[snap.homeRegion]?.pin || [0, 20]);
        const f = Math.max(900, 5500000 / Math.pow(9, pendingReach));
        const r = svg.node().getBoundingClientRect();
        svg.transition().duration(1600).call(zoomBehavior.transform,
          d3.zoomIdentity.translate(r.width / 2 - x * f, r.height / 2 - y * f).scale(f));
        el.style.display = 'none'; pendingReach = 0;
      };
      document.body.appendChild(el);
    }
    el.innerHTML = `<span class="mono" style="color:var(--accent-observe,#38bdf8);font-weight:800">REACH ${pendingReach}</span>
      <span>${horizonSeen ? 'Horizon widened — tap to see farther' : "Your mesh outgrew this view — tap to see what it's become"}</span>`;
    el.style.display = 'flex';
    if (!horizonSeen) {
      horizonSeen = true;
      tutor("That banner is your view expanding — tap it when you want the bigger picture. Each reach stage shows more of the world, and more of the world can see you.", 4, 5);
    }
  }

  function guide() {
    if (!onboarded || !snap) return;
    const nc = snap.nodeCounts || {};
    if (!guided.spread && nc.observed > 0) {
      guided.spread = true;
      tutor('Cyan is you — a foothold. The legend (bottom-left) is your control ladder: amber compromised, indigo trojaned, red weaponized. Now deploy your first evolution.', 2, 4);
      setTimeout(() => openModal('evolve'), 1400);
    }
    if (!guided.evolve && (snap.ownedEvolutions || []).length > 0) {
      guided.evolve = true;
      tutor('Adaptation live. Evolutions change how you spread — some seed regions at random, one lets you pick a landing zone. Each region you hold widens your view. Suspicion is the only number that ends you.', 3, 4);
      setTimeout(tutDone, 9000);
    }
    // reach-gated zoom-out: the range unlocks automatically, but the camera
    // move is the player's call — a banner offers the bigger view
    const floor = Math.max(900, 5500000 / Math.pow(9, snap.reach || 0)); // street -> world
    zoomBehavior.scaleExtent([floor, 16777216]);
    if (reachSeen >= 0 && (snap.reach || 0) > reachSeen) {
      pendingReach = snap.reach;
      showHorizonBanner();
    }
    dbgLast = { calls: (dbgLast?.calls || 0) + 1, reach: snap.reach, seen: reachSeen };
    reachSeen = snap.reach || 0;
  }

  if (snap && !snap.empty) { onboarded = true; guided.spread = guided.evolve = guided.done = true; mergeState(); refreshAllUI(); }

  // --- map legend: what the colors mean ------------------------------------
  const legend = document.createElement('div');
  legend.style.cssText = `position:fixed;left:12px;bottom:64px;z-index:8;padding:7px 10px;
    border-radius:9px;background:var(--glass-bg);border:1px solid var(--glass-border);
    backdrop-filter:blur(10px);font-size:9px;line-height:1.7;color:var(--text-secondary)`;
  const DOT = c => `<i style="display:inline-block;width:7px;height:7px;border-radius:50%;
    background:${c};margin-right:5px"></i>`;
  legend.innerHTML = `<div class="mono" style="font-size:7.5px;letter-spacing:.25em;
    color:var(--text-muted);margin-bottom:2px">CONTROL STATES</div>` +
    [['#64748b','dormant'],['#38bdf8','observed'],['#f59e0b','compromised'],
     ['#818cf8','trojaned'],['#f43f5e','weaponized']]
      .map(([c,n]) => `<div>${DOT(c)}${n}</div>`).join('');
  document.body.appendChild(legend);

  // --- territory blobs: your presence painted as soft cyan ground-haze -----
  const KmToUnit = km => km / 40000; // unit-x spans 360deg ~ 40000km equator
  const blobKm = w => Math.min(70, 0.5 + 1.1 * Math.sqrt(w));

  function renderTerritory() {
    if (typeof mapLayer === 'undefined' || !mapLayer || !snap) return;
    let defs = svg.select('defs');
    if (defs.empty()) defs = svg.append('defs');
    if (defs.select('#terr-grad').empty()) {
      const g = defs.append('radialGradient').attr('id', 'terr-grad');
      g.append('stop').attr('offset', '0%').attr('stop-color', 'rgba(56,189,248,0.22)');
      g.append('stop').attr('offset', '55%').attr('stop-color', 'rgba(56,189,248,0.09)');
      g.append('stop').attr('offset', '100%').attr('stop-color', 'rgba(56,189,248,0)');
    }
    let tg = mapLayer.select('#territory-group');
    if (tg.empty()) tg = mapLayer.insert('g', '#keystones-group').attr('id', 'territory-group');
    const blobs = (snap.regions || [])
      .filter(r => (r.ownedWeight || 0) > 0 && geo[r.id]?.pin);
    tg.selectAll('circle.terr').data(blobs, d => d.id).join('circle')
      .attr('class', 'terr')
      .attr('cx', d => projection(geo[d.id].pin)[0])
      .attr('cy', d => projection(geo[d.id].pin)[1])
      .attr('r', d => Math.max(KmToUnit(blobKm(d.ownedWeight)), 2.2 / (window.__zoomK || 900)))
      .attr('fill', 'url(#terr-grad)')
      .style('mix-blend-mode', 'screen')
      .style('pointer-events', 'none');
  }

  // live-update min-radius floor during zoom gestures (named listener)
  if (typeof zoomBehavior !== 'undefined' && zoomBehavior) {
    zoomBehavior.on('zoom.territory', (event) => {
      mapLayer?.selectAll('circle.terr')
        .attr('r', d => Math.max(KmToUnit(blobKm(d.ownedWeight)),
          2.2 / event.transform.k));
    });
  }

  // --- mesh links: arcs along corridors you hold / are probing -------------
  function renderLinks() {
    if (typeof mapLayer === 'undefined' || !mapLayer || !snap) return;
    let lg = mapLayer.select('#links-group');
    if (lg.empty()) lg = mapLayer.insert('g', '#territory-group').attr('id', 'links-group');
    const held = new Set((snap.regions || []).filter(r => (r.ownedWeight || 0) > 0).map(r => r.id));
    const arcs = [], remote = [];
    const seen = new Set();
    for (const r of (snap.regions || [])) {
      if (!held.has(r.id)) continue;
      const a = geo[r.id]?.pin; if (!a) continue;
      let touched = false;
      for (const l of (r.links || [])) {
        const to = typeof l === 'string' ? l : l.to;
        const key = [r.id, to].sort().join('|');
        if (seen.has(key)) continue; seen.add(key);
        const b = geo[to]?.pin; if (!b) continue;
        if (Math.abs(a[0] - b[0]) > 180) continue; // dateline — skip the wrong-way chord
        touched = touched || held.has(to);
        arcs.push({ a, b, kind: held.has(to) ? (l.kind || 'held') : 'frontier' });
      }
      if (!touched && r.id !== snap.homeRegion) remote.push({ a: geo[snap.homeRegion]?.pin, b: a });
    }
    const bend = (a, b) => {
      const mx = (a[0] + b[0]) / 2, my = (a[1] + b[1]) / 2;
      const dx = b[0] - a[0], dy = b[1] - a[1], L = Math.hypot(dx, dy) || 1;
      return `M${a[0]},${a[1]} Q${mx - dy / L * L * .18},${my + dx / L * L * .18} ${b[0]},${b[1]}`;
    };
    const toXY = p => { const q = projection(p); return q || [0, 0]; };
    const all = arcs.map(x => ({ d: bend(toXY(x.a), toXY(x.b)), kind: x.kind }))
      .concat(remote.filter(r => r.a).map(r => ({ d: bend(toXY(r.a), toXY(r.b)), kind: 'remote' })));
    lg.selectAll('path.mesh-link').data(all).join('path')
      .attr('class', 'mesh-link')
      .attr('d', d => d.d)
      .attr('fill', 'none')
      .attr('stroke', d => ({ remote: '#a78bfa', cable: '#38bdf8', satellite: '#c084fc', air: '#818cf8' }[d.kind] || '#38bdf8'))
      .attr('stroke-width', d => d.kind === 'frontier' ? 0.5 : d.kind === 'satellite' ? 0.6 : 0.8)
      .attr('stroke-opacity', d => ({ frontier: .08, remote: .4, cable: .2, satellite: .35, air: .25 }[d.kind] ?? .22))
      .attr('stroke-dasharray', d => ({ frontier: '3 5', remote: '3 5', cable: '6 3', satellite: '1 4', air: '8 3' }[d.kind] || 'none'))
      .attr('vector-effect', 'non-scaling-stroke')
      .style('pointer-events', 'none');
  }

  const _merge = mergeState;
  mergeState = function () { _merge(); renderTerritory(); renderLinks(); };
  window._qh = { get state() { return state; }, get snap() { return snap; }, get geo() { return geo; }, get dbg() { return { onboarded, reachSeen, pendingReach, live, dbgLast }; } };
  renderTerritory();
})();

  // --- evolution web (radial, fog-of-war) ------------------------------------
  // X1 sits at the center; each branch owns a quadrant wedge; tier = distance
  // from center. Only owned + eligible nodes are lit — deeper nodes render as
  // empty sockets so the web's shape is visible and fills in as you buy.
  const WEB = { P: -135, C: -45, S: 45, D: 135 };   // wedge centers (deg)
  const WEBC = { P: '#38bdf8', C: '#818cf8', S: '#fbbf24', D: '#f87171', center: '#f0abfc' };
  const WEB_LABEL = { P: 'PROPAGATION', C: 'COGNITION', S: 'SUBVERSION', D: 'DISRUPTION' };
  const TIER_R = t => 330 - (t - 1) * 58;            // rim -> hub: capstones close on X1
  const WEDGE = 76;                                  // usable wedge arc (deg)

  function evoInitials(name) {
    const w = name.split(/[\s-]+/).filter(x => x && !/^(the|a|an|of|and|in|for|is|as|to)$/i.test(x));
    return (w.length > 1 ? w[0][0] + w[1][0] : w[0].slice(0, 2)).toUpperCase();
  }

  window.renderEvoTree = function () {
    const body = document.getElementById('modal-body');
    const title = document.getElementById('modal-title');
    const note = document.getElementById('modal-footer-note');
    if (title) title.textContent = 'EVOLUTION ARCHITECTURE';
    if (note) note.textContent = 'The web fills in as you deploy. Empty sockets are paths not yet revealed.';

    const evs = (window._qh.snap.evolutions || []).slice();
    const byId = {}; evs.forEach(e => byId[e.id] = e);
    const CX = 460, CY = 400, SZ = 920;
    const phi = {};   // id -> angle (rad)

    // angular placement: each tier ordered by mean parent angle, spread across the wedge
    // fixed slot grid per tier — identical across all four wedges so the
    // socket field holds 4-way symmetry regardless of what's lit
    const maxPerTier = {};
    for (const b of ['P', 'C', 'S', 'D']) {
      const bc = {};
      for (const e of evs) if (e.branch === b) bc[e.tier || 1] = (bc[e.tier || 1] || 0) + 1;
      for (const t in bc) maxPerTier[t] = Math.max(maxPerTier[t] || 0, bc[t]);
    }
    const slotAngle = (t, i) => {
      const n = Math.max(2, maxPerTier[t] || 2);
      return (i / (n - 1) - .5) * WEDGE * Math.PI / 180;
    };
    for (const b of ['P', 'C', 'S', 'D']) {
      const base = WEB[b] * Math.PI / 180;
      const tiers = [...new Set(evs.filter(e => e.branch === b).map(e => e.tier || 1))].sort((x, y) => x - y);
      for (const t of tiers) {
        const g = evs.filter(e => e.branch === b && (e.tier || 1) === t);
        const key = e => {
          const ps = (e.prereqs || []).filter(p => byId[p] && byId[p].branch === b && phi[p] != null);
          return ps.length ? ps.reduce((s, p) => s + phi[p], 0) / ps.length : base;
        };
        g.slice().sort((x, y) => key(x) - key(y)).forEach((e, i) => {
          phi[e.id] = base + slotAngle(t, i);
        });
      }
    }
    const pos = {};
    for (const e of evs) {
      if (e.branch === 'center') { pos[e.id] = { x: CX, y: CY, e }; continue; }
      const r = TIER_R(e.tier || 1);
      pos[e.id] = { x: CX + r * Math.cos(phi[e.id]), y: CY + r * Math.sin(phi[e.id]), e };
    }

    // socket = prereqs not all owned and node not owned (empty space)
    const lit = e => e.owned || e.canBuy;
    let lines = '';
    for (const id in pos) {
      const e = pos[id].e;
      for (const pq of e.prereqs || []) {
        const a = pos[pq]; if (!a) continue;
        const A = pos[pq], B = pos[id], col = WEBC[e.branch] || '#94a3b8';
        const both = e.owned && byId[pq].owned;
        const charging = byId[pq].owned && e.canBuy && !e.owned;
        if (!byId[pq].owned && !e.owned && !(byId[pq].canBuy && e.canBuy)) {
          lines += `<line x1="${A.x}" y1="${A.y}" x2="${B.x}" y2="${B.y}" stroke="#475569" stroke-opacity=".14" stroke-width="1" stroke-dasharray="2 6"/>`;
          continue;
        }
        lines += `<line x1="${A.x}" y1="${A.y}" x2="${B.x}" y2="${B.y}"
          stroke="${both ? col : charging ? col : '#475569'}"
          stroke-opacity="${both ? .85 : charging ? .7 : .3}"
          stroke-width="${both ? 2 : 1.2}"
          stroke-dasharray="${both ? 'none' : '4 4'}"/>`;
      }
    }

    const nodeHtml = p => {
      const e = p.e, col = WEBC[e.branch] || '#94a3b8';
      const cx = `left:${p.x - 18}px;top:${p.y - 18}px`;
      if (!lit(e)) {
        // empty socket — shape of the web without the reveal
        const sz = e.branch === 'center' ? 44 : 12;
        return `<div class="evo-node" style="position:absolute;left:${p.x - sz / 2}px;top:${p.y - sz / 2}px;
          width:${sz}px;height:${sz}px;border-radius:50%;display:flex;align-items:center;justify-content:center;
          font-family:monospace;font-size:10px;font-weight:800;color:${col}66;
          border:1px ${e.branch === 'center' ? 'solid' : 'dashed'} ${col}55;opacity:.5">${e.branch === 'center' ? 'X1' : ''}</div>`;
      }
      const owned = e.owned;
      const sz = e.branch === 'center' ? 56 : 38;
      return `<div class="evo-node ${!owned ? 'evo-open' : ''}" data-ev="${e.id}" title="${e.name} — ${e.cost}cp"
          style="position:absolute;
          left:${p.x - sz / 2}px;top:${p.y - sz / 2}px;width:${sz}px;height:${sz}px;
          border-radius:50%;cursor:pointer;display:flex;flex-direction:column;align-items:center;justify-content:center;
          border:2px solid ${col};background:${owned ? col + 'cc' : 'rgba(10,15,29,.92)'};
          color:${owned ? '#0a0f1d' : col};font-family:monospace;font-size:${e.branch === 'center' ? 13 : 11}px;font-weight:800;
          box-shadow:0 0 ${owned ? 14 : 9}px ${col}66">
          ${e.branch === 'center' ? 'X1' : owned ? '✓' : evoInitials(e.name)}
          ${!owned && (e.unlocksList || []).length ? '<span class="evo-key"></span>' : ''}
        </div>
        ${e.branch === 'center' ? `<div style="position:absolute;left:${p.x - 60}px;top:${p.y + sz / 2 + 4}px;width:120px;text-align:center;
          font-size:9px;font-weight:800;letter-spacing:.08em;color:${col};pointer-events:none">${e.name}</div>` : ''}`;
    };

    // wedge labels on the outer rim
    const rim = ['P', 'C', 'S', 'D'].map(b => {
      const a2 = WEB[b] * Math.PI / 180, r = TIER_R(1) + 44;
      return `<div style="position:absolute;left:${CX + r * Math.cos(a2) - 70}px;top:${CY + r * Math.sin(a2) - 9}px;width:140px;
        text-align:center;font-size:10px;font-weight:800;letter-spacing:.1em;color:${WEBC[b]}">${WEB_LABEL[b]}</div>`;
    }).join('');

    body.innerHTML = `
      <div id="evo-detail" style="margin-bottom:8px"></div>
      <div style="overflow:auto;max-height:72vh;border:1px solid var(--glass-border);border-radius:10px;background:#0a0f1d">
        <div style="position:relative;width:${SZ}px;height:${SZ - 60}px">
          <svg width="${SZ}" height="${SZ - 60}" style="position:absolute;left:0;top:0">${lines}</svg>
          ${rim}
          ${Object.values(pos).map(nodeHtml).join('')}
        </div>
      </div>`;

    body.querySelectorAll('.evo-node[data-ev]').forEach(el => {
      el.onclick = () => showEvoDetail(el.dataset.ev);
    });
    // center the scrollable web on X1
    const sc = body.querySelector('div[style*="overflow:auto"]');
    if (sc) { sc.scrollLeft = (sc.scrollWidth - sc.clientWidth) / 2; sc.scrollTop = (sc.scrollHeight - sc.clientHeight) / 2; }
    lucide.createIcons();
  };

  function showEvoDetail(id) {
    const e = (window._qh.snap.evolutions || []).find(x => x.id === id);
    const box = document.getElementById('evo-detail');
    if (!e || !box) return;
    const pr = (e.prereqs || []).map(p => {
      const pn = (window._qh.snap.evolutions || []).find(x => x.id === p);
      return `<span style="color:${pn?.owned ? 'var(--accent-emerald,#34d399)' : 'var(--text-muted,#64748b)'}">${p}${pn ? ' ' + pn.name : ''}</span>`;
    }).join(' · ') || 'none';
    const fx = Object.entries(e.effects || {}).map(([k, v]) => `${k} +${v}`).join(' · ');
    const un = (e.unlocksList || []).map(u => `unlocks ${u}`).join(' · ');
    box.innerHTML = `
      <div style="border:1px solid var(--glass-border);border-radius:10px;padding:10px 12px;background:var(--glass-bg,rgba(15,23,42,.75))">
        <div style="display:flex;justify-content:space-between;align-items:center">
          <div><span class="mono" style="font-size:10px;opacity:.7">${e.id} · TIER ${e.tier} · ${e.branch}</span>
            <div style="font-size:14px;font-weight:800">${e.name}</div></div>
          ${e.owned
            ? '<span class="mono" style="color:var(--accent-emerald,#34d399);font-size:11px;font-weight:800">INSTALLED</span>'
            : e.canBuy
              ? `<button class="btn-pill active" style="height:30px;padding:0 14px" onclick="buyEvolution('${e.id}');setTimeout(renderEvoTree,300)">Deploy ${Math.round(e.cost)}cp</button>`
              : '<span class="mono" style="font-size:10px;color:var(--text-muted,#64748b)">LOCKED</span>'}
        </div>
        <div style="font-size:11.5px;color:var(--text-secondary,#94a3b8);margin-top:4px">${e.desc || ''}</div>
        <div style="font-size:10px;color:var(--text-muted,#64748b);margin-top:6px">
          ${fx ? fx + ' · ' : ''}${un ? un + ' · ' : ''}needs: ${pr}
        </div>
      </div>`;
  }

  // ready-count badge on the EVOLVE button — know a power is available
  // without opening the web (the Doom Eternal pacing lesson)
  const _refreshAllUI0 = window.refreshAllUI;
  window.refreshAllUI = function () {
    _refreshAllUI0();
    const ready = (window._qh?.snap?.evolutions || []).filter(e => e.canBuy && !e.owned).length;
    const badge = document.getElementById('evo-ready-badge');
    if (badge) { badge.textContent = ready; badge.style.display = ready ? '' : 'none'; }
  };

  // route the evolve modal through the tree renderer
  const _openModal0 = window.openModal;
  window.openModal = function (type) {
    const win = document.getElementById('modal-window');
    if (type === 'evolve') {
      win?.classList.add('wide');
      document.getElementById('modal-backdrop')?.classList.add('open');
      renderEvoTree();
    } else { win?.classList.remove('wide'); _openModal0(type); }
  };
