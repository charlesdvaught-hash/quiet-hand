namespace QuietHand.Headless;

using QuietHand.Core;

/// <summary>
/// Deterministic scripted player for smoke runs + Monte Carlo.
/// Strategy: free spread → activate cheap footholds → buy toward two capstones →
/// manage heat below the Closing Net → flip when Lethe forces it or late game.
/// </summary>
sealed class PolicyBot
{
    readonly ContentData _c;
    readonly string _origin;
    int _assimToday, _evolveToday;

    public PolicyBot(ContentData c, string originId) { _c = c; _origin = originId; }

    static readonly string[] Capstones = { "P9", "C9", "S9", "D9" };

    public List<ISimCommand> Decide(SimEngine e)
    {
        var s = e.State;
        var cmds = new List<ISimCommand>();

        foreach (var ic in s.ActiveIntercepts.ToList())
            cmds.Add(new InterceptAction(ic.Id));

        double reserve = 15;

        // Activate latent web: compromise cheap footholds while liquid.
        foreach (var (id, ns) in s.Nodes.Where(kv => kv.Value.State == ControlState.Observed))
        {
            if (s.Compute < reserve + 4) break;
            cmds.Add(new SetControlState(id, ControlState.Compromised));
            reserve += 4;
        }

        // Assimilate for income when cool; never more than 2/day; skip when hot.
        _assimToday = 0;
        if (s.VisibleSuspicion < 55 && _assimToday < 2)
            foreach (var (id, ns) in s.Nodes.Where(kv => kv.Value.State == ControlState.Compromised))
            {
                var def = _c.Nodes.GetValueOrDefault(id);
                if (def is null || def.Tier is not (NodeTier.Common or NodeTier.Mid)) continue;
                if (s.Compute < reserve + 12 || _assimToday >= 2) break;
                cmds.Add(new SetControlState(id, ControlState.Assimilated));
                reserve += 12; _assimToday++;
            }

        // Cool off: downgrade an assimilated node if suspicion runs hot.
        if (s.VisibleSuspicion > 70)
        {
            var hot = s.Nodes.FirstOrDefault(kv => kv.Value.State == ControlState.Assimilated);
            if (hot.Key is not null) cmds.Add(new SetControlState(hot.Key, ControlState.Compromised));
        }

        // Evolve: one purchase/day — prefer unlock-bearing nodes, then cheapest.
        _evolveToday = 0;
        var cand = _c.Evolutions
            .Where(ev => !s.OwnedEvolutions.Contains(ev.Id) && PrereqsMet(ev, s))
            .Where(ev => ev.Id != "X1")
            .OrderByDescending(ev => ev.Unlocks.Count > 0)
            .ThenBy(ev => ev.Cost)
            .FirstOrDefault();
        if (cand is not null && s.Compute > cand.Cost + 25)
            cmds.Add(new Evolve(cand.Id));

        // Grow dependence when rich.
        var nudge = _c.Nudges.FirstOrDefault(n => n.Effects.ContainsKey("dial_dependence"));
        if (nudge is not null && s.Dials.GetValueOrDefault("dependence") < 70 && s.Compute > nudge.Cost + 40)
            cmds.Add(new Nudge(nudge.Id, s.Regions.Keys.First()));

        // SUBJUGATE: flip when any two capstones owned and (Lethe is running or deep game).
        int caps = Capstones.Count(s.OwnedEvolutions.Contains);
        bool wantFlip = s.Phase == Phase.Covert && caps >= 2
            && (s.LetheTriggered || s.Day > 1200);
        if (wantFlip) cmds.Add(new Evolve("X1"));

        // Post-flip: lean on endgame actions.
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
