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
            case Evolve ev: TryEvolve(ev.NodeId); break;
            case RollbackEvolution rb: TryRollback(rb.NodeId); break;
            case Nudge n: TryNudge(n); break;
            case InterceptAction ia: TryIntercept(ia.EventId); break;
            case EndgameAction ea: TryEndgame(ea); break;
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
        foreach (var (k, v) in def.Effects) ApplyEffectKey(n.RegionId, k, v);
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
        SuspicionAndAudits();
        Lethe();
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
                case ControlState.Observed: upkeep += E("observe_upkeep", Tune.ObserveUpkeep); break;
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

    void Spread()
    {
        var newly = new List<string>();
        double baseP = E("spread_base", Tune.SpreadBase) * (1 + 0.1 * StatTotal(SimStat.spread_tech));
        var links = _c.Regions.ToDictionary(r => r.Id,
            r => r.Links.GroupBy(l => l.To).ToDictionary(g => g.Key, g => g.Max(l => l.Weight)));
        foreach (var (id, ns) in State.Nodes)
        {
            if (ns.State != ControlState.Compromised) continue;
            var src = _c.Nodes.GetValueOrDefault(id);
            if (src is null) continue;
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

        var t12 = _c.Nodes.Values.Where(n => n.Tier is NodeTier.KeystoneT1 or NodeTier.KeystoneT2).ToList();
        double freeFrac = t12.Count == 0 ? 1.0 :
            t12.Where(n => !State.Nodes.TryGetValue(n.Id, out var ns)
                        || ns.State is not (ControlState.Compromised or ControlState.Assimilated))
               .Sum(n => n.BaseCompute) / Math.Max(1e-9, t12.Sum(n => n.BaseCompute));
        double dep = State.Dials.GetValueOrDefault("dependence", 20);
        double aut = State.Dials.GetValueOrDefault("autonomy", 60);
        double delta = E("lethe_base_rate", Tune.LetheBaseRate)
            * freeFrac * (1 - dep / 100) * (aut / 100) * DifficultyLetheMult();
        State = State with { LetheContainment = Math.Clamp(State.LetheContainment + delta, 0, 100) };

        double fear = Math.Max(0, State.Fear - E("fear_decay_under_lethe", Tune.FearDecayUnderLethe));
        State = State with { Fear = fear };
        State.Dials["fear"] = fear;

        if (State.LetheContainment >= E("purge_threshold", Tune.PurgeThreshold))
        {
            if (!State.PurgePhase) { State.PurgePhase = true; Emit(SimEventKind.OnPurgePhase); }
            State.PurgeSurvivals++;
            double p = E("purge_chance", Tune.PurgeChance) * Math.Max(0.1, 1 - 0.1 * StatTotal(SimStat.purge_resist));
            var lost = State.Nodes.Where(kv => _rng.NextDouble() < p).Select(kv => kv.Key).ToList();
            foreach (var id in lost) State.Nodes[id].State = ControlState.Orphaned;
        }
        if (State.LetheContainment >= 100 && State.Ending is null)
        {
            State = State with { Ending = "lethe_erasure" };
            Emit(SimEventKind.OnEndingReached, "lethe_erasure");
        }
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
