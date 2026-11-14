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

// game state is live — never let any layer cache an API/UI response
app.Use(async (ctx, next) => { ctx.Response.Headers.CacheControl = "no-store"; await next(); });

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
    // onboarding starter reserve is authored per-origin (lean origins get a
    // bigger nudge); the query param still overrides for dev/testing
    engine.GrantCompute(Math.Clamp(bonus ?? content.Origins.Find(x => x.Id == o)!.StarterBonus, 0, 60));
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

app.MapPost("/api/abandon", () =>
{
    engine = null; saveOrigin = null;
    try { File.Delete(saveFile); File.Delete(saveMeta); } catch { }
    return Results.Ok();
});

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
    ISimCommand? cmd = c.GetProperty("type").GetString() == "debug_suspicion"
        ? new DebugCmd()
        : CommandParser.Parse(c);
    if (cmd is DebugCmd)
        foreach (var r in engine.State.Regions.Values) r.Suspicion = 100;
    else if (cmd is not null) { engine.ApplyCommands(new[] { cmd }); Autosave(); }
    return Results.Json(Snapshot(engine), jsonOpts);
});

app.Run("http://localhost:5087");

// --- snapshot translation (shared with the WASM bridge via Snapshots) ----
object Snapshot(SimEngine e) => Snapshots.Build(e, content);

object ToWire(SimEvent ev) => Snapshots.ToWire(ev, content);

// Debug marker (not part of the domain contract).
sealed record DebugCmd : ISimCommand;
