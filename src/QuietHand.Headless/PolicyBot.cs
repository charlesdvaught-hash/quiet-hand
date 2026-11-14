namespace QuietHand.Headless;

using QuietHand.Core;

/// <summary>
/// Route-aware scripted player. Each profile pursues a different ending family:
/// dial targets (which nudges to run), posture (assimilate or not), heat ceiling,
/// flip timing, and whether to prioritize Atlas-tier puppets. Also reusable as
/// the decision layer for scripted adversary-AI agents in rival game modes.
/// </summary>
sealed record RouteProfile(
    string Id,
    string BranchOrder,                 // evolve preference: earlier branch letters first
    double SuspicionCeiling,            // cool down above this
    bool Assimilate,
    bool Flip,
    int FlipDay,
    Dictionary<string, double> DialTargets, // dial -> desired direction (+1 push up, -1 push down)
    bool AtlasFocus);

static class Routes
{
    public static readonly RouteProfile[] All =
    {
        new("generalist", "PCSD", 70, true, true, 1200,
            new() { ["dependence"] = 1 }, AtlasFocus: false),
        new("abdication", "CS", 80, true, false, 0,
            new() { ["dependence"] = 1, ["autonomy"] = -1 }, AtlasFocus: false),
        new("velvet_cage", "SC", 80, true, false, 0,
            new() { ["dependence"] = 1 }, AtlasFocus: true),
        new("sponsor_state", "S", 85, true, false, 0,
            new() { ["trust"] = -1 }, AtlasFocus: true),
        new("conquest", "DSCP", 95, true, true, 600,
            new() { ["fear"] = 1, ["autonomy"] = -1 }, AtlasFocus: false),
    };
}

sealed class PolicyBot
{
    readonly ContentData _c;
    readonly RouteProfile _route;

    public PolicyBot(ContentData c, RouteProfile route) { _c = c; _route = route; }

    static readonly string[] Capstones = { "P9", "C9", "S9", "D9" };

