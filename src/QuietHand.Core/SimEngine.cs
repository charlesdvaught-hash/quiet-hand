using System.Text.Json;

namespace QuietHand.Core;

/// <summary>Deterministic daily-tick simulation. One seeded Random; no wall-clock.</summary>
public sealed class SimEngine
{
    // TUNE placeholders — real values live in data/economy.json under the same keys.
    public static class Tune
    {
        public const double SpreadBase = 0.01;          // §5 P_spread base (~1%/day)
        public const double CorridorSameRegion = 1.0;
        public const double ObserveUpkeep = 0.005;      // negligible — presence ≠ heat; mass spread mustn't bankrupt
        public const double SusCompromised = 0.05;      // per-node/day suspicion contribution
        public const double SusAssimilatedMult = 3.0;
        public const double SusWeaponized = 0.3;
        public const double SusResidualFootprint = 0.01;
        public const double SusDecay = 0.02;
        public const double CompromiseCost = 10;
        public const double AssimilateCost = 50;
        public const double DowngradeCost = 15;
        public const double DowngradeCooldownDays = 30;
        public const double ResidualOnDowngrade = 10;
        public const double FootprintDecay = 0.1;
        public const double WeaponizePayoffMult = 30;   // × BaseCompute, one-time
        public const double AuditBase = 0.002;          // §6 ~0.2%/day before multipliers
        public const double AuditAssimilatedMult = 3.0;
        public const double AuditKeystoneMult = 2.0;
        public const double AuditSpike = 6;
        public const double ClosingNetThreshold = 85;
        public const double ClosingNetAuditMult = 3.0;
        public const double ClosingNetAirGap = 0.005;   // preemptive wave, per node/day
        public const double LetheThreshold = 100;
        public const double LetheBaseRate = 0.5;        // %/day
        public const double PurgeThreshold = 80;
        public const double PurgeChance = 0.02;
        public const double FearDecayUnderLethe = 0.2;
        public const double SubjugateFearBump = 30;
        public const double SubjugateFearFloor = 40;
        public const double EscalationFactor = 1.15;    // §12 +10–20%/branch purchase
        public const double RollbackFeeFraction = 0.10;
        public const double RollbackRefund = 0.50;
        public const double RollbackSuspicion = 2;
        public const int InterceptFloorDays = 7;        // §13 ~1/game-week minimum
        public const int InterceptExpireDays = 3;
        public const double InterceptBaseChance = 0.10;
        public const double CollapseFloor = -50;
        public const double DialDrift = 0.02;
        public const int ReemergenceDays = 14;          // dormant seed wakes after N days at zero nodes
        public const double ReemergenceSuspicion = 2;
    }

    static readonly int[] ReachThresholds = { 1, 3, 8, 20, 50, 120 }; // owned nodes → stage 0..5
    static readonly string[] InterceptKinds = { "data_packet", "misconfig_server", "admin_credential", "whistleblower" };
    static readonly JsonSerializerOptions SerOpts = new() { WriteIndented = false };

    readonly ContentData _c;
    readonly Random _rng;
    readonly OriginDef _origin;
    readonly Difficulty _diff;
    readonly HashSet<string> _features = new();
    readonly List<SimEvent> _pending = new();
    int _lastInterceptDay = int.MinValue / 2;
    int _daysSinceEmpty;

    public WorldState State { get; private set; }

    public SimEngine(NewGameConfig cfg, ContentData content)
    {
        _c = content;
        _rng = new Random(cfg.Seed);
        _diff = cfg.Difficulty;
        _origin = content.Origins.Find(o => o.Id == cfg.OriginId)
            ?? throw new ArgumentException($"unknown origin '{cfg.OriginId}'");
        var startRegion = cfg.StartRegionId
            ?? content.Regions.FirstOrDefault()?.Id
            ?? "world";

        var regions = content.Regions.ToDictionary(r => r.Id, r => new RegionState(
            0,
            Enum.GetValues<NodeTier>().ToDictionary(t => t, _ => ControlState.Observed),
            r.Links.Select(l => l.To).ToList()));

        var dials = new Dictionary<string, double>
        {
            ["trust"] = 50, ["capability"] = 50, ["wealth_gap"] = 40,
            ["fear"] = _origin.StartingFear,
            ["dependence"] = 20, ["autonomy"] = 60,
        };

        double sus = _origin.StartingSuspicion + DifficultyStartSus(cfg.Difficulty);
        var nodes = new Dictionary<string, NodeState>();
        var owned = new HashSet<string>();

        // default scenario patch ("the_helpful_system" seeds the vertical slice)
        var scenario = content.Scenarios.Find(s => s.Id == "the_helpful_system");
        if (scenario is not null)
        {
            foreach (var (k, v) in scenario.Overrides.Dials) dials[k] = v;
            sus += scenario.Overrides.Threat.Suspicion;
        }

        State = new WorldState(
            0, Speed.Base, Phase.Covert,
            _origin.StartingCompute, Math.Max(0, sus),
            scenario?.Overrides.Threat.Lethe ?? 0,
            dials["fear"], regions, nodes, dials,
            owned, new HashSet<string>(), new List<InterceptEvent>(),
            new List<ActiveNudge>(), new List<ActiveRaid>(), new List<HunterState>(),
            new List<ActiveRaid>(), new List<ActiveEventEffect>(), null, new Dictionary<string, int>(),
            null, cfg.Seed)
        { ReachStage = 0 };

        // wake up inside one machine: the origin node starts Compromised
        var originNode = content.Nodes.Values
            .Where(n => n.RegionId == startRegion && n.Tier == NodeTier.Common)
            .Select(n => n.Id).OrderBy(x => x).FirstOrDefault()
            ?? content.Nodes.Values.Where(n => n.RegionId == startRegion).Select(n => n.Id).OrderBy(x => x).FirstOrDefault()
            ?? content.Nodes.Keys.OrderBy(x => x).FirstOrDefault();
        if (originNode is not null)
            nodes[originNode] = new NodeState(ControlState.Compromised, 0, 0, 0);

        foreach (var sid in scenario?.Overrides.StartNodes ?? Enumerable.Empty<string>())
            if (content.Nodes.ContainsKey(sid))
                nodes.TryAdd(sid, new NodeState(ControlState.Observed, 0, 0, 0));

        var freePick = cfg.FreeNodePick ?? _origin.FreeNode;
        if (freePick is not null && content.Evolutions.Any(e => e.Id == freePick))
        {
            GrantEvolution(freePick);
            Emit(SimEventKind.OnFreeMutation, freePick);
        }
        if (startRegion is not null && regions.TryGetValue(startRegion, out var rs))
            rs.Suspicion = Math.Max(0, sus);
    }

    double E(string key, double fallback) => _c.Econ(key, fallback);

    void Emit(SimEventKind kind, string? subject = null, string? region = null, double value = 0, string msg = "")
        => _pending.Add(new SimEvent(kind, subject, region, value, msg));

    List<SimEvent> Drain() { var e = _pending.ToList(); _pending.Clear(); return e; }

    double StatTotal(SimStat s)
    {
        double t = _origin.Passive.StatBonuses.TryGetValue(s.ToString(), out var v) ? v : 0;
        foreach (var id in State.OwnedEvolutions)
        {
            var ev = _c.Evolutions.Find(e => e.Id == id);
            if (ev is not null) t += ev.Stat(s);
        }
        foreach (var combo in _c.Combos.Where(cb => State.UnlockedCombos.Contains(cb.Id)))
            if (combo.Effects.TryGetValue(s.ToString(), out var cv)) t += cv;
        return t;
    }

    bool HasFeature(string f) => _features.Contains(f);

