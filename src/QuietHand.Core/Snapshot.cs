using System.Text.Json;

namespace QuietHand.Core;

/// Single source of truth for the UI-facing snapshot + command wire format
/// (pinned in CONTRACTS.md). Shared by the local server shell and the WASM
/// bridge so both expose an identical API surface.
public static class Snapshots
{
    public static object Build(SimEngine e, ContentData content)
    {
        var s = e.State;
        var ownedNodes = s.Nodes.Select(kv => (id: kv.Key, st: kv.Value.State,
            def: content.Nodes.GetValueOrDefault(kv.Key))).ToList();
        int alive(ControlState st) => ownedNodes.Count(x => x.st == st);
        double controlledShare = ownedNodes.Count == 0 ? 0 :
            100.0 * ownedNodes.Count(x => x.st is ControlState.Compromised
                or ControlState.Trojan or ControlState.Weaponized) / content.Nodes.Count;

        var unlocks = e.Features.ToHashSet();

        return new
        {
            day = s.Day,
            compute = Math.Round(s.Compute, 1),
            suspicion = Math.Round(s.VisibleSuspicion, 1),
            suspicionPeak = Math.Round(s.SuspicionPeak, 1),
            lethe = Math.Round(s.LetheContainment, 1),
            letheTriggered = s.LetheTriggered,
            purgePhase = s.PurgePhase,
            phase = s.Phase.ToString(),
            fear = Math.Round(s.Fear, 1),
            readiness = Math.Round(s.CountermeasureReadiness, 1),
            dials = s.Dials.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 1)),
            reach = s.ReachStage,
            controlledShare = Math.Round(controlledShare, 1),
            nodeCounts = new
            {
                observed = alive(ControlState.Observed),
                compromised = alive(ControlState.Compromised),
                trojan = alive(ControlState.Trojan),
                weaponized = alive(ControlState.Weaponized),
                airgapped = alive(ControlState.AirGapped),
                orphaned = alive(ControlState.Orphaned),
            },
            ending = s.Ending,
            homeRegion = s.Nodes.Keys.Select(id => content.Nodes.GetValueOrDefault(id)?.RegionId)
                .FirstOrDefault(r => r is not null),
            ownedEvolutions = s.OwnedEvolutions.ToArray(),
            unlocks = unlocks.ToArray(),
            hunters = s.Hunters.Count,
            fieldedTechs = s.FieldedTechs.ToArray(),
            siblings = s.Siblings.Where(x => x.Status != "dead").Select(x => new
            { x.Id, x.Archetype, x.Status, x.DaysLeft }).ToArray(),
            regions = s.Regions.Select(kv => new
            {
                id = kv.Key,
                name = content.Regions.Find(r => r.Id == kv.Key)?.Name ?? kv.Key,
                suspicion = Math.Round(kv.Value.Suspicion, 1),
                postures = kv.Value.TierPostures.ToDictionary(p => p.Key.ToString(), p => p.Value.ToString()),
                links = kv.Value.LinkIds,
                ownedWeight = s.Nodes.Where(n => n.Key.StartsWith(kv.Key + ':')
                    && n.Value.State is not (ControlState.AirGapped or ControlState.Orphaned))
                    .Sum(n => n.Key.Split(':')[1][0] switch { 'a' => 4, 't' => 8, 'k' => 8, _ => 1 }),
                probed = s.ProbedRegions.Contains(kv.Key),
                intel = s.ProbedRegions.Contains(kv.Key) ? new
                {
                    defense = content.Nodes.Values.Where(n => n.RegionId == kv.Key)
                        .Select(n => (int)n.Defense).DefaultIfEmpty().Average(),
                    hosts = content.Nodes.Values.Count(n => n.RegionId == kv.Key
                        && n.Tier is NodeTier.Common or NodeTier.Mid),
                    income = Math.Round(content.Nodes.Values.Where(n => n.RegionId == kv.Key)
                        .Sum(n => n.BaseCompute), 1),
                } : null,
            }).ToArray(),
            evolutions = content.Evolutions.Select(ev => new
            {
                ev.Id, ev.Name, ev.Branch, ev.Tier,
                cost = ev.Cost,
                owned = s.OwnedEvolutions.Contains(ev.Id),
                canBuy = !s.OwnedEvolutions.Contains(ev.Id) && ev.PrereqMode switch
                {
                    PrereqMode.OR => ev.Prereqs.Count == 0 || ev.Prereqs.Any(s.OwnedEvolutions.Contains),
                    PrereqMode.ANY_TWO => ev.Prereqs.Count(s.OwnedEvolutions.Contains) >= 2,
                    _ => ev.Prereqs.All(s.OwnedEvolutions.Contains),
                },
                prereqs = ev.Prereqs, prereqMode = ev.PrereqMode.ToString(),
                effects = ev.Effects, unlocksList = ev.Unlocks, desc = ev.Desc,
            }).ToArray(),
            nudges = content.Nudges.Select(n => new
            {
                n.Id, n.Name, n.UnlockGate, n.Cost, n.CooldownDays, n.DurationDays,
                n.Dials, n.SuspicionPerDay, desc = n.Desc,
                unlocked = n.UnlockGate == "" || unlocks.Contains(n.UnlockGate),
                active = s.ActiveNudges.Any(a => a.NudgeId == n.Id),
            }).ToArray(),
            keystones = content.Keystones.Select(k => new
            {
                k.Id, k.Name, region = k.Region, tier = k.Tier, nodeTier = k.NodeTier.ToString(), k.Compute, k.Defense,
                k.Status,
                nodeState = s.Nodes.TryGetValue(k.Id, out var ns) ? ns.State.ToString() : null,
                raiding = s.ActiveRaids.Any(r => r.NodeId == k.Id),
            }).ToArray(),
            pendingEvent = s.PendingEvent is null ? null : PendingView(s, content),
            activeRaids = s.ActiveRaids.Select(r => new { r.NodeId, r.CompletesDay }).ToArray(),
            // ambient Atlas farms — every generated facility, so the map can
            // draw each site as its own dot instead of folding them into
            // region aggregates. ~2/region.
            // routes creep found into unheld regions — tap-to-connect signals
            pendingLinks = s.PendingLinks.Select(p => new
            {
                p.NodeId, p.RegionId, p.ExpiresDay,
                daysLeft = p.ExpiresDay - s.Day,
            }).ToArray(),
            atlasNodes = content.Nodes.Values.Where(n => n.Tier == NodeTier.Atlas)
                .Select(n => new
                {
                    n.Id, n.RegionId, n.BaseCompute, n.Defense,
                    nodeState = s.Nodes.TryGetValue(n.Id, out var ns) ? ns.State.ToString() : null,
                    raiding = s.ActiveRaids.Any(r => r.NodeId == n.Id),
                }).ToArray(),
        };
    }

    public static object ToWire(SimEvent ev, ContentData content) => new
    {
        kind = ev.Kind.ToString(),
        subject = ev.SubjectId,
        region = ev.RegionId,
        value = ev.Value,
        msg = ev.Message,
        murmur = ev.Kind == SimEventKind.OnWorldEvent && ev.SubjectId is string sid
            ? content.Events.Find(x => x.Id == sid)?.MurmurText : null,
        headline = ev.Kind == SimEventKind.OnWorldEvent && ev.SubjectId is string sid2
            ? content.Events.Find(x => x.Id == sid2)?.WireHeadline : null,
    };

    static object? PendingView(WorldState s, ContentData content)
    {
        var ev = content.Events.Find(x => x.Id == s.PendingEvent!.EventId);
        if (ev is null) return null;
        return new
        {
            ev.Id, ev.Name, headline = ev.WireHeadline, murmur = ev.MurmurText,
            choices = ev.Choices.Select(ch => new { ch.Id, ch.Label, ch.Hint }).ToArray(),
            expiresDay = s.PendingEvent.ExpiresDay,
        };
    }
}

