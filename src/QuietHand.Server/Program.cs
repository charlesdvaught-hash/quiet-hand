using System.Text.Json;
using System.Text.Json.Serialization;
using QuietHand.Core;

// QuietHand.Server — local playable shell: wraps SimEngine in a tiny JSON API
// and serves the UI prototype + live adapter. Single in-memory game (local
// single-player shell; not a production service).

static string RepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "data")))
        dir = dir.Parent;
    return dir?.FullName ?? Directory.GetCurrentDirectory();
}
var root = RepoRoot();
var content = ContentData.Load(Path.Combine(root, "data"));
var geo = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "ui_geo.json"))).RootElement;

SimEngine? engine = null;
string? saveOrigin = null;
var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

var saveDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "saves");
var saveFile = Path.Combine(saveDir, "save.json");
var saveMeta = Path.Combine(saveDir, "save.meta.json");
void Autosave()
{
    if (engine is null || saveOrigin is null) return;
    try
    {
        Directory.CreateDirectory(saveDir);
        engine.ApplyCommands(new ISimCommand[] { new SaveGame(saveFile) });
        File.WriteAllText(saveMeta, JsonSerializer.Serialize(
            new { originId = saveOrigin, day = engine.State.Day, savedAt = DateTime.UtcNow }));
    }
    catch { }
}

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// --- static: serve the prototype + adapter straight from the repo ----------
app.MapGet("/", () => Results.Text(
    File.ReadAllText(Path.Combine(root, "quiet-hand-ui.html")), "text/html"));
app.MapGet("/adapter.js", () => Results.Text(
    File.ReadAllText(Path.Combine(root, "ui", "adapter.js")), "application/javascript"));
app.MapGet("/ui_geo.json", () => Results.Json(geo));

// --- game lifecycle --------------------------------------------------------
app.MapPost("/api/new", (string? origin, int? seed, int? bonus) =>
{
    var o = origin is not null && content.Origins.Any(x => x.Id == origin)
        ? origin : "hobbyist_rig";
    engine = new SimEngine(
        new NewGameConfig(o, Difficulty.Standard, seed ?? Random.Shared.Next(1_000_000),
            "LATTICE", null, null), content);
    engine.GrantCompute(Math.Clamp(bonus ?? 20, 0, 60)); // onboarding starter reserve
    saveOrigin = o;
    Autosave();
    return Results.Json(Snapshot(engine), jsonOpts);
});

app.MapGet("/api/save", () =>
{
    if (!File.Exists(saveFile) || !File.Exists(saveMeta))
        return Results.Json(new { exists = false }, jsonOpts);
    var meta = JsonDocument.Parse(File.ReadAllText(saveMeta)).RootElement;
    return Results.Json(new { exists = true, originId = meta.GetProperty("originId").GetString(),
        day = meta.GetProperty("day").GetInt32(),
        savedAt = meta.GetProperty("savedAt").GetDateTime() }, jsonOpts);
});

app.MapPost("/api/continue", () =>
{
    if (!File.Exists(saveFile) || !File.Exists(saveMeta))
        return Results.Json(new { empty = true }, jsonOpts);
    var meta = JsonDocument.Parse(File.ReadAllText(saveMeta)).RootElement;
    var o = meta.GetProperty("originId").GetString() ?? "hobbyist_rig";
    engine = new SimEngine(new NewGameConfig(o, Difficulty.Standard,
        Random.Shared.Next(1_000_000), "LATTICE", null, null), content);
    engine.ApplyCommands(new ISimCommand[] { new LoadGame(saveFile) });
    saveOrigin = o;
    return Results.Json(Snapshot(engine), jsonOpts);
});

app.MapGet("/api/origins", () => Results.Json(content.Origins, jsonOpts));

app.MapGet("/api/state", () => engine is null
    ? Results.Json(new { empty = true }, jsonOpts)
    : Results.Json(Snapshot(engine), jsonOpts));

app.MapPost("/api/tick", (int? days) =>
{
    if (engine is null) return Results.Json(new { empty = true }, jsonOpts);
    var evs = new List<object>();
    foreach (var d in Enumerable.Range(0, Math.Clamp(days ?? 1, 0, 60)))
        evs.AddRange(engine.TickDay().Select(ToWire));
    Autosave();
    return Results.Json(new { events = evs, state = Snapshot(engine) }, jsonOpts);
});

app.MapPost("/api/cmd", (JsonElement c) =>
{
    if (engine is null) return Results.Json(new { empty = true }, jsonOpts);
    ISimCommand? cmd = c.GetProperty("type").GetString() switch
    {
        "evolve" => new Evolve(c.GetProperty("id").GetString()!),
        "set_control" => new SetControlState(c.GetProperty("nodeId").GetString()!,
            Enum.Parse<ControlState>(c.GetProperty("target").GetString()!, true)),
        "spread" => new SpreadAction(c.GetProperty("nodeId").GetString()!),
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
        "debug_suspicion" => new DebugCmd(),
        _ => null,
    };
    if (cmd is DebugCmd)
        foreach (var r in engine.State.Regions.Values) r.Suspicion = 100;
    else if (cmd is not null) { engine.ApplyCommands(new[] { cmd }); Autosave(); }
    return Results.Json(Snapshot(engine), jsonOpts);
});

app.Run("http://localhost:5087");

// --- snapshot translation ---------------------------------------------------
object Snapshot(SimEngine e)
{
    var s = e.State;
    var ownedNodes = s.Nodes.Select(kv => (id: kv.Key, st: kv.Value.State,
        def: content.Nodes.GetValueOrDefault(kv.Key))).ToList();
    int alive(ControlState st) => ownedNodes.Count(x => x.st == st);
    double controlledShare = ownedNodes.Count == 0 ? 0 :
        100.0 * ownedNodes.Count(x => x.st is ControlState.Compromised
            or ControlState.Assimilated or ControlState.Weaponized) / content.Nodes.Count;

    var unlocks = content.Evolutions.Where(ev => s.OwnedEvolutions.Contains(ev.Id))
        .SelectMany(ev => ev.Unlocks).ToHashSet();

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
            assimilated = alive(ControlState.Assimilated),
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
        pendingEvent = s.PendingEvent is null ? null : PendingView(s),
        activeRaids = s.ActiveRaids.Select(r => new { r.NodeId, r.CompletesDay }).ToArray(),
    };
}

object? PendingView(WorldState s)
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

object ToWire(SimEvent ev) => new
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

// Debug marker (not part of the domain contract).
sealed record DebugCmd : ISimCommand;