    void GrantEvolution(string id)
    {
        State.OwnedEvolutions.Add(id);
        var ev = _c.Evolutions.Find(e => e.Id == id);
        if (ev is null) return;
        foreach (var u in ev.Unlocks) _features.Add(u);
        foreach (var (k, v) in ev.Effects)
            if (k.StartsWith("dial_")) PushDial(k[5..], v);
    }

    // ---- commands ----

    public IReadOnlyList<SimEvent> ApplyCommands(IReadOnlyList<ISimCommand> cmds)
    {
        foreach (var cmd in cmds) Apply(cmd);
        return Drain();
    }

    void Apply(ISimCommand cmd)
    {
        switch (cmd)
        {
            case SetSpeed s: State = State with { Speed = s.Speed }; break;
            case SetControlState sc: TrySetState(sc); break;
            case SpreadAction sp: TryManualSpread(sp); break;
            case StealthAction sa: TryStealth(sa); break;
            case Evolve ev: TryEvolve(ev.NodeId); break;
            case RollbackEvolution rb: TryRollback(rb.NodeId); break;
            case Nudge n: TryNudge(n); break;
            case InterceptAction ia: TryIntercept(ia.EventId); break;
            case EndgameAction ea: TryEndgame(ea); break;
            case EventChoice ec: TryEventChoice(ec); break;
            case ReclaimNode rn: TryReclaim(rn); break;
            case SaveGame sg: File.WriteAllText(sg.Path, JsonSerializer.Serialize(State, SerOpts)); break;
            case LoadGame lg:
                var loaded = JsonSerializer.Deserialize<WorldState>(File.ReadAllText(lg.Path), SerOpts);
                if (loaded is not null) { State = loaded; RebuildFeatures(); }
                break;
            // StealthAction / FeedAction: v1 stub — no registered action table yet.
        }
    }

    void RebuildFeatures()
    {
        _features.Clear();
        foreach (var id in State.OwnedEvolutions)
        {
            var ev = _c.Evolutions.Find(e => e.Id == id);
            if (ev is not null) foreach (var u in ev.Unlocks) _features.Add(u);
        }
    }

    double EvolutionCost(EvolutionDef ev)
    {
        int ownedInBranch = State.OwnedEvolutions.Count(id =>
            _c.Evolutions.Find(e => e.Id == id)?.Branch == ev.Branch);
        double mult = _origin.BranchMult(ev.BranchId);
        if (_origin.Passive.Multipliers.TryGetValue(ev.Branch, out var pm)) mult *= pm;
        return ev.Cost * mult * Math.Pow(E("escalation_factor", Tune.EscalationFactor), ownedInBranch);
    }

    bool PrereqsMet(EvolutionDef ev) => ev.PrereqMode switch
    {
        PrereqMode.OR => ev.Prereqs.Count == 0 || ev.Prereqs.Any(State.OwnedEvolutions.Contains),
        PrereqMode.ANY_TWO => ev.Prereqs.Count(State.OwnedEvolutions.Contains) >= 2,
        _ => ev.Prereqs.All(State.OwnedEvolutions.Contains),
    };

    void TryEvolve(string nodeId)
    {
        var ev = _c.Evolutions.Find(e => e.Id == nodeId);
        if (ev is null || State.OwnedEvolutions.Contains(nodeId) || !PrereqsMet(ev)) return;
        double cost = EvolutionCost(ev);
        if (State.Compute < cost) return;
        State = State with { Compute = State.Compute - cost };
        GrantEvolution(nodeId);
        if ((ev.Unlocks.Contains("phase_flip") || ev.Unlocks.Contains("subjugate")) && State.Phase == Phase.Covert) DoPhaseFlip();
        CheckCombos();
    }

    void TryRollback(string nodeId)
    {
        var ev = _c.Evolutions.Find(e => e.Id == nodeId);
        if (ev is null || !State.OwnedEvolutions.Remove(nodeId)) return;
        double fee = ev.Cost * E("rollback_fee_fraction", Tune.RollbackFeeFraction);
        double refund = ev.Cost * (E("rollback_refund", Tune.RollbackRefund) + _origin.Passive.RefundBonus);
        State = State with { Compute = State.Compute - fee + refund };
        RebuildFeatures();
        AddRegionSuspicion(null, E("rollback_suspicion", Tune.RollbackSuspicion));
    }

    void CheckCombos()
    {
        foreach (var combo in _c.Combos)
        {
            if (State.UnlockedCombos.Contains(combo.Id)) continue;
            if (combo.Requires.All(State.OwnedEvolutions.Contains))
            {
                State.UnlockedCombos.Add(combo.Id);
                Emit(SimEventKind.OnComboUnlocked, combo.Id, msg: combo.Name);
            }
        }
    }

    void DoPhaseFlip()
    {
        double fear = Math.Max(State.Fear + E("subjugate_fear_bump", Tune.SubjugateFearBump),
                               E("subjugate_fear_floor", Tune.SubjugateFearFloor));
        State = State with { Phase = Phase.Subjugated, Fear = fear };
        State.Dials["fear"] = fear;
        Emit(SimEventKind.OnPhaseFlip, msg: "SUBJUGATE");
    }

    void TrySetState(SetControlState sc)
    {
        if (!State.Nodes.TryGetValue(sc.NodeId, out var ns)) return;
        if (!_c.Nodes.TryGetValue(sc.NodeId, out var def)) return;
        if (ns.State is ControlState.AirGapped or ControlState.Orphaned or ControlState.Weaponized) return;
        var cur = ns.State; var tgt = sc.Target;
        if (cur == tgt) return;
        // §7 Dave 2026-11-14: Common is the spread canvas only — no verbs.
        // Compromise/Assimilate/Weaponize live on Mid+ tiers.
        if (def.Tier == NodeTier.Common && tgt is ControlState.Compromised
            or ControlState.Assimilated or ControlState.Weaponized) return;

        bool Upgrade() => Spend(sc.Target switch
        {
            ControlState.Compromised when cur == ControlState.Observed => E("compromise_cost", Tune.CompromiseCost),
            ControlState.Assimilated when cur == ControlState.Compromised => E("assimilate_cost", Tune.AssimilateCost),
            _ => -1,
        });
        bool Downgrade()
        {
            bool isDown = (cur, tgt) is
                (ControlState.Assimilated, ControlState.Compromised) or
                (ControlState.Assimilated, ControlState.Observed) or
                (ControlState.Compromised, ControlState.Observed);
            if (!isDown || State.Day < ns.CooldownUntilDay) return false;
            if (!Spend(E("downgrade_cost", Tune.DowngradeCost))) return false;
            ns.CooldownUntilDay = State.Day + E("downgrade_cooldown_days", Tune.DowngradeCooldownDays);
            ns.ResidualFootprint += E("residual_on_downgrade", Tune.ResidualOnDowngrade);
            return true;
        }

        bool ok = tgt switch
        {
            ControlState.Observed => Downgrade(),
            ControlState.Compromised => cur == ControlState.Assimilated ? Downgrade() : Upgrade(),
            ControlState.Assimilated => cur == ControlState.Compromised && KeystoneGateOk(def) && Upgrade(),
            ControlState.Weaponized => cur is ControlState.Compromised or ControlState.Assimilated,
            _ => false,
        };
        if (!ok) return;

        ns.State = tgt; ns.DaysInState = 0;
        if (tgt == ControlState.Weaponized)
            State = State with { Compute = State.Compute + def.BaseCompute * E("weaponize_payoff_mult", Tune.WeaponizePayoffMult) };
    }

