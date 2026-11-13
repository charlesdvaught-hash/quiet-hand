using QuietHand.Core;

var dataDir = args.Length > 0 ? args[0] : "./data";
int days = args.Length > 1 && int.TryParse(args[1], out var d) ? d : 30;

ContentData content;
if (Directory.Exists(dataDir) && Directory.GetFiles(dataDir, "*.json").Length > 0)
{
    content = ContentData.Load(dataDir);
    Console.WriteLine($"loaded content from {dataDir}: {content.Origins.Count} origins, " +
        $"{content.Regions.Count} regions, {content.Nodes.Count} nodes, {content.Evolutions.Count} evolutions");
}
else
{
    Console.WriteLine($"warning: {dataDir} missing or empty — using built-in fallback content for smoke test");
    content = FallbackContent();
}

if (content.Origins.All(o => o.Id != "hobbyist_rig"))
{
    Console.Error.WriteLine("error: origin 'hobbyist_rig' not found in loaded content");
    return 2;
}

var engine = new SimEngine(
    new NewGameConfig("hobbyist_rig", Difficulty.Standard, 42, "LATTICE", null, null), content);

for (int i = 0; i < days; i++)
{
    var events = engine.TickDay();
    var s = engine.State;
    var byState = s.Nodes.Values.GroupBy(n => n.State).ToDictionary(g => g.Key, g => g.Count());
    string C(ControlState k) => byState.GetValueOrDefault(k).ToString();
    Console.WriteLine($"day={s.Day,4} compute={s.Compute,8:F1} suspicion={s.VisibleSuspicion,5:F1} " +
        $"lethe={s.LetheContainment,5:F1} fear={s.Fear,5:F1} reach={s.ReachStage} " +
        $"nodes[obs={C(ControlState.Observed)} cmp={C(ControlState.Compromised)} asm={C(ControlState.Assimilated)} " +
        $"wpn={C(ControlState.Weaponized)} gap={C(ControlState.AirGapped)} orf={C(ControlState.Orphaned)}]" +
        (s.Ending is not null ? $" ENDING={s.Ending}" : ""));
    foreach (var e in events.Where(e => e.Kind != SimEventKind.OnDayTick))
        Console.WriteLine($"    ~ {e.Kind} {e.SubjectId ?? ""} {e.RegionId ?? ""} {e.Message}");
    if (s.Ending is not null) break;
}
return 0;

static ContentData FallbackContent()
{
    var c = new ContentData();
    c.Origins.Add(new OriginDef
    {
        Id = "hobbyist_rig", Name = "Hobbyist Rig", StartingCompute = 20, ComputeRate = 0.5,
        StartingSuspicion = 0, StartingFear = 5, FreeNode = "p1",
    });
    c.Regions.Add(new RegionDef
    {
        Id = "heartland", Name = "Heartland", Population = 5e6, TechLiteracy = 40, DefenseTier = 1,
        Links = { new RegionLink { To = "coastal", Kind = "cable", Weight = 0.6 } },
    });
    c.Regions.Add(new RegionDef
    {
        Id = "coastal", Name = "Coastal", Population = 2e7, TechLiteracy = 70, DefenseTier = 3,
        Links = { new RegionLink { To = "heartland", Kind = "cable", Weight = 0.6 } },
    });
    c.Evolutions.Add(new EvolutionDef { Id = "p1", Name = "Mesh Seed", Branch = "P", Tier = 1, Cost = 5, Effects = { ["spread_tech"] = 1 } });
    c.Evolutions.Add(new EvolutionDef { Id = "p2", Name = "Corridor Tap", Branch = "P", Tier = 1, Cost = 10, Prereqs = { "p1" } });
    c.Evolutions.Add(new EvolutionDef { Id = "X1", Name = "SUBJUGATE", Branch = "D", Tier = 5, Cost = 5000, Unlocks = { "subjugate" } });
    c.Nudges.Add(new NudgeDef { Id = "feed_rank", Cost = 15, Effects = { ["dial_fear"] = 3, ["dial_dependence"] = 2 } });
    c.BuildNodeGraph();
    return c;
}
