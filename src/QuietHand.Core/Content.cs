using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuietHand.Core;

// ---- Content DTOs (data/*.json shapes per CONTRACTS.md) ----

public sealed class OriginDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TierUnlock { get; set; } = "";
    public string StartScope { get; set; } = "";
    public double StartingCompute { get; set; }
    public double ComputeRate { get; set; }
    public double StartingSuspicion { get; set; }
    public double StartingFear { get; set; }
    public Dictionary<string, double> BranchMultipliers { get; set; } = new();
    public string? FreeNode { get; set; }
    public OriginPassive Passive { get; set; } = new();
    public string Desc { get; set; } = "";

    public double BranchMult(BranchId b) =>
        BranchMultipliers.TryGetValue(b.ToString(), out var m) ? m : 1.0;
}

public sealed class OriginPassive
{
    public Dictionary<string, double> StatBonuses { get; set; } = new();
    public List<string> Flags { get; set; } = new();
    public double RefundBonus { get; set; }
    public Dictionary<string, double> Multipliers { get; set; } = new();
    public string Desc { get; set; } = "";
}

public enum PrereqMode { AND, OR, ANY_TWO }

public sealed class EvolutionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Branch { get; set; } = "P";
    public int Tier { get; set; }
    public double Cost { get; set; }
    public List<string> Prereqs { get; set; } = new();
    public PrereqMode PrereqMode { get; set; } = PrereqMode.AND;
    // Effect keys are SimStat names; stored as strings so unknown data keys can't break the load.
    public Dictionary<string, double> Effects { get; set; } = new();
    public List<string> Unlocks { get; set; } = new();
    public string TechBasis { get; set; } = "";
    public string TechSource { get; set; } = "";
    public string TechStatus { get; set; } = "";
    public string Desc { get; set; } = "";

    public BranchId BranchId => Enum.TryParse<BranchId>(Branch, out var b) ? b : QuietHand.Core.BranchId.P;
    public double Stat(SimStat s) => Effects.TryGetValue(s.ToString(), out var v) ? v : 0;
}

public sealed class ComboDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Requires { get; set; } = new();
    public Dictionary<string, double> Effects { get; set; } = new();
    public string Desc { get; set; } = "";
}

public sealed class RegionDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Population { get; set; }
    public double Wealth { get; set; }
    public double Urbanization { get; set; }
    public double Governance { get; set; }
    [JsonPropertyName("pressFreedom")] public double PressFreedom { get; set; }
    [JsonPropertyName("defenseTier")] public double DefenseTier { get; set; }
    public string Climate { get; set; } = "";
    [JsonPropertyName("techLiteracy")] public double TechLiteracy { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<RegionLink> Links { get; set; } = new();
}

public sealed class RegionLink
{
    public string To { get; set; } = "";
    public string Kind { get; set; } = "cable"; // cable|land|air|satellite
    public double Weight { get; set; } = 1.0;
}

public sealed class KeystoneDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Region { get; set; } = "";
    public string Tier { get; set; } = "T3"; // "T1..T5|wildcard"
    public double Compute { get; set; }
    public double Defense { get; set; }
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public string SourceStatus { get; set; } = "";

    public NodeTier NodeTier => Tier.ToLowerInvariant() switch
    {
        "t1" => QuietHand.Core.NodeTier.KeystoneT1,
        "t2" => QuietHand.Core.NodeTier.KeystoneT2,
        "wildcard" => QuietHand.Core.NodeTier.Wildcard,
        _ => QuietHand.Core.NodeTier.KeystoneT3to5,
    };
}

public sealed class EndingDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Family { get; set; } = "quiet"; // quiet|conquest
    public Dictionary<string, double> Meters { get; set; } = new();
    public Dictionary<string, JsonElement> Thresholds { get; set; } = new();
    public string Speed { get; set; } = "";
    public string Polarity { get; set; } = "";
    public string Mechanism { get; set; } = "";
    public string Severity { get; set; } = "";
    public string SourceNote { get; set; } = "";
    public bool Ship { get; set; } = true;
    public double StalemateAt { get; set; } = 0.9;   // §10 stalemate line, fraction of ending progress
    public string LossHint { get; set; } = "";       // §10a in-fiction near-miss coverage
    public string Epilogue { get; set; } = "";       // §10a ending-screen text
}

public sealed class ScenarioDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public string? DifficultyLock { get; set; }
    public ScenarioOverrides Overrides { get; set; } = new();
}

public sealed class ScenarioOverrides
{
    public Dictionary<string, JsonElement> Regions { get; set; } = new();
    public Dictionary<string, double> Dials { get; set; } = new();
    [JsonPropertyName("startNodes")] public List<string> StartNodes { get; set; } = new();
    [JsonPropertyName("originLock")] public string? OriginLock { get; set; }
    public ScenarioThreat Threat { get; set; } = new();
}

public sealed class ScenarioThreat
{
    public double Suspicion { get; set; }
    public double Lethe { get; set; }
}

public sealed class EventDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, JsonElement> SpawnConditions { get; set; } = new();
    public Dictionary<string, JsonElement> Effects { get; set; } = new();
    public string WireHeadline { get; set; } = "";
    public string MurmurText { get; set; } = "";
}