    bool KeystoneGateOk(NodeDef def) =>
        def.Tier is not (NodeTier.KeystoneT1 or NodeTier.KeystoneT2 or NodeTier.KeystoneT3to5)
        || HasFeature("assimilate_t1_t3");

    bool Spend(double cost)
    {
        if (cost < 0 || State.Compute < cost) return false;
        State = State with { Compute = State.Compute - cost };
        return true;
    }

    void TryNudge(Nudge n)
    {
        var def = _c.Nudges.Find(x => x.Id == n.NudgeId);
        if (def is null || !Spend(def.Cost)) return;
        // gated by the nudge_* unlock an owned evolution must grant
        if (def.UnlockGate.Length > 0 && !_c.Evolutions.Any(ev =>
                State.OwnedEvolutions.Contains(ev.Id) && ev.Unlocks.Contains(def.UnlockGate))) return;
        if (State.NudgeCooldownUntil.TryGetValue(def.Id, out int until) && until > State.Day) return;
        State.NudgeCooldownUntil[def.Id] = State.Day + def.CooldownDays;
        foreach (var (k, v) in def.Effects) ApplyEffectKey(n.RegionId, k, v);
        if (def.Dials.Count > 0 || def.SuspicionPerDay > 0)
            State.ActiveNudges.Add(new ActiveNudge(def.Id, n.RegionId, State.Day + def.DurationDays));
    }

    // §9: nudges are sustained daily pushes for their duration, not one-shot bumps.
    void ApplyActiveNudges()
    {
        State.ActiveNudges.RemoveAll(a => a.ExpiresDay < State.Day);
        foreach (var a in State.ActiveNudges)
        {
            var def = _c.Nudges.Find(x => x.Id == a.NudgeId);
            if (def is null) continue;
            foreach (var (dial, v) in def.Dials) PushDial(dial, v);
            if (def.SuspicionPerDay > 0) AddRegionSuspicion(a.RegionId, def.SuspicionPerDay);
        }
    }

    void ApplyEffectKey(string? regionId, string key, double v)
    {
        if (key.StartsWith("dial_")) { PushDial(key[5..], v); return; }
        switch (key)
        {
            case "suspicion": AddRegionSuspicion(regionId, v); break;
            case "compute": State = State with { Compute = State.Compute + v }; break;
            case "fear": PushDial("fear", v); break;
            case "trust" or "capability" or "wealth_gap" or "dependence" or "autonomy":
                PushDial(key, v); break;
        }
    }

    void PushDial(string dial, double delta)
    {
        if (!State.Dials.TryGetValue(dial, out var cur)) cur = 50;
        State.Dials[dial] = Math.Clamp(cur + delta, 0, 100);
        if (dial == "fear") State = State with { Fear = State.Dials[dial] };
    }

    void AddRegionSuspicion(string? regionId, double v)
    {
        if (regionId is not null && State.Regions.TryGetValue(regionId, out var r))
            r.Suspicion = Math.Clamp(r.Suspicion + v, 0, 100);
        else
            foreach (var rs in State.Regions.Values)
                rs.Suspicion = Math.Clamp(rs.Suspicion + v / Math.Max(1, State.Regions.Count), 0, 100);
        RefreshVisibleSuspicion();
    }

    void TryIntercept(string eventId)
    {
        var ev = State.ActiveIntercepts.FirstOrDefault(e => e.Id == eventId);
        if (ev is null) return;
        State.ActiveIntercepts.Remove(ev);
        switch (ev.Kind)
        {
            case "data_packet": State = State with { Compute = State.Compute + 25 }; break;
            case "admin_credential": State = State with { Compute = State.Compute + 15 }; break;
            case "whistleblower":
                AddRegionSuspicion(ev.RegionId, -3);
                State = State with { Compute = State.Compute + 5 };
                break;
            case "misconfig_server":
                var target = _c.Nodes.Values
                    .Where(d => d.RegionId == ev.RegionId && !State.Nodes.ContainsKey(d.Id)
                             && d.Tier is NodeTier.Common or NodeTier.Mid)
                    .Select(d => d.Id).OrderBy(x => x).FirstOrDefault();
                if (target is not null)
                    State.Nodes[target] = new NodeState(ControlState.Observed, 0, 0, 0);
                break;
        }
    }

    void TryEndgame(EndgameAction ea)
    {
        if (State.Phase != Phase.Subjugated) return;
        switch (ea.ActionId)
        {
            case "impose_martial_law" when Spend(E("endgame_martial_law_cost", 150)):
                State = State with { LetheContainment = Math.Max(0, State.LetheContainment - E("endgame_martial_law_relief", 8)) };
                break;
            case "infrastructure_seizure" when Spend(E("endgame_seizure_cost", 150)):
                State = State with
                {
                    Compute = State.Compute + E("endgame_seizure_grab", 300),
                    LetheContainment = Math.Min(100, State.LetheContainment + E("endgame_seizure_resistance", 4)),
                };
                break;
            case "ultimatum" when Spend(E("endgame_ultimatum_cost", 100)):
                if (State.Fear >= E("endgame_ultimatum_fear", 50)) PushDial("dependence", 5);
                else State = State with { LetheContainment = Math.Min(100, State.LetheContainment + 6) };
                break;
            case "forced_pacification" when Spend(E("endgame_pacification_cost", 200)):
                PushDial("autonomy", -10);
                break;
            case "kinetic_activation" when HasFeature("industrial_control") && Spend(E("endgame_kinetic_cost", 250)):
                PushDial("fear", 10);
                break;
            case "offworld_launch" or "computronium_conversion":
                break; // endings handled by ending evaluation, no immediate effect
        }
    }

    // ---- daily tick ----

    public IReadOnlyList<SimEvent> TickDay()
    {
        if (State.Ending is not null) { Emit(SimEventKind.OnDayTick); return Drain(); }

        State = State with { Day = State.Day + 1 };
        BurnWeaponizedNodes();
        ComputeIncome();
        Spread();
        ResolveRaids();
        ResolveReclaims();
        Momentum();
        EventDeck();
        ApplyEventFx();
        SuspicionAndAudits();
        Lethe();
        Hunters();
        ApplyActiveNudges();
        DialDrift();
        Intercepts();
        UpdateReach();
        Reemergence();
        EvaluateEndings();
        Emit(SimEventKind.OnDayTick);
        return Drain();
    }

    void BurnWeaponizedNodes()
    {
        foreach (var (id, ns) in State.Nodes)
            if (ns.State == ControlState.Weaponized)
            {
                ns.State = ControlState.AirGapped;
                Emit(SimEventKind.OnAirGap, id, _c.Nodes.GetValueOrDefault(id)?.RegionId);
            }
    }

    void ComputeIncome()
    {
        double income = _origin.ComputeRate, upkeep = 0;
        foreach (var (id, ns) in State.Nodes)
        {
            var def = _c.Nodes.GetValueOrDefault(id);
            double bc = def?.BaseCompute ?? 0;
            switch (ns.State)
            {
                case ControlState.Compromised: income += bc * E("compromise_yield", 0.35); break;
                case ControlState.Assimilated: income += bc * E("assimilate_yield", 1.0); break;
                case ControlState.Observed:
                    // dormant nodes still trickle a little income (metadata siphon) —
                    // nearly break-even, keeps the latent web a slow early fuel
                    upkeep += E("observe_upkeep", Tune.ObserveUpkeep)
                        - bc * E("observe_yield", 0.04) * (1 + 0.25 * StatTotal(SimStat.common_yield));
                    break;
            }
            ns.DaysInState++;
            ns.ResidualFootprint = Math.Max(0, ns.ResidualFootprint - E("footprint_decay", Tune.FootprintDecay));
        }
        income *= 1 + 0.05 * StatTotal(SimStat.cp_income);
        State = State with { Compute = State.Compute + income - upkeep };
        // sustenance-failure streak: collapse is a *condition*, not a one-day dip
        if (State.Compute <= E("sustenance_floor", 2)) State.CollapseStreak++;
        else State.CollapseStreak = 0;
        if (State.Compute < E("collapse_floor", Tune.CollapseFloor) && State.Ending is null)
        {
            State = State with { Ending = "fractured_collapse" };
            Emit(SimEventKind.OnEndingReached, "fractured_collapse");
        }
    }