/// Translates /api/cmd JSON bodies into engine commands. `debug_suspicion`
/// stays server-side; everything else is the shared contract.
public static class CommandParser
{
    public static ISimCommand? Parse(JsonElement c) => c.GetProperty("type").GetString() switch
    {
        "evolve" => new Evolve(c.GetProperty("id").GetString()!),
        "set_control" => new SetControlState(c.GetProperty("nodeId").GetString()!,
            Enum.Parse<ControlState>(c.GetProperty("target").GetString()!, true)),
        "spread" => new SpreadAction(c.GetProperty("nodeId").GetString()!),
        "connect" => new ConnectAction(c.GetProperty("nodeId").GetString()!),
        "seed" => new SeedAction(c.GetProperty("nodeId").GetString()!),
        "stealth" => new StealthAction(c.GetProperty("action").GetString()!,
            c.TryGetProperty("nodeId", out var n) ? n.GetString()! : ""),
        "nudge" => new Nudge(c.GetProperty("id").GetString()!,
            c.TryGetProperty("regionId", out var r) ? r.GetString()! : ""),
        "event_choice" => new EventChoice(c.GetProperty("eventId").GetString()!,
            c.GetProperty("choiceId").GetString()!),
        "reclaim" => new ReclaimNode(c.GetProperty("nodeId").GetString()!,
            c.TryGetProperty("buy", out var b) && b.GetBoolean()),
        "endgame" => new EndgameAction(c.GetProperty("action").GetString()!,
            c.TryGetProperty("regionId", out var r) ? r.GetString()! : ""),
        "intercept" => new InterceptAction(c.GetProperty("id").GetString()!),
        _ => null,
    };
}
