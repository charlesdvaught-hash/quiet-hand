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
  const UI_TO_ENUM = { observe: 'Observed', compromise: 'Compromised', assimilate: 'Assimilated', weaponize: 'Weaponized' };
  window.buyEvolution = (id) => { cmd({ type: 'evolve', id }); showToast(`Deploying ${id}`, 'git-branch'); };
  window.setKeystoneState = (id, st) => {
    const k = state.keystones.find(x => x.id === id);
    const kreg = (snap.regions || []).find(r => r.id === k?.region);
    if (k && !k.owned) {
      if (!reachRegions().ok(kreg?.id)) { showToast('Out of reach — bring your mesh closer or evolve Proxy Subagents', 'lock'); return; }
      cmd({ type: 'stealth', action: 'intrude', nodeId: id }); showToast(`Raiding ${k.name}`, 'swords');
    }
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
    try { await fetch(BASE + '/api/abandon', { method: 'POST' }); } catch {}
    location.reload();
  };
  window.toggleAirGap = () => showToast('Air-gaps are defender-initiated in the live sim', 'scissors');
  window.toggleNudge = (id) => {
    const reach = reachRegions();
    const rid = (snap.regions || []).find(r => reach.ok(r.id))?.id || '';
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
      const idx = [0,1].find(i => !(snap.keystones||[]).some(k=>k.id===rid+':a'+i));
      cmd({ type: 'stealth', action: 'intrude', nodeId: rid + ':a' + (idx ?? 0) });
      showToast('Raiding regional Atlas farm', 'swords');
    };
    document.querySelector('#inspector-card .insp-body')?.appendChild(btn);
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
            <div class="mono" style="font-size:10px;color:var(--text-muted);margin-top:4px">${o.startingCompute}cp reserve · ${o.computeRate}/day</div>
          </div>`).join('')}
      </div>
      <div style="font-size:10px;color:var(--text-muted)">+25 compute starter reserve on first run</div>`;
    document.body.appendChild(ov);
    ov.querySelector('[data-continue]')?.addEventListener('click', async () => {
      snap = await api('/api/continue', { method: 'POST' });
      ov.remove(); onboarded = true; reachSeen = -1; guided.done = true;
      mergeState(); refreshAllUI(); zoomToHome(); guide();
      showToast('Mesh restored — day ' + snap.day, 'power');
    });
    ov.querySelectorAll('[data-oid]').forEach(el => el.onclick = async () => {
      snap = await api(`/api/new?origin=${el.dataset.oid}&bonus=25`, { method: 'POST' });
      ov.remove(); onboarded = true; reachSeen = -1;
      state.posts = AMBIENT.map((p, i) => ({
        id: postSeq++, handle: p[0], persona: p[1], badge: p[2], text: p[3], state: 'normal',
      }));
      mergeState(); refreshAllUI(); zoomToHome(); guide();
      tutor('This is your street — the lights below are real blocks. Your mesh creeps outward on its own, block by block. To push faster, tap a neighboring area — a paid burst into adjacent ground.', 1, 4);
    });
  }

  function zoomToHome() {
    const g = geo[snap.homeRegion];
    if (!g || typeof projection === 'undefined') return;
    if (!(topoFeatures || []).length) { setTimeout(zoomToHome, 350); return; } // wait for fit
    const [x, y] = projection(g.pin);
    if (!isFinite(x) || !isFinite(y)) { setTimeout(zoomToHome, 350); return; }
    const k = 5500000; // ~z14 street view
    svg.transition().duration(1200).call(zoomBehavior.transform,
      d3.zoomIdentity.translate(500 - x * k, 250 - y * k).scale(k));
  }

  function guide() {
    if (!onboarded || !snap) return;
    const nc = snap.nodeCounts || {};
    if (!guided.spread && nc.observed > 0) {
      guided.spread = true;
      tutor('Cyan is you — a foothold. The legend (bottom-left) is your control ladder: amber compromised, indigo assimilated, red weaponized. Now deploy your first evolution.', 2, 4);
      setTimeout(() => openModal('evolve'), 1400);
    }
    if (!guided.evolve && (snap.ownedEvolutions || []).length > 0) {
      guided.evolve = true;
      tutor('Adaptation live. Evolutions change how you spread — some seed regions at random, one lets you pick a landing zone. Each region you hold widens your view. Suspicion is the only number that ends you.', 3, 4);
      setTimeout(tutDone, 9000);
    }
    // reach-gated zoom-out: the world reveals itself as you grow
    const floor = Math.max(900, 5500000 / Math.pow(9, snap.reach || 0)); // street -> world
    zoomBehavior.scaleExtent([floor, 16777216]);
    if (reachSeen >= 0 && (snap.reach || 0) > reachSeen) {
      const [x, y] = projection(geo[snap.homeRegion]?.pin || [0, 20]);
      svg.transition().duration(1400).call(zoomBehavior.transform,
        d3.zoomIdentity.translate(500 - x * floor, 250 - y * floor).scale(floor));
    }
    reachSeen = snap.reach || 0;
  }

  if (snap && !snap.empty) { mergeState(); refreshAllUI(); }

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
     ['#818cf8','assimilated'],['#f43f5e','weaponized']]
      .map(([c,n]) => `<div>${DOT(c)}${n}</div>`).join('');
  document.body.appendChild(legend);

  // --- territory blobs: your presence painted as soft cyan ground-haze -----
  const KmToUnit = km => km / 40000; // unit-x spans 360deg ~ 40000km equator
  const blobKm = w => Math.min(120, 0.5 + 1.6 * Math.sqrt(w));

  function renderTerritory() {
    if (typeof mapLayer === 'undefined' || !mapLayer || !snap) return;
    let defs = svg.select('defs');
    if (defs.empty()) defs = svg.append('defs');
    if (defs.select('#terr-grad').empty()) {
      const g = defs.append('radialGradient').attr('id', 'terr-grad');
      g.append('stop').attr('offset', '0%').attr('stop-color', 'rgba(56,189,248,0.34)');
      g.append('stop').attr('offset', '55%').attr('stop-color', 'rgba(56,189,248,0.14)');
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

  const _merge = mergeState;
  mergeState = function () { _merge(); renderTerritory(); };
  window._qh = { get state() { return state; }, get snap() { return snap; }, get geo() { return geo; } };
  renderTerritory();
})();