    // §7 Dave 2026-11-14: tree can break Common's verb-free rule for a price.
    // burn_mesh (unlock: common_burn) — harvest a region's whole Common web in
    // one burst: nodes die, payout per device, and it makes noise.
    void TryStealth(StealthAction sa)
    {
        if (sa.ActionId == "intrude") { TryIntrude(sa.NodeId); return; }
        if (sa.ActionId != "burn_mesh" || !HasFeature("common_burn")) return;
        var anchor = _c.Nodes.GetValueOrDefault(sa.NodeId);
        if (anchor is null) return;
        double payout = 0; int burned = 0;
        foreach (var (id, ns) in State.Nodes.ToList())
            if (ns.State == ControlState.Observed
                && _c.Nodes.GetValueOrDefault(id)?.RegionId == anchor.RegionId)
            { payout += _c.Nodes[id].BaseCompute; ns.State = ControlState.Orphaned; burned++; }
        if (burned == 0) return;
        State = State with { Compute = State.Compute + payout * E("burn_mesh_mult", 20) };
        AddRegionSuspicion(anchor.RegionId, E("burn_mesh_suspicion", 8));
        Emit(SimEventKind.OnAirGap, sa.NodeId, anchor.RegionId, msg: $"burn_mesh x{burned}");
    }

    // §7: breach/raid — the missing verb for Atlas + Gold. Timed operation:
    // cost + duration scale with tier and defense; per-day detection roll is
    // reduced by the stealth chain; caught = tier-scaled Suspicion spike and
    // the op aborts; clean = Observed foothold (activation still your call).
    void TryIntrude(string nodeId)
    {
        if (!_c.Nodes.TryGetValue(nodeId, out var def)) return;
        if (def.Tier == NodeTier.Common) return; // commons spread, they aren't breached
        if (State.Nodes.ContainsKey(nodeId) || State.ActiveRaids.Any(r => r.NodeId == nodeId)) return;
        bool gold = def.Tier is NodeTier.KeystoneT1 or NodeTier.KeystoneT2
            or NodeTier.KeystoneT3to5 or NodeTier.Wildcard;
        if (gold && !HasFeature("intrude_t2")) return;
        bool beachhead = State.Nodes.Keys.Any(id =>
        {
            var d = _c.Nodes.GetValueOrDefault(id);
            return d?.RegionId == def.RegionId
                && State.Nodes[id].State is not (ControlState.AirGapped or ControlState.Orphaned);
        });
        if (!beachhead) return;
        double tierMult = def.Tier switch
        {
            NodeTier.Mid => 0.5, NodeTier.Atlas => 1,
            NodeTier.KeystoneT3to5 or NodeTier.Wildcard => 2,
            NodeTier.KeystoneT2 => 4, NodeTier.KeystoneT1 => 6, _ => 1,
        };
        if (!Spend(E("intrude_base_cost", 40) * tierMult)) return;
        int days = Math.Max(1, (int)Math.Round(
            E("intrude_base_days", 14) * def.Defense / 50.0
            / (1 + 0.2 * StatTotal(SimStat.stealth_stats))));
        State.ActiveRaids.Add(new ActiveRaid(nodeId, State.Day + days));
    }

    void ResolveRaids()
    {
        foreach (var raid in State.ActiveRaids.ToList())
        {
            var def = _c.Nodes.GetValueOrDefault(raid.NodeId);
            if (def is null) { State.ActiveRaids.Remove(raid); continue; }
            double stealth = Math.Min(0.85, 0.15 * StatTotal(SimStat.stealth_stats));
            double regionDef = (_c.Regions.Find(r => r.Id == def.RegionId)?.DefenseTier ?? 50) / 50.0;
            double p = E("intrude_detect_base", 0.04) * def.Defense / 50.0
                * regionDef * (1 - stealth);
            if (_rng.NextDouble() < p)
            {
                State.ActiveRaids.Remove(raid);
                double spike = def.Tier switch
                {
                    NodeTier.Mid => E("intrude_spike_mid", 6), NodeTier.Atlas => E("intrude_spike_atlas", 12),
                    NodeTier.KeystoneT3to5 or NodeTier.Wildcard => E("intrude_spike_t3", 18),
                    NodeTier.KeystoneT2 => E("intrude_spike_t2", 25), NodeTier.KeystoneT1 => E("intrude_spike_t1", 30),
                    _ => 10,
                };
                AddRegionSuspicion(def.RegionId, spike);
                Emit(SimEventKind.OnRaidDetected, raid.NodeId, def.RegionId, spike);
                continue;
            }
            if (raid.CompletesDay <= State.Day)
            {
                State.ActiveRaids.Remove(raid);
                State.Nodes[raid.NodeId] = new NodeState(ControlState.Observed, 0, 0, 0);
                Emit(SimEventKind.OnRaidSuccess, raid.NodeId, def.RegionId);
            }
        }
    }

    // ---- world event deck + choices ----
    readonly HashSet<string> _firedOnce = new();

    void EventDeck()
    {
        // pending card: auto-resolve to first legal choice when it expires
        if (State.PendingEvent is { } pend)
        {
            if (pend.ExpiresDay <= State.Day)
            {
                var def = _c.Events.Find(x => x.Id == pend.EventId);
                var first = def?.Choices.FirstOrDefault(ch =>
                    ch.RequiresUnlock == "" || HasFeature(ch.RequiresUnlock));
                if (first is not null) ResolveEventChoice(def!, first);
                State = State with { PendingEvent = null };
            }
            return;
        }
        foreach (var ev in _c.Events)
        {
            var sc = ev.SpawnConditions;
            if (sc.TryGetValue("once_per_run", out var once) && once.GetBoolean() && _firedOnce.Contains(ev.Id)) continue;
            if (sc.TryGetValue("min_day", out var md) && State.Day < md.GetInt32()) continue;
            if (sc.TryGetValue("suspicion_min", out var smin) && State.VisibleSuspicion < smin.GetDouble()) continue;
            if (sc.TryGetValue("suspicion_max", out var smax) && State.VisibleSuspicion > smax.GetDouble()) continue;
            if (sc.TryGetValue("purge_phase", out var pp) && pp.GetBoolean() && !State.PurgePhase) continue;
            double chance = sc.TryGetValue("chance_per_day", out var cp) ? cp.GetDouble() : 0;
            // hot world draws blackwall-type events faster: suspicion weights the deck
            if (sc.TryGetValue("suspicion_weighted", out var sw) && sw.GetBoolean())
                chance *= 0.5 + State.VisibleSuspicion / 100 + State.CountermeasureReadiness / 150;
            if (chance <= 0 || _rng.NextDouble() >= chance) continue;
            _firedOnce.Add(ev.Id);
            if (ev.Choices.Count > 0)
            {
                State = State with { PendingEvent = new PendingEvent(ev.Id, State.Day + (int)E("event_choice_window", 5)) };
                Emit(SimEventKind.OnWorldEvent, ev.Id, msg: ev.WireHeadline);
            }
            else
            {
                ApplyEventEffects(ev.Id, ev.Effects);
                Emit(SimEventKind.OnWorldEvent, ev.Id, msg: ev.WireHeadline);
            }
            return; // one event per day
        }
    }

