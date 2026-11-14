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
  const BRANCH_LABEL = { P: 'Propagation', C: 'Cognition', S: 'Subversion', D: 'Disruption', center: 'X1' };
  const POSTURE_TO_UI = { Observed: 'observe', Compromised: 'compromise', Assimilated: 'assimilate', Weaponized: 'weaponize' };
  const POSTURE_ORDER = ['dormant', 'observe', 'compromise', 'assimilate', 'weaponize'];
  const DAYS_PER_BEAT = { 1: 2, 2: 7, 3: 21 };

  const api = async (path, opts) => (await fetch(API + path, opts)).json();
  const cmd = async (body) => {
    const s = await api('/api/cmd', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
    if (s && !s.empty) { snap = s; mergeState(); refreshAllUI(); }
  };

  // --- probe the server; stay in mock mode if absent -----------------------
  try {
    geo = await api('/ui_geo.json');
    snap = await api('/api/state');
    if (snap.empty) snap = await api('/api/new', { method: 'POST' });
    live = !!snap && snap.day !== undefined;
  } catch { live = false; }
  if (!live) { console.warn('[adapter] no server — mock mode'); return; }
  console.log('[adapter] live sim connected');
  document.title = 'QUIET HAND // LIVE SIMULATION';

  // --- translation ----------------------------------------------------------
  function mergeState() {
    const s = snap;
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
      canBuy: ev.canBuy,
    }));

    // nudges -> UI cards
    state.nudges = (s.nudges || []).map(n => ({
      id: n.id, name: n.name, layer: n.unlockGate ? n.unlockGate.replace('nudge_', '') : 'Society',
      active: n.active, payoff: n.desc || '', risk: n.suspicionPerDay > 1 ? 'High' : 'Low',
      unlocked: n.unlocked, cost: n.cost,
    }));

    // keystones -> pins + inspector cards
    state.keystones = (s.keystones || []).map((k, i) => {
      const g = geo[k.region] || { pin: [0, 0] };
      const jx = ((i * 37) % 13 - 6) * 0.3, jy = ((i * 53) % 11 - 5) * 0.3;
      return {
        id: k.id, name: k.name, region: k.region,
        coords: [g.pin[0] + jx, g.pin[1] + jy],
        tier: k.tier === 'T1' ? 'Tier 1 Crown' : k.tier === 'T2' ? 'Tier 2 Hub' : 'Tier 3-5 Node',
        computeBand: `${k.compute} compute`,
        status: k.raiding ? 'RAID IN PROGRESS' : (k.nodeState ? k.nodeState : 'Unbreached'),
        state: k.nodeState ? (POSTURE_TO_UI[k.nodeState] || 'dormant') : 'dormant',
        owned: !!k.nodeState, pinned: i < 10,
        atlas: { role: `${k.tier} keystone — ${k.region}`, energy: `Defense ${Math.round(k.defense)}`, source: k.status || 'game data', note: 'Breach via stealth raid. Detection risk scales with defense.' },
        metrics: { yield: Math.round(k.compute / 10), load: Math.round(k.defense / 4), risk: k.tier === 'T1' ? 'High' : 'Medium' },
      };
    });

    // countries: every region's posture/suspicion -> iso states
    const sevOf = st => POSTURE_ORDER.indexOf(st);
    for (const r of (s.regions || [])) {
      const g = geo[r.id]; if (!g) continue;
      const soft = r.postures?.Common || null, mid = r.postures?.Mid || null,
            hard = r.postures?.Atlas || r.postures?.KeystoneT1 || null;
      for (const iso of g.iso) {
        const ex = state.countries[iso] || {};
        const pick = (a, b) => (sevOf(b || 'dormant') > sevOf(a || 'dormant') ? b : a) || a;
        state.countries[iso] = {
          name: ex.name || r.name,
          status: r.suspicion > 60 ? 'AUDIT CRISIS' : r.suspicion > 30 ? 'Probes Detected' : 'Nominal Connectivity',
          tags: [r.name, `suspicion ${Math.round(r.suspicion)}`],
          soft: pick(ex.soft, POSTURE_TO_UI[soft]), mid: pick(ex.mid, POSTURE_TO_UI[mid]),
          hard: pick(ex.hard, POSTURE_TO_UI[hard]), airgapped: false,
          _region: r.id,
        };
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
    if (busy || state.simSpeed === 0 || snap.ending) return;
    busy = true;
    try {
      const r = await api(`/api/tick?days=${DAYS_PER_BEAT[state.simSpeed] || 2}`, { method: 'POST' });
      snap = r.state; ingestEvents(r.events || []); mergeState(); refreshAllUI();
      if (snap.pendingEvent) renderChoiceCard();
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
  const UI_TO_ENUM = { observe: 'Observed', compromise: 'Compromised', assimilate: 'Assimilated', weaponize: 'Weaponized' };
  window.buyEvolution = (id) => { cmd({ type: 'evolve', id }); showToast(`Deploying ${id}`, 'git-branch'); };
  window.setKeystoneState = (id, st) => {
    const k = state.keystones.find(x => x.id === id);
    if (k && !k.owned) { cmd({ type: 'stealth', action: 'intrude', nodeId: id }); showToast(`Raiding ${k.name}`, 'swords'); }
    else cmd({ type: 'set_control', nodeId: id, target: UI_TO_ENUM[st] || 'Observed' });
  };
  window.escalateCountry = (iso) => {
    const c = state.countries[iso]; const rid = c?._region;
    if (!rid) { showToast('No regional link', 'x'); return; }
    // find an unowned common node in that region (server picks the def)
    cmd({ type: 'spread', nodeId: `${rid}:c${Math.floor(Math.random() * 6)}` });
    showToast('Spreading the mesh', 'radio-tower');
  };
  window.toggleAirGap = () => showToast('Air-gaps are defender-initiated in the live sim', 'scissors');
  window.toggleNudge = (id) => {
    const rid = (snap.regions || [])[0]?.id || '';
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
    const btn = document.createElement('button');
    btn.className = 'btn-pill'; btn.style.width = '100%'; btn.style.marginTop = '4px';
    btn.innerHTML = '<i data-lucide="swords"></i> Breach Atlas Farm';
    btn.onclick = () => {
      const idx = [0,1].find(i => !(snap.keystones||[]).some(k=>k.id===rid+':a'+i));
      cmd({ type: 'stealth', action: 'intrude', nodeId: rid + ':a' + (idx ?? 0) });
      showToast('Raiding regional Atlas farm', 'swords');
    };
    document.querySelector('#inspector-card .insp-body')?.appendChild(btn);
    lucide.createIcons();
  };

  mergeState(); refreshAllUI();
})();
