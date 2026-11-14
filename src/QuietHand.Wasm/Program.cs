using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using QuietHand.Core;

// QuietHand.Wasm — the game running fully in-browser. Same engine, same
// snapshot wire format as the server; persistence is JS-side (localStorage).

Console.WriteLine("[QuietHand] wasm runtime ready");

namespace QuietHand.Wasm
{
/// <summary>
/// JS-facing bridge. Every method takes/returns JSON strings so the wire
/// format is identical to the HTTP API the adapter already speaks.
/// </summary>
public partial class Bridge
{
    static ContentData content = null!;
    static SimEngine? engine;
    static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase);
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // source-gen for known types, reflection fallback for anonymous shapes
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            QHContentJson.Default, QHStateJson.Default, new DefaultJsonTypeInfoResolver()),
    };

    static string Norm(string p) => Path.GetFileName(p.Replace('\\', '/'));
    static string J(object o) => JsonSerializer.Serialize(o, JsonOpts);
    const string Empty = "{\"empty\":true}";

    static void InitIO()
    {
        SimIO.Exists = p => Files.ContainsKey(Norm(p));
        SimIO.ReadAllText = p => Files[Norm(p)];
        SimIO.WriteAllText = (p, t) => Files[Norm(p)] = t;
    }

    /// <summary>Preload: JS fetches all data/*.json and passes {file: text}.</summary>
    [JSExport]
    public static void Init(string filesJson)
    {
        InitIO();
        foreach (var kv in JsonSerializer.Deserialize<Dictionary<string, string>>(filesJson)!)
            Files[Norm(kv.Key)] = kv.Value;
        content = ContentData.Load("data");
    }

    [JSExport]
    public static string NewGame(string origin, int seed)
    {
        var o = content.Origins.Any(x => x.Id == origin) ? origin : "hobbyist_rig";
        engine = new SimEngine(
            new NewGameConfig(o, Difficulty.Standard,
                seed > 0 ? seed : Random.Shared.Next(1_000_000), "LATTICE", null, null),
            content);
        engine.GrantCompute(Math.Clamp(content.Origins.Find(x => x.Id == o)!.StarterBonus, 0, 60));
        return J(Snapshots.Build(engine, content));
    }

    [JSExport]
    public static string Continue(string stateJson, string originId)
    {
        if (string.IsNullOrEmpty(stateJson)) return Empty;
        var o = string.IsNullOrEmpty(originId) ? "hobbyist_rig" : originId;
        engine = new SimEngine(
            new NewGameConfig(o, Difficulty.Standard,
                Random.Shared.Next(1_000_000), "LATTICE", null, null),
            content);
        Files["save.json"] = stateJson;
        engine.ApplyCommands(new ISimCommand[] { new LoadGame("save.json") });
        return J(Snapshots.Build(engine, content));
    }

    [JSExport]
    public static string State() => engine is null ? Empty : J(Snapshots.Build(engine, content));

    [JSExport]
    public static string Origins() => J(content.Origins);

    [JSExport]
    public static void Abandon()
    {
        engine = null;
        Files.Remove("save.json");
    }

    [JSExport]
    public static string Tick(int days)
    {
        if (engine is null) return Empty;
        var evs = new List<object>();
        foreach (var _ in Enumerable.Range(0, Math.Clamp(days, 0, 60)))
            evs.AddRange(engine.TickDay().Select(ev => Snapshots.ToWire(ev, content)));
        return J(new { events = evs, state = Snapshots.Build(engine, content) });
    }

    [JSExport]
    public static string Command(string body)
    {
        if (engine is null) return Empty;
        using var doc = JsonDocument.Parse(body);
        var cmd = CommandParser.Parse(doc.RootElement);
        if (cmd is not null) engine.ApplyCommands(new[] { cmd });
        return J(Snapshots.Build(engine, content));
    }

    /// <summary>Serialize the live state for JS-side persistence (localStorage).</summary>
    [JSExport]
    public static string? GetSave()
    {
        if (engine is null) return null;
        engine.ApplyCommands(new ISimCommand[] { new SaveGame("save.json") });
        return Files.TryGetValue("save.json", out var s) ? s : null;
    }
}
}