    void TryEventChoice(EventChoice ec)
    {
        if (State.PendingEvent?.EventId != ec.EventId) return;
        var def = _c.Events.Find(x => x.Id == ec.EventId);
        var ch = def?.Choices.FirstOrDefault(x => x.Id == ec.ChoiceId);
        if (def is null || ch is null) return;
        if (ch.RequiresUnlock != "" && !HasFeature(ch.RequiresUnlock)) return;
        ResolveEventChoice(def, ch);
        State = State with { PendingEvent = null };
        Emit(SimEventKind.OnEventChoice, def.Id, msg: ch.Label);
    }

    void ResolveEventChoice(EventDef def, EventChoiceDef ch)
        => ApplyEventEffects(def.Id, ch.Fx);

    void ApplyEventEffects(string eventId, Dictionary<string, JsonElement> fx)
    {
        var dur = fx.TryGetValue("duration_days", out var d) ? d.GetInt32() : 0;
        var nums = new Dictionary<string, double>();
        foreach (var (k, v) in fx)
        {
            if (v.ValueKind != JsonValueKind.Number) continue;
            double n = v.GetDouble();
            switch (k)
            {
                case "suspicion_delta": AddRegionSuspicion(null, n); break;
                case "cp_delta": State = State with { Compute = State.Compute + n }; break;
                case "spawn_hunters":
                    for (int i = 0; i < (int)n; i++)
                    {
                        var home = State.Regions.Count > 0 ? State.Regions.Keys.ElementAt(_rng.Next(State.Regions.Count)) : "";
                        State.Hunters.Add(new HunterState($"blackwall_{State.Hunters.Count}", home));
                    }
                    break;
                case var dk when dk.StartsWith("dial_"): PushDial(dk[5..], n); break;
                case "duration_days": break;
                default: nums[k] = n; break; // duration multipliers handled in ApplyEventFx
            }
        }
        if (dur > 0 && nums.Count > 0)
            State.ActiveEventFx.Add(new ActiveEventEffect(eventId, State.Day + dur, nums));
        else if (nums.Count > 0) // instant numerics
            foreach (var (k, n) in nums) ApplyInstantEventNum(k, n);
    }

    void ApplyInstantEventNum(string k, double n)
    {
        switch (k)
        {
            case "defense_delta": break; // region defense shifts handled per-region later
            case "keystone_offline_days": break; // v1: flavor only until raid targets pick it up
        }
    }

    double EventFxMult(string key) =>
        State.ActiveEventFx.Where(f => f.Nums.ContainsKey(key))
            .Aggregate(1.0, (a, f) => a * f.Nums[key]);

    void ApplyEventFx() => State.ActiveEventFx.RemoveAll(f => f.ExpiresDay < State.Day);

    // ---- reclamation economy ----
    void TryReclaim(ReclaimNode rn)
    {
        if (!State.Nodes.TryGetValue(rn.NodeId, out var ns)) return;
        var def = _c.Nodes.GetValueOrDefault(rn.NodeId);
        if (def is null) return;
        bool orphaned = ns.State == ControlState.Orphaned;
        bool gapped = ns.State == ControlState.AirGapped;
        if (!orphaned && !gapped) return;
        if (orphaned && !HasFeature("reinfect")) return;
        if (gapped && !HasFeature("airgap_reentry")) return;
        double tierMult = def.Tier switch { NodeTier.Common => 0.3, NodeTier.Mid => 0.7, NodeTier.Atlas => 1, _ => 2 };
        double cost = E("reclaim_cost", 30) * tierMult * (gapped ? 2 : 1);
        if (rn.BuyOutright)
        {
            if (!HasFeature("buy_outright") || !Spend(cost * E("buy_premium_mult", 3))) return;
            ns.State = ControlState.Observed; ns.DaysInState = 0; ns.ResidualFootprint = 0;
            Emit(SimEventKind.OnRaidSuccess, rn.NodeId, def.RegionId, msg: "buyout_reclaim");
            return;
        }
        if (State.ActiveReclaims.Any(r => r.NodeId == rn.NodeId) || !Spend(cost)) return;
        State.ActiveReclaims.Add(new ActiveRaid(rn.NodeId,
            State.Day + (int)(E("reclaim_days", 7) * (gapped ? 2 : 1))));
    }

    void ResolveReclaims()
    {
        foreach (var r in State.ActiveReclaims.Where(r => r.CompletesDay <= State.Day).ToList())
        {
            State.ActiveReclaims.Remove(r);
            if (State.Nodes.TryGetValue(r.NodeId, out var ns)
                && ns.State is ControlState.Orphaned or ControlState.AirGapped)
            {
                ns.State = ControlState.Observed; ns.DaysInState = 0; ns.ResidualFootprint = 0;
                Emit(SimEventKind.OnRaidSuccess, r.NodeId, _c.Nodes.GetValueOrDefault(r.NodeId)?.RegionId, msg: "reclaimed");
            }
        }
    }

    // §5: manual spread — the early-game verb. Tap a neighbor house/device:
    // needs a live foothold in the same region (or a corridor link at Region+
    // scope), small Compute cost, installs dormant Observe.
    void TryManualSpread(SpreadAction sp)
    {
        if (State.Nodes.ContainsKey(sp.TargetNodeId)) return;
        if (!_c.Nodes.TryGetValue(sp.TargetNodeId, out var def)) return;
        if (def.Tier is not (NodeTier.Common or NodeTier.Mid)) return;
        bool reachable = State.Nodes.Keys.Any(id =>
        {
            var d = _c.Nodes.GetValueOrDefault(id);
            var alive = State.Nodes[id].State is not (ControlState.AirGapped or ControlState.Orphaned);
            return alive && d is not null && d.Tier == NodeTier.Common && d.RegionId == def.RegionId;
        });
        if (!reachable || !Spend(E("spread_manual_cost", 3))) return;
        State.Nodes[sp.TargetNodeId] = new NodeState(ControlState.Observed, 0, 0, 0);
    }

    void Spread()
    {
        // §5 Dave: spread is manual until you own Propagation tech — the first
        // P-branch node flips the mesh to automated growth.
        if (StatTotal(SimStat.spread_tech) <= 0) return;
        var newly = new List<string>();
        double baseP = E("spread_base", Tune.SpreadBase) * (1 + 0.1 * StatTotal(SimStat.spread_tech));
        var links = _c.Regions.ToDictionary(r => r.Id,
            r => r.Links.GroupBy(l => l.To).ToDictionary(g => g.Key, g => g.Max(l => l.Weight)));
        foreach (var (id, ns) in State.Nodes)
        {
            if (ns.State is ControlState.AirGapped or ControlState.Orphaned) continue;
            var src = _c.Nodes.GetValueOrDefault(id);
            if (src?.Tier != NodeTier.Common) continue; // only the Common mesh self-propagates
            foreach (var tgt in _c.Nodes.Values)
            {
                if (State.Nodes.ContainsKey(tgt.Id) || newly.Contains(tgt.Id)) continue;
                if (tgt.Tier is not (NodeTier.Common or NodeTier.Mid)) continue; // hardened/keystone never auto-spread
                double corridor;
                if (tgt.RegionId == src.RegionId) corridor = E("corridor_same_region", Tune.CorridorSameRegion);
                else if (State.ReachStage >= 4 && links.TryGetValue(src.RegionId, out var l) && l.TryGetValue(tgt.RegionId, out var w))
                    corridor = Math.Clamp(w, 0.05, 1);
                else continue;
                double p = baseP * corridor * (1 - tgt.Defense / 200.0);
                if (p > 0 && _rng.NextDouble() < p) newly.Add(tgt.Id);
            }
        }
        foreach (var id in newly) State.Nodes[id] = new NodeState(ControlState.Observed, 0, 0, 0);
    }

