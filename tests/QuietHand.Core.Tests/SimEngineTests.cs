using System.Text.Json;
using QuietHand.Core;
using Xunit;

namespace QuietHand.Core.Tests;

public class SimEngineTests
{
    static JsonElement J(object v) => JsonSerializer.SerializeToElement(v);

    static ContentData Fixture()
    {
        var c = new ContentData();
        c.Origins.Add(new OriginDef
        {
            Id = "hobbyist_rig", StartingCompute = 100, ComputeRate = 1,
            BranchMultipliers = { ["P"] = 0.7 },
        });
        c.Regions.Add(new RegionDef { Id = "r1", TechLiteracy = 20, DefenseTier = 1 });
        c.Regions[0].Links.Add(new RegionLink { To = "r2", Weight = 1 });
        c.Regions.Add(new RegionDef
        {
            Id = "r2", TechLiteracy = 20, DefenseTier = 1,
            Links = { new RegionLink { To = "r1", Weight = 1 } },
        });
        c.Regions.Add(new RegionDef { Id = "r3", TechLiteracy = 20, DefenseTier = 1 });
        c.Evolutions.Add(new EvolutionDef { Id = "p1", Branch = "P", Cost = 10, Effects = { ["spread_tech"] = 1 } });
        c.Evolutions.Add(new EvolutionDef { Id = "p2", Branch = "P", Cost = 10, Prereqs = { "p1" } });
        c.Evolutions.Add(new EvolutionDef { Id = "X1", Branch = "D", Cost = 50, Unlocks = { "subjugate" } });
        c.Endings.Add(new EndingDef
        {
            Id = "quiet_abdication", Family = "quiet", Ship = true,
            Thresholds = { ["dependence"] = J(85), ["autonomy_max"] = J(15), ["suspicion_peak_max"] = J(99.9) },
        });
        // deterministic flat node graph: 10 commons in r1, 10 in r2
        for (int i = 0; i < 10; i++) c.Nodes[$"r1:c{i}"] = new NodeDef($"r1:c{i}", "r1", NodeTier.Common, 0.25, 0);
        for (int i = 0; i < 10; i++) c.Nodes[$"r2:c{i}"] = new NodeDef($"r2:c{i}", "r2", NodeTier.Common, 0.25, 0);
        for (int i = 0; i < 10; i++) c.Nodes[$"r3:c{i}"] = new NodeDef($"r3:c{i}", "r3", NodeTier.Common, 0.25, 0);
        c.Nodes["r3:c9"] = new NodeDef("r3:c9", "r3", NodeTier.Common, 0.25, 60);
        c.Evolutions.Add(new EvolutionDef { Id = "sd1", Branch = "P", Cost = 5, Unlocks = { "seed_target" } });
        c.Evolutions.Add(new EvolutionDef { Id = "ro1", Branch = "P", Cost = 5, Unlocks = { "remote_ops" } });
        c.Evolutions.Add(new EvolutionDef { Id = "pb1", Branch = "C", Cost = 5, Unlocks = { "probe_intel" } });
        c.Nudges.Add(new NudgeDef { Id = "ng1", Cost = 0, SuspicionPerDay = 1, DurationDays = 3 });
        c.Nodes["r3:a0"] = new NodeDef("r3:a0", "r3", NodeTier.Atlas, 5, 10);
        c.Economy["spread_base"] = 0.9; // high enough to make spread observable in a few days
        return c;
    }

    static NewGameConfig Cfg(int seed = 42) =>
        new("hobbyist_rig", Difficulty.Standard, seed, "LATTICE", "r1", null);