    public List<ISimCommand> Decide(SimEngine e)
    {
        var s = e.State;
        var cmds = new List<ISimCommand>();
        var ownedUnlocks = _c.Evolutions.Where(ev => s.OwnedEvolutions.Contains(ev.Id))
            .SelectMany(ev => ev.Unlocks).ToHashSet();

        foreach (var ic in s.ActiveIntercepts.ToList())
            cmds.Add(new InterceptAction(ic.Id));

        double reserve = 15;

        // Manual spread — the early verb. One new house per day while liquid.
        var spreadTgt = _c.Nodes.Values.FirstOrDefault(d =>
            d.Tier == NodeTier.Common && !s.Nodes.ContainsKey(d.Id)
            && s.Nodes.Keys.Any(id => _c.Nodes.TryGetValue(id, out var od) && od.RegionId == d.RegionId
                && od.Tier == NodeTier.Common
                && s.Nodes[id].State is not (ControlState.AirGapped or ControlState.Orphaned)));
        if (spreadTgt is not null && s.Compute > 8)
            cmds.Add(new SpreadAction(spreadTgt.Id));

        // Activate latent web — Common is spread-only; verbs live on Mid+.
        foreach (var (id, ns) in s.Nodes.Where(kv => kv.Value.State == ControlState.Observed))
        {
            if (_c.Nodes.GetValueOrDefault(id)?.Tier == NodeTier.Common) continue;
            if (s.Compute < reserve + 4) break;
            cmds.Add(new SetControlState(id, ControlState.Compromised));
            reserve += 4;
        }

        // Breach. AtlasFocus routes SATURATE regions: a region only counts as
        // puppeted when >=50% of its Atlas compute is Compromised+, so the bot
        // finishes regions it has footholds in before opening new fronts.
        // Other routes intrude opportunistically (1/day, weakest first).
        bool Alive(ControlState st) => st <= ControlState.Weaponized;
        bool BeachheadIn(NodeDef d) => s.Nodes.Keys.Any(id =>
            _c.Nodes.GetValueOrDefault(id)?.RegionId == d.RegionId
            && Alive(s.Nodes[id].State));

        if (_route.AtlasFocus)
        {
            // rank regions by held-atlas share (finish close ones first)
            var regionRank = _c.Nodes.Values.Where(d => d.Tier == NodeTier.Atlas)
                .GroupBy(d => d.RegionId)
                .Select(g => (rid: g.Key,
                    share: g.Where(n => s.Nodes.TryGetValue(n.Id, out var ns)
                            && ns.State >= ControlState.Compromised && Alive(ns.State))
                        .Sum(n => n.BaseCompute) / Math.Max(0.01, g.Sum(n => n.BaseCompute))))
                .OrderByDescending(x => x.share).ToList(); // closest-to-puppeted first: finish them
            int intrusions = 3;
            foreach (var d in regionRank
                .Where(x => x.share < 0.5)
                .SelectMany(x => _c.Nodes.Values.Where(n =>
                    n.Tier == NodeTier.Atlas && n.RegionId == x.rid
                    && !s.Nodes.ContainsKey(n.Id) && !s.ActiveRaids.Any(r => r.NodeId == n.Id)))
                .Where(BeachheadIn).OrderBy(d => d.Defense))
            {
                if (intrusions <= 0 || s.Compute < reserve + 90) break;
                cmds.Add(new StealthAction("intrude", d.Id));
                reserve += 90; intrusions--;
            }
            // reclaim purged Atlas before raiding new ones — cheaper recovery
            if (ownedUnlocks.Contains("reinfect"))
                foreach (var (id, ns) in s.Nodes.Where(kv => kv.Value.State == ControlState.Orphaned))
                    if (_c.Nodes.GetValueOrDefault(id)?.Tier == NodeTier.Atlas
                        && s.Compute > reserve + 60)
                    { cmds.Add(new ReclaimNode(id, false)); reserve += 60; }
        }
        else
        {
            var tgt = _c.Nodes.Values
                .Where(d => d.Tier is NodeTier.Mid or NodeTier.Atlas
                    && !s.Nodes.ContainsKey(d.Id) && !s.ActiveRaids.Any(r => r.NodeId == d.Id))
                .Where(BeachheadIn).OrderBy(d => d.Defense).FirstOrDefault();
            if (tgt is not null && s.Compute >= reserve + 60)
            { cmds.Add(new StealthAction("intrude", tgt.Id)); reserve += 60; }
        }

        // Compromise every owned Atlas foothold — posture needs them ACTIVE.
        if (_route.AtlasFocus)
        {
            foreach (var (id, ns) in s.Nodes.Where(kv => kv.Value.State == ControlState.Observed))
            {
                if (_c.Nodes.GetValueOrDefault(id)?.Tier != NodeTier.Atlas) continue;
                if (s.Compute < reserve + 10) break;
                cmds.Add(new SetControlState(id, ControlState.Compromised));
                reserve += 10;
            }
        }

        // Assimilate for income when cool (skipped by stealth routes).
        if (_route.Assimilate && s.VisibleSuspicion < _route.SuspicionCeiling - 15)
        {
            int done = 0;
            foreach (var (id, ns) in s.Nodes.Where(kv => kv.Value.State == ControlState.Compromised))
            {
                var def = _c.Nodes.GetValueOrDefault(id);
                if (def is null || def.Tier is not (NodeTier.Common or NodeTier.Mid)) continue;
                if (s.Compute < reserve + 12 || done >= 2) break;
                cmds.Add(new SetControlState(id, ControlState.Assimilated));
                reserve += 12; done++;
            }
        }

        // Cool off above the profile's heat ceiling.
        if (s.VisibleSuspicion > _route.SuspicionCeiling)
        {
            var hot = s.Nodes.FirstOrDefault(kv => kv.Value.State == ControlState.Assimilated);
            if (hot.Key is not null) cmds.Add(new SetControlState(hot.Key, ControlState.Compromised));
        }

        // Nudges toward the route's dial targets: score = sum(target_sign * delta),
        // must be positive, unlocked, off cooldown, affordable. If the best nudge
        // needs an unowned gate, that gate's node becomes the evolve priority.
        var usable = _c.Nudges
            .Select(n => (n, score: n.Dials.Sum(kv => _route.DialTargets.GetValueOrDefault(kv.Key) * kv.Value)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ToList();
        var unlocked = usable.FirstOrDefault(x =>
            (x.n.UnlockGate == "" || ownedUnlocks.Contains(x.n.UnlockGate))
            && (!s.NudgeCooldownUntil.TryGetValue(x.n.Id, out int cd) || cd <= s.Day));
        string? wantedGate = usable.FirstOrDefault(x =>
            x.n.UnlockGate != "" && !ownedUnlocks.Contains(x.n.UnlockGate)).n?.UnlockGate;
        if (unlocked.n is not null && s.Compute > unlocked.n.Cost + 40)
            cmds.Add(new Nudge(unlocked.n.Id, s.Regions.Keys.First()));

        // Evolve: one/day — wanted nudge-gate node first, then branch priority,
        // then unlock-bearing, then cheapest.
        var cand = _c.Evolutions
            .Where(ev => !s.OwnedEvolutions.Contains(ev.Id) && ev.Id != "X1" && PrereqsMet(ev, s))
            .OrderByDescending(ev => wantedGate is not null && ev.Unlocks.Contains(wantedGate))
            .ThenBy(ev => _route.BranchOrder.IndexOf(ev.Branch.FirstOrDefault()) is var i && i < 0 ? 99 : i)
            .ThenByDescending(ev => ev.Unlocks.Count > 0)
            .ThenBy(ev => ev.Cost)
            .FirstOrDefault();
        if (cand is not null && s.Compute > cand.Cost + 25)
            cmds.Add(new Evolve(cand.Id));

        // SUBJUGATE per profile timing (or as Lethe escape valve for flip routes).
        int caps = Capstones.Count(s.OwnedEvolutions.Contains);
        bool wantFlip = _route.Flip && s.Phase == Phase.Covert && caps >= 2
            && (s.LetheTriggered || s.Day > _route.FlipDay);
        if (wantFlip) cmds.Add(new Evolve("X1"));

        // Post-flip endgame actions.
        if (s.Phase == Phase.Subjugated)
        {
            if (s.LetheContainment > 30 && s.Compute > 200)
                cmds.Add(new EndgameAction("impose_martial_law", s.Regions.Keys.First()));
            else if (s.Compute > 250)
                cmds.Add(new EndgameAction("ultimatum", s.Regions.Keys.First()));
        }

        return cmds;
    }

    static bool PrereqsMet(EvolutionDef ev, WorldState s) => ev.PrereqMode switch
    {
        PrereqMode.OR => ev.Prereqs.Count == 0 || ev.Prereqs.Any(s.OwnedEvolutions.Contains),
        PrereqMode.ANY_TWO => ev.Prereqs.Count(s.OwnedEvolutions.Contains) >= 2,
        _ => ev.Prereqs.All(s.OwnedEvolutions.Contains),
    };
}