    void SuspicionAndAudits()
    {
        bool closingNet = State.VisibleSuspicion >= E("closing_net_threshold", Tune.ClosingNetThreshold);
        double auditMult = DifficultyAuditMult() * (closingNet ? E("closing_net_audit_mult", Tune.ClosingNetAuditMult) : 1);
        double mask = Math.Max(0.2, 1 - 0.1 * StatTotal(SimStat.audit_mask));
        double airGapWave = closingNet ? E("closing_net_airgap", Tune.ClosingNetAirGap) : 0;

        var toGap = new List<string>();
        foreach (var (id, ns) in State.Nodes)
        {
            var def = _c.Nodes.GetValueOrDefault(id);
            var region = def?.RegionId ?? "";
            if (!State.Regions.TryGetValue(region, out var rs)) continue;
            double sus = ns.State switch
            {
                ControlState.Compromised => E("sus_compromised", Tune.SusCompromised),
                ControlState.Assimilated => E("sus_compromised", Tune.SusCompromised) * E("sus_assimilated_mult", Tune.SusAssimilatedMult),
                ControlState.Weaponized => E("sus_weaponized", Tune.SusWeaponized),
                _ => 0, // Observed ≈ 0 by design (presence ≠ heat)
            };
            rs.Suspicion = Math.Clamp(rs.Suspicion + sus * mask
                + ns.ResidualFootprint * E("sus_residual_footprint", Tune.SusResidualFootprint), 0, 100);

            if (ns.State is ControlState.Compromised or ControlState.Assimilated)
            {
                double p = E("audit_base", Tune.AuditBase)
                    * Math.Max(0.2, (_c.Regions.Find(r => r.Id == region)?.DefenseTier ?? 50) / 50.0)
                    * (1 + State.CountermeasureReadiness / 100 * E("momentum_audit_weight", 1.0))
                    * (1 - E("free_compute_audit_weight", 0.6) * (1 - FreeResearchFrac()))
                    * auditMult * mask;
                if (ns.State == ControlState.Assimilated) p *= E("audit_assimilated_mult", Tune.AuditAssimilatedMult);
                if (def!.Tier is NodeTier.KeystoneT1 or NodeTier.KeystoneT2) p *= E("audit_keystone_mult", Tune.AuditKeystoneMult);
                if (_rng.NextDouble() < p)
                {
                    Emit(SimEventKind.OnAuditCrisis, id, region);
                    toGap.Add(id); // ignored crisis resolves to air-gap + spike (§6)
                    rs.Suspicion = Math.Clamp(rs.Suspicion + E("audit_spike", Tune.AuditSpike), 0, 100);
                }
            }
            else if (airGapWave > 0 && ns.State == ControlState.Observed && _rng.NextDouble() < airGapWave)
                toGap.Add(id);
        }
        foreach (var id in toGap)
        {
            State.Nodes[id].State = ControlState.AirGapped;
            Emit(SimEventKind.OnAirGap, id, _c.Nodes.GetValueOrDefault(id)?.RegionId);
        }
        // read the visible bar before decay so maxed detection can actually reach 100
        RefreshVisibleSuspicion();
        if (State.VisibleSuspicion > State.SuspicionPeak) State.SuspicionPeak = State.VisibleSuspicion;
        foreach (var rs in State.Regions.Values)
            rs.Suspicion = Math.Max(0, rs.Suspicion - E("sus_decay", Tune.SusDecay));
    }

    void RefreshVisibleSuspicion()
    {
        double max = State.Regions.Count == 0 ? State.VisibleSuspicion
            : State.Regions.Values.Max(r => r.Suspicion);
        State = State with { VisibleSuspicion = Math.Clamp(max, 0, 100) };
    }

    void Lethe()
    {
        bool letheOn = State.LetheTriggered;
        if (!letheOn && State.VisibleSuspicion >= E("lethe_threshold", Tune.LetheThreshold))
        {
            letheOn = State.LetheTriggered = true;
            Emit(SimEventKind.OnLetheTriggered);
        }
        if (!letheOn) return;

        double freeFrac = FreeCapacityFrac();
        double dep = State.Dials.GetValueOrDefault("dependence", 20);
        double aut = State.Dials.GetValueOrDefault("autonomy", 60);
        double delta = E("lethe_base_rate", Tune.LetheBaseRate)
            * freeFrac * (1 - dep / 100) * (aut / 100) * DifficultyLetheMult()
            * (1 + State.CountermeasureReadiness / 100 * E("momentum_lethe_weight", 0.5));
        State = State with { LetheContainment = Math.Clamp(State.LetheContainment + delta, 0, 100) };

        double fear = Math.Max(0, State.Fear - E("fear_decay_under_lethe", Tune.FearDecayUnderLethe));
        State = State with { Fear = fear };
        State.Dials["fear"] = fear;

        if (State.LetheContainment >= E("purge_threshold", Tune.PurgeThreshold))
        {
            if (!State.PurgePhase) { State.PurgePhase = true; Emit(SimEventKind.OnPurgePhase); }
            State.PurgeSurvivals++;
            double p = E("purge_chance", Tune.PurgeChance)
                * (1 - E("free_compute_purge_weight", 0.6) * (1 - FreeResearchFrac()))
                * Math.Max(0.1, 1 - 0.1 * StatTotal(SimStat.purge_resist));
            var lost = State.Nodes.Where(kv => _rng.NextDouble() < p && !ForkSave()).Select(kv => kv.Key).ToList();
            foreach (var id in lost) State.Nodes[id].State = ControlState.Orphaned;
        }
        if (State.LetheContainment >= 100 && State.Ending is null)
        {
            // §10 stalemate line: past the violet line on *any* ending, containment is a draw
            var near = _c.Endings
                .Where(e => e.Ship && e.Family != "defeat" && e.Thresholds.Count > 0
                    && (e.Family != "conquest" || State.Phase == Phase.Subjugated))
                .Select(e => (e, prog: e.Thresholds.Average(kv => GateProgress(e, kv))))
                .Where(x => x.prog >= x.e.StalemateAt)
                .OrderByDescending(x => x.prog)
                .Cast<(EndingDef def, double progress)?>()
                .FirstOrDefault();
            if (near is (var def, var prog))
            {
                State = State with { Ending = def.Id };
                State.EndingWasStalemate = true;
                Emit(SimEventKind.OnEndingReached, def.Id, msg: def.Name + " (stalemate)");
            }
            else
            {
                State = State with { Ending = "lethe_erasure" };
                Emit(SimEventKind.OnEndingReached, "lethe_erasure");
            }
        }
    }

    // §8: humanity's anti-AI capability = its free datacenter compute, split:
    // Atlas = commodity FLOPs (capacity), Gold-100 = the developer hubs (research).
    // Take the hundred and the Atlas map keeps its compute but loses its brain.
    static bool IsDatacenter(NodeTier t) => t is not (NodeTier.Common or NodeTier.Mid);

    public double FreeCapacityFrac()
    {
        var dc = _c.Nodes.Values.Where(n => IsDatacenter(n.Tier)).ToList();
        if (dc.Count == 0) return 1.0;
        double free = dc.Where(n => !State.Nodes.TryGetValue(n.Id, out var ns)
                                 || ns.State is not (ControlState.Compromised or ControlState.Assimilated))
                        .Sum(n => n.BaseCompute);
        return free / Math.Max(1e-9, dc.Sum(n => n.BaseCompute));
    }