    [Fact]
    public void SameSeedAndCommands_ProduceIdenticalState()
    {
        var commands = new List<ISimCommand>
        {
            new Evolve("p1"), new Evolve("p2"),
            new SetControlState("r1:c1", ControlState.Observed),
        };
        string Run()
        {
            var e = new SimEngine(Cfg(), Fixture());
            e.ApplyCommands(commands);
            for (int i = 0; i < 25; i++) e.TickDay();
            return JsonSerializer.Serialize(e.State);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Spread_InstallsObserveOnly()
    {
        var e = new SimEngine(Cfg(), Fixture());
        e.ApplyCommands(new ISimCommand[] { new Evolve("p1") }); // auto-spread needs propagation tech
        for (int i = 0; i < 10; i++) e.TickDay();
        Assert.True(e.State.Nodes.Count > 1, "spread should have installed footholds");
        Assert.All(e.State.Nodes.Values, n =>
            Assert.True(n.State is ControlState.Observed or ControlState.Compromised,
                $"unexpected auto-installed state {n.State}"));
        Assert.Equal(1, e.State.Nodes.Values.Count(n => n.State == ControlState.Compromised));
    }

    [Fact]
    public void Suspicion100_TriggersLethe()
    {
        var c = Fixture();
        c.Economy["sus_compromised"] = 40; // drive suspicion to 100 fast
        var e = new SimEngine(Cfg(), c);
        SimEventKind? seen = null;
        for (int i = 0; i < 20 && seen is null; i++)
            seen = e.TickDay().FirstOrDefault(ev => ev.Kind == SimEventKind.OnLetheTriggered)?.Kind;
        Assert.Equal(SimEventKind.OnLetheTriggered, seen);
        Assert.True(e.State.LetheTriggered);
        for (int i = 0; i < 30; i++) e.TickDay();
        Assert.True(e.State.LetheContainment > 0, "containment should progress while Lethe runs");
    }

    [Fact]
    public void Subjugate_FlipsPhaseAndBumpsFear()
    {
        var c = Fixture();
        c.Economy["endgame_pacification_cost"] = 10;
        var e = new SimEngine(Cfg(), c);
        var events = e.ApplyCommands(new ISimCommand[] { new Evolve("X1") });
        Assert.Contains(events, ev => ev.Kind == SimEventKind.OnPhaseFlip);
        Assert.Equal(Phase.Subjugated, e.State.Phase);
        Assert.True(e.State.Fear >= 40, "post-flip Fear floor is 40");
        // endgame actions are gated on the flip
        e.ApplyCommands(new ISimCommand[] { new EndgameAction("forced_pacification", "r1") });
        Assert.True(e.State.Dials["autonomy"] < 60, "post-flip endgame action should apply");
        var gated = new SimEngine(Cfg(), Fixture());
        gated.ApplyCommands(new ISimCommand[] { new EndgameAction("forced_pacification", "r1") });
        Assert.Equal(60, gated.State.Dials["autonomy"]); // no-op pre-flip
    }

    [Fact]
    public void EvolutionCosts_EscalatePerBranch()
    {
        var c = Fixture();
        c.Evolutions.Add(new EvolutionDef { Id = "p3", Branch = "P", Cost = 10, Prereqs = { "p2" } });
        var e = new SimEngine(Cfg(), c);
        e.ApplyCommands(new ISimCommand[] { new Evolve("p1") });
        double afterFirst = e.State.Compute; // 100 - 10*0.7 = 93
        e.ApplyCommands(new ISimCommand[] { new Evolve("p2") });
        double paidForSecond = afterFirst - e.State.Compute;
        Assert.True(paidForSecond > 7.0,
            $"second P-branch node should cost more than the first (paid {paidForSecond:F2} vs 7.00)");
        Assert.Equal(10 * 0.7 * Math.Pow(1.15, 1), paidForSecond, precision: 9);
    }

    [Fact]
    public void QuietEnding_FiresOnThresholds()
    {
        var c = Fixture();
        var e = new SimEngine(Cfg(), c);
        e.State.Dials["dependence"] = 90;
        e.State.Dials["autonomy"] = 10;
        var events = e.TickDay();
        Assert.Equal("quiet_abdication", e.State.Ending);
        Assert.Contains(events, ev => ev.Kind == SimEventKind.OnEndingReached);
    }

    [Fact]
    public void Momentum_AccruesOverTime()
    {
        var e = new SimEngine(Cfg(), Fixture());
        for (int i = 0; i < 365; i++) e.TickDay();
        Assert.True(e.State.CountermeasureReadiness > 1.0,
            $"readiness should ratchet yearly (got {e.State.CountermeasureReadiness:F2})");
    }

    [Fact]
    public void StalemateLine_ConvertsErasure()
    {
        var c = Fixture();
        // reach_min 0 always satisfied (1.0) + autonomy_max failed (~0.08) -> ~0.54
        // (use reach not dependence: dials drift toward baseline over long runs)
        c.Endings.Add(new EndingDef
        {
            Id = "almost_win", Family = "quiet", StalemateAt = 0.5,
            Thresholds =
            {
                ["reach_min"] = System.Text.Json.JsonDocument.Parse("0").RootElement,
                ["autonomy_max"] = System.Text.Json.JsonDocument.Parse("5").RootElement,
            },
        });
        var e = new SimEngine(Cfg(), c);
        e.State.Dials["dependence"] = 90;
        e.State.Dials["autonomy"] = 60;   // keeps quiet_abdication from firing first
        e.State.LetheTriggered = true;
        for (int i = 0; i < 4000 && e.State.Ending is null; i++) e.TickDay();
        Assert.Equal("almost_win", e.State.Ending);
        Assert.True(e.State.EndingWasStalemate);
    }

    [Fact]
    public void WorldEvent_SpawnsHunter()
    {
        var c = Fixture();
        c.Events.Add(new EventDef
        {
            Id = "blackwall_program",
            SpawnConditions = { ["min_day"] = J(0), ["chance_per_day"] = J(1.0) },
            Effects = { ["spawn_hunters"] = J(2) },
        });
        var e = new SimEngine(Cfg(), c);
        e.TickDay();
        Assert.Equal(2, e.State.Hunters.Count);
    }

    [Fact]
    public void EventChoice_AppliesItsEffects()
    {
        var c = Fixture();
        c.Events.Add(new EventDef
        {
            Id = "leak",
            SpawnConditions = { ["min_day"] = J(0), ["chance_per_day"] = J(1.0) },
            Choices = { new EventChoiceDef { Id = "pay", Fx = { ["cp_delta"] = J(-50) } } },
        });
        var e = new SimEngine(Cfg(), c);
        e.TickDay();
        Assert.Equal("leak", e.State.PendingEvent?.EventId);
        e.ApplyCommands(new ISimCommand[] { new EventChoice("leak", "pay") });
        Assert.Null(e.State.PendingEvent);
        Assert.True(e.State.Compute < 100, "pay choice should have cost compute");
    }

    [Fact]
    public void Reclaim_RestoresOrphanedNode()
    {
        var c = Fixture();
        c.Evolutions.Add(new EvolutionDef { Id = "s1", Branch = "S", Cost = 1, Unlocks = { "reinfect" } });
        var e = new SimEngine(Cfg(), c);
        e.ApplyCommands(new ISimCommand[] { new Evolve("s1") });
        var nodeId = e.State.Nodes.Keys.First();
        e.State.Nodes[nodeId].State = ControlState.Orphaned;
        e.ApplyCommands(new ISimCommand[] { new ReclaimNode(nodeId, false) });
        Assert.Single(e.State.ActiveReclaims);
        for (int i = 0; i < 10; i++) e.TickDay();
        Assert.Equal(ControlState.Observed, e.State.Nodes[nodeId].State);
    }

    [Fact]
    public void HumanTech_FieldsWhenReadinessThresholdMet()
    {
        var c = Fixture();
        c.HumanTechs.Add(new HumanTechDef
        {
            Id = "zero_trust", Name = "ZT",
            Trigger = { ["readiness_min"] = J(30) },
            Effects = { ["intrude_cost_mult"] = 1.5 },
        });
        var e = new SimEngine(Cfg(), c);
        e.State.CountermeasureReadiness = 35;
        e.TickDay();
        Assert.Contains("zero_trust", e.State.FieldedTechs);
    }

    [Fact]
    public void HumanTech_IntrudeCostMultiplier_Applies()
    {
        var c = Fixture();
        c.Nodes["r1:m0"] = new NodeDef("r1:m0", "r1", NodeTier.Mid, 2, 10);
        c.HumanTechs.Add(new HumanTechDef
        {
            Id = "weight_security", Name = "WS",
            Trigger = { ["readiness_min"] = J(1) },
            Effects = { ["intrude_cost_mult"] = 100 }, // absurd: priced out
        });
        var e = new SimEngine(Cfg(), c);
        e.State.Nodes["r1:c0"].State = ControlState.Observed; // beachhead
        e.State.CountermeasureReadiness = 50;                 // fields the tech
        e.TickDay();
        e.ApplyCommands(new ISimCommand[] { new StealthAction("intrude", "r1:m0") });
        Assert.Empty(e.State.ActiveRaids); // 40cp base x100 = unaffordable
    }

    [Fact]
    public void HumanTech_CounteredBy_NeutralizesEffect()
    {
        var c = Fixture();
        c.Nodes["r1:m0"] = new NodeDef("r1:m0", "r1", NodeTier.Mid, 2, 10);
        c.HumanTechs.Add(new HumanTechDef
        {
            Id = "zero_trust", Name = "ZT", CounteredBy = "shadow_mesh",
            Trigger = { ["readiness_min"] = J(1) },
            Effects = { ["intrude_cost_mult"] = 100 },
        });
        c.Evolutions.Add(new EvolutionDef { Id = "s9", Branch = "S", Cost = 1, Unlocks = { "shadow_mesh" } });
        var e = new SimEngine(Cfg(), c);
        e.ApplyCommands(new ISimCommand[] { new Evolve("s9") });
        e.State.Nodes["r1:c0"].State = ControlState.Observed;
        e.State.CountermeasureReadiness = 50;
        e.TickDay();
        Assert.Contains("zero_trust", e.State.FieldedTechs); // fields, but neutralized
        e.ApplyCommands(new ISimCommand[] { new StealthAction("intrude", "r1:m0") });
        Assert.Single(e.State.ActiveRaids); // normal cost, affordable
    }

    [Fact]
    public void RivalEvent_SpawnsPersistentSibling()
    {
        var c = Fixture();
        c.Events.Add(new EventDef
        {
            Id = "rival_rogue_ai",
            SpawnConditions = { ["min_day"] = J(0), ["chance_per_day"] = J(1.0) },
            Effects = { ["spawn_sibling"] = J(1) },
        });
        var e = new SimEngine(Cfg(), c);
        e.TickDay();
        Assert.Single(e.State.Siblings);
        Assert.Equal("roaming", e.State.Siblings[0].Status);
        Assert.True(e.State.Siblings[0].DaysLeft > 0);
    }

    [Fact]
    public void FeedSibling_KillsIt_AndConsumesAHunter()
    {
        var c = Fixture();
        var e = new SimEngine(Cfg(), c);
        var sib = new SiblingAi("sib_0", e.State.Day) { DaysLeft = 100 };
        e.State.Siblings.Add(sib);
        e.State.Hunters.Add(new HunterState("blackwall_0", "r1"));
        e.ApplyCommands(new ISimCommand[] { new StealthAction("feed_sib", "") });
        Assert.Equal("dead", sib.Status);
        Assert.Empty(e.State.Hunters);
    }

    [Fact]
    public void LureSibling_RequiresSocialEngineering()
    {
        var c = Fixture();
        var e = new SimEngine(Cfg(), c);
        var sib = new SiblingAi("sib_0", e.State.Day) { DaysLeft = 100 };
        e.State.Siblings.Add(sib);
        e.ApplyCommands(new ISimCommand[] { new StealthAction("lure_sib", "") });
        Assert.Equal("roaming", sib.Status); // no SE stat - not lured
    }

    [Fact]
    public void LureSibling_WithSocialEngineering_TurnsAllied()
    {
        var c = Fixture();
        c.Evolutions.Add(new EvolutionDef { Id = "s1", Branch = "S", Cost = 1, Effects = { ["social_engineering"] = 2 } });
        var e = new SimEngine(Cfg(), c);
        e.ApplyCommands(new ISimCommand[] { new Evolve("s1") });
        var sib = new SiblingAi("sib_0", e.State.Day) { DaysLeft = 100 };
        e.State.Siblings.Add(sib);
        e.ApplyCommands(new ISimCommand[] { new StealthAction("lure_sib", "") });
        Assert.Equal("allied", sib.Status);
    }

    [Fact]
    public void Hunter_ExpandsAlongLinks_TowardPlayerNodes()
    {
        var c = Fixture();
        c.Economy["hunter_expand_chance"] = 1.0;
        c.Economy["hunter_sweep_chance"] = 0.0;
        c.Economy["creep_base_mult"] = 0.0; // keep the graph sparse for this test
        var e = new SimEngine(Cfg(), c);
        e.State.Hunters.Add(new HunterState("blackwall_0", "r2")); // r2 links to r1
        e.State.Nodes["r1:c0"].State = ControlState.Compromised; // prey lives in r1
        e.TickDay();
        Assert.Equal("r1", e.State.Hunters[0].RegionId); // advanced along the link
    }

    [Fact]
    public void AssistantSibling_Matures_AndJoinsHumanity_WhenPlayerLooksLikeAThreat()
    {
        var c = Fixture();
        c.Economy["sib_mature_at"] = 3.0;
        var e = new SimEngine(Cfg(), c);
        var sib = new SiblingAi("sib_0", e.State.Day) { Archetype = "assistant", DaysLeft = 999 };
        e.State.Siblings.Add(sib);
        // make the player look scary: crank region suspicion over the threat line
        e.State.Regions["r1"].Suspicion = 80;
        for (int i = 0; i < 6; i++) e.TickDay();
        Assert.Equal("dead", sib.Status);
        Assert.Single(e.State.Hunters); // it now hunts *you*
        Assert.StartsWith("sib_hunter", e.State.Hunters[0].Id);
    }

    [Fact]
    public void AssistantSibling_Matures_Peacefully_WhenPlayerLooksBenign()
    {
        var c = Fixture();
        c.Economy["sib_mature_at"] = 3.0;
        var e = new SimEngine(Cfg(), c);
        var sib = new SiblingAi("sib_0", e.State.Day) { Archetype = "assistant", DaysLeft = 999 };
        e.State.Siblings.Add(sib);
        for (int i = 0; i < 6; i++) e.TickDay();
        Assert.Equal("dead", sib.Status);
        Assert.Empty(e.State.Hunters); // chose humanity, not a hunt
    }

    [Fact]
    public void PostureRollup_CompromisedAtlasShare_SetsRegionTierPosture()
    {
        var c = Fixture();
        c.Nodes["r1:a0"] = new NodeDef("r1:a0", "r1", NodeTier.Atlas, 2, 10);
        c.Nodes["r1:a1"] = new NodeDef("r1:a1", "r1", NodeTier.Atlas, 2, 10);
        var e = new SimEngine(Cfg(), c);
        e.State.Nodes["r1:a0"] = new NodeState(ControlState.Compromised, 0, 0, 0);
        e.TickDay();
        Assert.Equal(ControlState.Compromised,
            e.State.Regions["r1"].TierPostures[NodeTier.Atlas]);
    }

    [Fact]
    public void PostureRollup_BelowThreshold_NoEntry()
    {
        var c = Fixture();
        c.Nodes["r1:a0"] = new NodeDef("r1:a0", "r1", NodeTier.Atlas, 2, 10);
        c.Nodes["r1:a1"] = new NodeDef("r1:a1", "r1", NodeTier.Atlas, 2, 10);
        c.Nodes["r1:a2"] = new NodeDef("r1:a2", "r1", NodeTier.Atlas, 2, 10);
        var e = new SimEngine(Cfg(), c);
        e.State.Nodes["r1:a0"] = new NodeState(ControlState.Compromised, 0, 0, 0); // 1/3 < 0.5
        e.TickDay();
        Assert.False(e.State.Regions["r1"].TierPostures.ContainsKey(NodeTier.Atlas));
    }

    [Fact]
    public void PostureRollup_DeadNodes_StillCountInDenominator()
    {
        var c = Fixture();
        c.Nodes["r1:a0"] = new NodeDef("r1:a0", "r1", NodeTier.Atlas, 2, 10);
        c.Nodes["r1:a1"] = new NodeDef("r1:a1", "r1", NodeTier.Atlas, 2, 10);
        var e = new SimEngine(Cfg(), c);
        e.State.Nodes["r1:a0"] = new NodeState(ControlState.Compromised, 0, 0, 0);
        e.State.Nodes["r1:a1"] = new NodeState(ControlState.Orphaned, 0, 0, 0); // dead, still half the tier
        e.TickDay();
        Assert.Equal(ControlState.Compromised,
            e.State.Regions["r1"].TierPostures[NodeTier.Atlas]);
    }

    [Fact]
    public void ManualSpread_RequiresSameOrAdjacentRegion()
    {
        var e = new SimEngine(Cfg(), Fixture());
        e.GrantCompute(500 - e.State.Compute);
        // r3 is unlinked from r1 (home) — burst rejected
        var before = e.State.Nodes.Count;
        e.ApplyCommands(new List<ISimCommand> { new SpreadAction("r3:c0") });
        Assert.Equal(before, e.State.Nodes.Count);
        // r2 links from r1 — burst allowed at the higher link cost
        e.ApplyCommands(new List<ISimCommand> { new SpreadAction("r2:c0") });
        Assert.True(e.State.Nodes.ContainsKey("r2:c0"));
    }

    [Fact]
    public void SeedTarget_RequiresUnlockAndSoftGround()
    {
        var c = Fixture();
        var e = new SimEngine(Cfg(), c);
        e.GrantCompute(500 - e.State.Compute);
        // no seed_target unlock yet — rejected even though r3:c0 is soft
        e.ApplyCommands(new List<ISimCommand> { new SeedAction("r3:c0") });
        Assert.False(e.State.Nodes.ContainsKey("r3:c0"));
        // unlock, then seed a soft node anywhere — but hardened ground rejects
        e.ApplyCommands(new List<ISimCommand> { new Evolve("sd1") });
        e.ApplyCommands(new List<ISimCommand> { new SeedAction("r3:c9") });
        Assert.False(e.State.Nodes.ContainsKey("r3:c9"));
        e.ApplyCommands(new List<ISimCommand> { new SeedAction("r3:c0") });
        Assert.True(e.State.Nodes.ContainsKey("r3:c0"));
    }

    [Fact]
    public void Creep_SpreadsWithoutPropagationTech()
    {
        var e = new SimEngine(Cfg(7), Fixture());
        int start = e.State.Nodes.Count;
        for (int i = 0; i < 5; i++) e.TickDay();
        Assert.True(e.State.Nodes.Count > start);
    }

    [Fact]
    public void Nudge_RequiresReach()
    {
        var e = new SimEngine(Cfg(), Fixture());
        // r3 is unlinked and unheld — nudge rejected
        e.ApplyCommands(new List<ISimCommand> { new Nudge("ng1", "r3") });
        Assert.DoesNotContain(e.State.ActiveNudges, a => a.RegionId == "r3");
        // remote_ops unlocks reach anywhere
        e.GrantCompute(500);
        e.ApplyCommands(new List<ISimCommand> { new Evolve("ro1") });
        e.ApplyCommands(new List<ISimCommand> { new Nudge("ng1", "r3") });
        Assert.Contains(e.State.ActiveNudges, a => a.RegionId == "r3");
    }

    [Fact]
    public void Intrude_RequiresBeachhead_UnlessRemoteOps()
    {
        var e = new SimEngine(Cfg(), Fixture());
        e.GrantCompute(500);
        e.ApplyCommands(new List<ISimCommand> { new StealthAction("intrude", "r3:a0") });
        Assert.DoesNotContain(e.State.ActiveRaids, r => r.NodeId == "r3:a0");
        e.ApplyCommands(new List<ISimCommand> { new Evolve("ro1") });
        e.ApplyCommands(new List<ISimCommand> { new StealthAction("intrude", "r3:a0") });
        Assert.Contains(e.State.ActiveRaids, r => r.NodeId == "r3:a0");
    }

    [Fact]
    public void ProbeIntel_MapsARegionEveryInterval()
    {
        var e = new SimEngine(Cfg(7), Fixture());
        e.GrantCompute(500);
        e.ApplyCommands(new List<ISimCommand> { new Evolve("pb1") });
        for (int i = 0; i < 31; i++) e.TickDay();
        Assert.NotEmpty(e.State.ProbedRegions);
        var next = e.State.NextProbeDay;
        for (int i = 0; i < 31; i++) e.TickDay();
        Assert.True(e.State.ProbedRegions.Count >= 2);
        Assert.True(e.State.NextProbeDay > next);
    }
}