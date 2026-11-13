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
        c.Regions.Add(new RegionDef
        {
            Id = "r2", TechLiteracy = 20, DefenseTier = 1,
            Links = { new RegionLink { To = "r1", Weight = 1 } },
        });
        c.Evolutions.Add(new EvolutionDef { Id = "p1", Branch = "P", Cost = 10 });
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
}