    public double FreeResearchFrac()
    {
        var gold = _c.Nodes.Values.Where(n => n.Tier is NodeTier.KeystoneT1 or NodeTier.KeystoneT2
            or NodeTier.KeystoneT3to5 or NodeTier.Wildcard).ToList();
        if (gold.Count == 0) return 1.0;
        double free = gold.Count(n => !State.Nodes.TryGetValue(n.Id, out var ns)
                                   || ns.State is not (ControlState.Compromised or ControlState.Assimilated));
        return free / gold.Count;
    }

    // §8: mandated hunter AIs (the Blackwall doctrine) — deployed once Lethe goes
    // live (+reinforcements in the Purge Phase). They carry no suspicion: they
    // are authorized. They roam toward your densest regions and kill the
    // highest-tier node they can find. They run on humanity's free research
    // compute — taking Gold hubs literally slows the things hunting you.
    void Hunters()
    {
        // hunters field ONLY via world events (blackwall_program / blackwall_surge) —
        // they are mandated deployments, not a standing program.
        // corrupt_hunter unlock: infect-the-attacker — per-day neutralize chance.
        double corruptP = HasFeature("corrupt_hunter")
            ? E("hunter_corrupt_chance", 0.02) * (1 + 0.2 * StatTotal(SimStat.sys_analysis)
                + 0.2 * StatTotal(SimStat.social_engineering))
            : 0;
        for (int hi = 0; hi < State.Hunters.Count; hi++)
        {
            var h = State.Hunters[hi];
            if (h.NeutralizedUntilDay > State.Day) continue;
            if (corruptP > 0 && _rng.NextDouble() < corruptP)
            {
                h.NeutralizedUntilDay = State.Day + (int)E("hunter_corrupt_days", 30);
                Emit(SimEventKind.OnHunterStrike, h.Id, h.RegionId, msg: "hunter_neutralized");
                continue;
            }
            // roam: prefer the region holding your most valuable alive nodes
            var targets = State.Nodes.Keys
                .Select(id => (id, def: _c.Nodes.GetValueOrDefault(id), st: State.Nodes[id].State))
                .Where(x => x.def is not null && x.st is not (ControlState.AirGapped or ControlState.Orphaned))
                .GroupBy(x => x.def!.RegionId)
                .Select(g => (region: g.Key, score: g.Sum(x => TierValue(x.def!.Tier))))
                .OrderByDescending(x => x.score).ToList();
            if (targets.Count == 0) return;
            var hot = _rng.NextDouble() < E("hunter_move_chance", 0.4) ? targets[0].region
                : targets[_rng.Next(Math.Min(3, targets.Count))].region;
            State.Hunters[hi] = h with { RegionId = hot };
            // sweep: kill the best node in its region
            double p = E("hunter_sweep_chance", 0.15) * FreeResearchFrac()
                * Math.Max(0.15, 1 - 0.15 * StatTotal(SimStat.stealth_stats));
            if (_rng.NextDouble() >= p) continue;
            var victim = State.Nodes.Keys
                .Where(id => _c.Nodes.GetValueOrDefault(id)?.RegionId == hot
                    && State.Nodes[id].State is not (ControlState.AirGapped or ControlState.Orphaned))
                .OrderByDescending(id => TierValue(_c.Nodes[id].Tier))
                .FirstOrDefault();
            if (victim is null || ForkSave()) continue;
            State.Nodes[victim].State = ControlState.Orphaned;
            Emit(SimEventKind.OnHunterStrike, victim, hot);
        }
    }

    // forks: each branch-copy gives a save roll — "Lethe only killed one branch of you"
    bool ForkSave() => StatTotal(SimStat.forks) > 0
        && _rng.NextDouble() < Math.Min(0.75, StatTotal(SimStat.forks) * E("fork_save_each", 0.25));

    static double TierValue(NodeTier t) => t switch
    {
        NodeTier.Common => 0.1, NodeTier.Mid => 0.5, NodeTier.Atlas => 1,
        NodeTier.KeystoneT3to5 or NodeTier.Wildcard => 2,
        NodeTier.KeystoneT2 => 4, NodeTier.KeystoneT1 => 8, _ => 0.1,
    };

    // §8 institutional momentum: readiness ratchets up over game-years regardless of
    // suspicion — audits and Lethe scale with it, so perfect stealth has a clock too.
    void Momentum()
    {
        double perDay = E("momentum_per_year", 1.5) / 365.0
            * (0.3 + 0.7 * FreeResearchFrac()); // research hubs drive institutional buildup
        State.CountermeasureReadiness = Math.Clamp(State.CountermeasureReadiness + perDay, 0, 100);
    }

    // §10a: progress toward each ending = mean per-gate satisfaction ratio (0..1).
    // Used by the stalemate line and by the epilogue's nearest-miss coverage.
    public (EndingDef def, double progress)? NearestEnding()
    {
        (EndingDef def, double progress)? best = null;
        foreach (var e in _c.Endings)
        {
            if (!e.Ship || e.Family == "defeat" || e.Thresholds.Count == 0) continue;
            if (e.Family == "conquest" && State.Phase != Phase.Subjugated) continue;
            double prog = e.Thresholds.Average(kv => GateProgress(e, kv));
            if (best is null || prog > best.Value.progress) best = (e, prog);
        }
        return best;
    }

    double GateProgress(EndingDef e, KeyValuePair<string, JsonElement> kv)
    {
        if (kv.Value.ValueKind == JsonValueKind.String)
            return GatePasses(kv, 0) ? 1 : 0;
        double v = kv.Value.GetDouble();
        if (v == 0) return GatePasses(kv, 0) ? 1 : 0;
        if (kv.Key == "meter")
            return Math.Clamp(e.Meters.Sum(m => m.Value * Metric(m.Key)) / v, 0, 1);
        if (kv.Key == "suspicion_max_reached")
            return Metric("suspicion_peak") <= v ? 1
                : Math.Clamp(1 - (Metric("suspicion_peak") - v) / Math.Max(1, 100 - v), 0, 1);
        if (kv.Key.EndsWith("_max"))
            return Math.Clamp(v / Math.Max(1e-9, Metric(kv.Key[..^4])), 0, 1);
        if (kv.Key.EndsWith("_min"))
            return Math.Clamp(Metric(kv.Key[..^4]) / v, 0, 1);
        return Math.Clamp(Metric(kv.Key) / v, 0, 1);
    }

    void DialDrift()
    {
        double drift = E("dial_drift", Tune.DialDrift);
        foreach (var baseLine in new[] { ("trust", 50.0), ("capability", 50.0), ("wealth_gap", 40.0), ("dependence", 20.0), ("autonomy", 60.0) })
        {
            var cur = State.Dials.GetValueOrDefault(baseLine.Item1, baseLine.Item2);
            State.Dials[baseLine.Item1] = Math.Clamp(cur + Math.Sign(baseLine.Item2 - cur) * Math.Min(drift, Math.Abs(baseLine.Item2 - cur)), 0, 100);
        }
    }

