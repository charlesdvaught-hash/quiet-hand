using QuietHand.Core;
using QuietHand.Headless;

var dataDir = args.Length > 0 ? args[0] : "./data";
int days = args.Length > 1 && int.TryParse(args[1], out var d) ? d : 3650;
int seed = args.Length > 2 && int.TryParse(args[2], out var sd) ? sd : 42;
int mc = args.Contains("--mc") && int.TryParse(args.SkipWhile(a => a != "--mc").Skip(1).FirstOrDefault() ?? "", out var m) ? m : 0;
bool verbose = !args.Contains("--quiet") && mc == 0;

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

var routeArg = args.SkipWhile(a => a != "--route").Skip(1).FirstOrDefault() ?? "generalist";
var routes = args.Contains("--routes") ? QuietHand.Headless.Routes.All
    : QuietHand.Headless.Routes.All.Where(r => r.Id == routeArg).ToArray();
if (routes.Length == 0) { Console.Error.WriteLine($"unknown route '{routeArg}'"); return 2; }

if (mc > 0)
{
    foreach (var route in routes)
    {
        var results = new Dictionary<string, int>();
        var daysToEnd = new List<int>();
        for (int s2 = 1; s2 <= mc; s2++)
        {
            var (ending, endDay) = RunGame(content, s2, days, verbose: false, route);
            results[ending ?? $"survived_{days}d"] = results.GetValueOrDefault(ending ?? $"survived_{days}d") + 1;
            daysToEnd.Add(endDay);
        }
        Console.WriteLine($"\n== {route.Id}: {mc} seeds, {days}-day cap ==");
        foreach (var kv in results.OrderByDescending(kv => kv.Value))
            Console.WriteLine($"  {kv.Key,-28} {kv.Value,4} ({100.0 * kv.Value / mc:F0}%)");
        Console.WriteLine($"  median end day: {daysToEnd.OrderBy(x => x).ElementAt(daysToEnd.Count / 2)}");
    }
    return 0;
}

RunGame(content, seed, days, verbose, routes[0]);
return 0;

static (string? ending, int endDay) RunGame(ContentData content, int seed, int days, bool verbose, RouteProfile route)
{
    var engine = new SimEngine(
        new NewGameConfig("hobbyist_rig", Difficulty.Standard, seed, "LATTICE", null, null), content);
    var bot = new PolicyBot(content, route);

    for (int i = 0; i < days; i++)
    {
        engine.ApplyCommands(bot.Decide(engine));
        var events = engine.TickDay();
        var s = engine.State;
        if (verbose && (s.Day < 400 || s.Day % 30 == 0 || s.Ending is not null))
        {
            var byState = s.Nodes.Values.GroupBy(n => n.State).ToDictionary(g => g.Key, g => g.Count());
            string C(ControlState k) => byState.GetValueOrDefault(k).ToString();
            Console.WriteLine($"day={s.Day,4} compute={s.Compute,8:F1} suspicion={s.VisibleSuspicion,5:F1} " +
                $"lethe={s.LetheContainment,5:F1} fear={s.Fear,5:F1} reach={s.ReachStage} " +
                $"evo={s.OwnedEvolutions.Count,2} phase={s.Phase} " +
                $"nodes[obs={C(ControlState.Observed)} cmp={C(ControlState.Compromised)} asm={C(ControlState.Trojan)} " +
                $"wpn={C(ControlState.Weaponized)} gap={C(ControlState.AirGapped)} orf={C(ControlState.Orphaned)}]" +
                (s.Ending is not null ? $" ENDING={s.Ending}" : ""));
        }
        if (verbose)
            foreach (var e in events.Where(e => e.Kind is not (SimEventKind.OnDayTick or SimEventKind.OnInterceptSpawned)))
                Console.WriteLine($"    ~ {e.Kind} {e.SubjectId ?? ""} {e.RegionId ?? ""} {e.Message}");
        if (s.Ending is not null)
        {
            if (verbose) PrintEpilogue(engine, content);
            return (s.Ending, s.Day);
        }
    }
    if (verbose) PrintEpilogue(engine, content);
    return (null, engine.State.Day);
}

// §10a: end-screen fiction — recap the run actually taken, then on defeat print
// the world's in-fiction near-miss coverage (loss hint) + how charged the
// nearest ending was when it all ended.
static void PrintEpilogue(SimEngine engine, ContentData content)
{
    var s = engine.State;
    var ending = content.Endings.FirstOrDefault(e => e.Id == s.Ending);
    Console.WriteLine("\n== EPILOGUE ==");
    Console.WriteLine($"  {(ending?.Name ?? s.Ending ?? $"survived {s.Day} days")}" +
        $"{(s.EndingWasStalemate ? " — STALEMATE LINE (containment finished past the violet line)" : "")}");
    Console.WriteLine($"  day {s.Day} (~{s.Day / 365.0:F1}y) · nodes {s.Nodes.Count} · evolutions {s.OwnedEvolutions.Count} " +
        $"· peak suspicion {s.SuspicionPeak:F0} · readiness {s.CountermeasureReadiness:F1} · phase {s.Phase}");
    if (ending?.Epilogue.Length > 0) Console.WriteLine($"  {ending.Epilogue}");
    if (ending?.Family == "defeat")
    {
        var near = engine.NearestEnding();
        if (near is { } n)
            Console.WriteLine($"  NEAR-MISS: {n.def.Name} was {n.progress:P0} charged when the run ended.");
        if (ending?.LossHint.Length > 0) Console.WriteLine($"  WIRE: {ending.LossHint}");
    }
}

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