public sealed class NudgeDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Cost { get; set; }
    public int DurationDays { get; set; } = 1;
    public int CooldownDays { get; set; }
    public string UnlockGate { get; set; } = "";
    public double SuspicionPerDay { get; set; }
    public Dictionary<string, double> Dials { get; set; } = new();
    public Dictionary<string, double> Effects { get; set; } = new();
    public string Desc { get; set; } = "";
}

// ---- Static node graph (built at load: keystones + generated ambient mesh) ----

public sealed record NodeDef(string Id, string RegionId, NodeTier Tier, double BaseCompute, double Defense);

// ---- ContentData ----

public sealed class ContentData
{
    public List<OriginDef> Origins { get; set; } = new();
    public List<EvolutionDef> Evolutions { get; set; } = new();
    public List<ComboDef> Combos { get; set; } = new();
    public List<RegionDef> Regions { get; set; } = new();
    public List<KeystoneDef> Keystones { get; set; } = new();
    public List<EndingDef> Endings { get; set; } = new();
    public List<ScenarioDef> Scenarios { get; set; } = new();
    public List<EventDef> Events { get; set; } = new();
    public List<NudgeDef> Nudges { get; set; } = new();
    public Dictionary<string, double> Economy { get; set; } = new();
    public Dictionary<string, NodeDef> Nodes { get; set; } = new();

    public double Econ(string key, double fallback) =>
        Economy.TryGetValue(key, out var v) ? v : fallback;

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Loads every file that exists in dir; missing files yield empty lists.</summary>
    public static ContentData Load(string dir)
    {
        var c = new ContentData();
        c.Origins = LoadList<OriginDef>(dir, "origins.json", "origins");
        var (nodes, combos) = LoadEvolutions(dir);
        c.Evolutions = nodes; c.Combos = combos;
        c.Regions = LoadList<RegionDef>(dir, "regions.json", "regions");
        c.Keystones = LoadList<KeystoneDef>(dir, "keystones.json", "keystones");
        c.Endings = LoadList<EndingDef>(dir, "endings.json", "endings");
        c.Scenarios = LoadList<ScenarioDef>(dir, "scenarios.json", "scenarios");
        c.Events = LoadList<EventDef>(dir, "events.json", "events");
        c.Nudges = LoadList<NudgeDef>(dir, "nudges.json", "nudges");
        c.Economy = LoadEconomy(dir);
        c.BuildNodeGraph();
        return c;
    }

    /// <summary>Keystones + a generated per-region ambient mesh (Common + Atlas nodes).</summary>
    public void BuildNodeGraph()
    {
        Nodes = new Dictionary<string, NodeDef>();
        foreach (var k in Keystones)
            Nodes[k.Id] = new NodeDef(k.Id, k.Region, k.NodeTier, k.Compute, k.Defense);
        int commons = (int)Econ("ambient_nodes_per_region", 6);
        int atlases = (int)Econ("atlas_nodes_per_region", 2);
        foreach (var r in Regions)
        {
            double softCompute = Econ("compute_common", 0.25);
            double atlasCompute = Econ("compute_atlas", 2.0);
            for (int i = 0; i < commons; i++)
                Nodes[$"{r.Id}:c{i}"] = new NodeDef($"{r.Id}:c{i}", r.Id, NodeTier.Common,
                    softCompute, r.TechLiteracy * 0.5);
            for (int i = 0; i < atlases; i++)
                Nodes[$"{r.Id}:a{i}"] = new NodeDef($"{r.Id}:a{i}", r.Id, NodeTier.Atlas,
                    atlasCompute, r.DefenseTier * 10 + r.TechLiteracy * 0.3);
        }
    }

    private static List<T> LoadList<T>(string dir, string file, string key)
    {
        var path = Path.Combine(dir, file);
        if (!File.Exists(path)) return new();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        JsonElement arr = root.ValueKind == JsonValueKind.Array ? root
            : root.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Array ? el
            : default;
        return arr.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<T>>(arr.GetRawText(), JsonOpts) ?? new()
            : new();
    }

    private static (List<EvolutionDef>, List<ComboDef>) LoadEvolutions(string dir)
    {
        var path = Path.Combine(dir, "evolutions.json");
        if (!File.Exists(path)) return (new(), new());
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var evos = root.TryGetProperty("nodes", out var n) || root.TryGetProperty("evolutions", out n)
            ? JsonSerializer.Deserialize<List<EvolutionDef>>(n.GetRawText(), JsonOpts) ?? new()
            : new();
        var combos = root.TryGetProperty("combos", out var cb)
            ? JsonSerializer.Deserialize<List<ComboDef>>(cb.GetRawText(), JsonOpts) ?? new()
            : new();
        return (evos, combos);
    }

    private static Dictionary<string, double> LoadEconomy(string dir)
    {
        var path = Path.Combine(dir, "economy.json");
        if (!File.Exists(path)) return new();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var src = root.TryGetProperty("constants", out var el) || root.TryGetProperty("values", out el)
            ? el : root;
        var d = new Dictionary<string, double>();
        if (src.ValueKind == JsonValueKind.Object)
            foreach (var p in src.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDouble(out var v))
                    d[p.Name] = v;
        return d;
    }
}