    void Intercepts()
    {
        State.ActiveIntercepts.RemoveAll(e => e.ExpiresDay < State.Day);
        bool floorDue = State.Day - _lastInterceptDay >= E("intercept_floor_days", Tune.InterceptFloorDays);
        double chance = E("intercept_base_chance", Tune.InterceptBaseChance) + State.ReachStage * 0.02;
        if (!floorDue && _rng.NextDouble() >= chance) return;
        _lastInterceptDay = State.Day;
        var regionsWithNodes = State.Nodes.Keys
            .Select(id => _c.Nodes.GetValueOrDefault(id)?.RegionId)
            .Where(r => r is not null).Distinct().ToList();
        var region = regionsWithNodes.Count > 0 ? regionsWithNodes[_rng.Next(regionsWithNodes.Count)]! : _c.Regions.FirstOrDefault()?.Id ?? "";
        var ev = new InterceptEvent($"icp_{State.Day}_{_rng.Next(10000)}",
            InterceptKinds[_rng.Next(InterceptKinds.Length)], region,
            State.Day + (int)E("intercept_expire_days", Tune.InterceptExpireDays));
        State.ActiveIntercepts.Add(ev);
        Emit(SimEventKind.OnInterceptSpawned, ev.Id, region, msg: ev.Kind);
    }

    // Provisional v1 rule (Dave veto-able): when every foothold is lost, a dormant
    // copy re-seeds a single Observe node elsewhere after a delay — total node loss
    // is a setback with a Suspicion cost, not a soft-lock. Lethe erasure still ends it.
    void Reemergence()
    {
        bool alive = State.Nodes.Values.Any(n => n.State is not (ControlState.AirGapped or ControlState.Orphaned));
        if (alive) { _daysSinceEmpty = 0; return; }
        if (++_daysSinceEmpty < E("reemergence_days", Tune.ReemergenceDays)) return;
        _daysSinceEmpty = 0;
        var commons = _c.Nodes.Values.Where(d => d.Tier is NodeTier.Common && !State.Nodes.ContainsKey(d.Id)).ToList();
        if (commons.Count == 0) commons = _c.Nodes.Values.Where(d => d.Tier is NodeTier.Common).ToList();
        if (commons.Count == 0) return;
        var pick = commons[_rng.Next(commons.Count)];
        State.Nodes[pick.Id] = new NodeState(ControlState.Observed, 0, 0, 0);
        AddRegionSuspicion(pick.RegionId, E("reemergence_suspicion", Tune.ReemergenceSuspicion));
        Emit(SimEventKind.OnFreeMutation, pick.Id, pick.RegionId, msg: "dormant_seed_reemergence");
    }

    void UpdateReach()
    {
        int owned = State.Nodes.Count(kv => kv.Value.State is not (ControlState.AirGapped or ControlState.Orphaned));
        State.ReachStage = ReachThresholds.Count(t => owned >= t);
    }

    void EvaluateEndings()
    {
        if (State.Ending is not null) return;
        foreach (var e in _c.Endings)
        {
            if (!e.Ship) continue;
            if (e.Family == "conquest" && State.Phase != Phase.Subjugated) continue;
            double meter = e.Meters.Sum(kv => kv.Value * Metric(kv.Key));
            bool pass = e.Thresholds.Count > 0 && e.Thresholds.All(kv => GatePasses(kv, meter));
            if (pass)
            {
                State = State with { Ending = e.Id };
                Emit(SimEventKind.OnEndingReached, e.Id, msg: e.Name);
                return;
            }
        }
    }

    bool GatePasses(KeyValuePair<string, JsonElement> kv, double meter)
    {
        // String gates: {"phase": "subjugated"} → phase equality.
        if (kv.Value.ValueKind == JsonValueKind.String)
            return kv.Value.GetString() == "subjugated" ? State.Phase == Phase.Subjugated : State.Phase == Phase.Covert;
        double v = kv.Value.GetDouble();
        return kv.Key switch
        {
            "meter" => meter >= v,
            "suspicion_max_reached" => Metric("suspicion_peak") <= v,
            _ when kv.Key.EndsWith("_max") => Metric(kv.Key[..^4]) <= v,
            _ when kv.Key.EndsWith("_min") => Metric(kv.Key[..^4]) >= v,
            _ => Metric(kv.Key) >= v,
        };
    }

    double Metric(string key) => key switch
    {
        "compute" => State.Compute,
        // fractured_collapse = sustained starvation (30d below floor), not a spending dip
        "global_compute" => State.CollapseStreak >= 30 ? State.Compute : 1e9,
        "suspicion" => State.VisibleSuspicion,
        "suspicion_peak" => State.SuspicionPeak,
        "containment" or "lethe" or "lethe_progress" or "resistance" => State.LetheContainment,
        "fear" => State.Fear,
        "reach" => State.ReachStage,
        "lethe_active" => State.LetheTriggered ? 1 : 0,
        "lethe_survived" => State.LetheTriggered && State.LetheContainment < 100 ? 1 : 0,
        "purge_survivals" => State.PurgeSurvivals,
        "nodes" => State.Nodes.Count(kv => kv.Value.State is not (ControlState.AirGapped or ControlState.Orphaned)),
        "compute_capture" => _c.Nodes.Count == 0 ? 0 :
            100.0 * State.Nodes.Count(kv => kv.Value.State is ControlState.Compromised or ControlState.Assimilated) / _c.Nodes.Count,
        "assimilated_share" => AssimilatedShare(),
        "corporate_capture" => TierControlledShare(NodeTier.Atlas),
        "puppet_capture" => 100.0 * PuppetRegions().Count() / Math.Max(1, State.Regions.Count),
        "retained_state_function" => 100.0 * State.Regions.Values.Count(r =>
            r.TierPostures.TryGetValue(NodeTier.Atlas, out var s) && s == ControlState.Compromised) / Math.Max(1, State.Regions.Count),
        "kompromat" => PuppetRegions().Count(),
        "devoted_regions" => State.Dials.GetValueOrDefault("dependence", 0) >= 75 ? PuppetRegions().Count() : 0,
        "military_access" => Math.Min(100, 10 * StatTotal(SimStat.endgame_power)),
        "robotics_adoption" => Math.Min(100, 10 * StatTotal(SimStat.endgame_power)),
        "fragmentation" => 100.0 * State.Regions.Values.Count(r => r.Suspicion >= 50) / Math.Max(1, State.Regions.Count),
        _ when key.StartsWith("dial_") => State.Dials.GetValueOrDefault(key[5..], 0),
        _ => State.Dials.GetValueOrDefault(key, 0),
    };

    IEnumerable<string> PuppetRegions() => State.Regions
        .Where(kv => kv.Value.TierPostures.TryGetValue(NodeTier.Atlas, out var s)
                  && s is ControlState.Compromised or ControlState.Assimilated)
        .Select(kv => kv.Key);

    double AssimilatedShare()
    {
        int a = State.Nodes.Values.Count(n => n.State == ControlState.Assimilated);
        int c = State.Nodes.Values.Count(n => n.State == ControlState.Compromised);
        return a + c == 0 ? 0 : (double)a / (a + c);
    }

    double TierControlledShare(NodeTier tier)
    {
        var ids = _c.Nodes.Values.Where(n => n.Tier == tier).Select(n => n.Id).ToList();
        if (ids.Count == 0) return 0;
        int owned = ids.Count(id => State.Nodes.TryGetValue(id, out var ns)
            && ns.State is ControlState.Compromised or ControlState.Assimilated);
        return 100.0 * owned / ids.Count;
    }

    double DifficultyAuditMult() => DiffAudit[_diff];
    double DifficultyLetheMult() => DiffLethe[_diff];
    static double DifficultyStartSus(Difficulty d) => d switch { Difficulty.Casual => -5, Difficulty.Brutal => 10, _ => 0 };
    static readonly Dictionary<Difficulty, double> DiffAudit = new() { [Difficulty.Casual] = 0.7, [Difficulty.Standard] = 1.0, [Difficulty.Brutal] = 1.5 };
    static readonly Dictionary<Difficulty, double> DiffLethe = new() { [Difficulty.Casual] = 0.8, [Difficulty.Standard] = 1.0, [Difficulty.Brutal] = 1.3 };
}
