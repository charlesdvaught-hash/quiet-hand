using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuietHand.Core;

/// Source-generated serialization for content DTOs — required on browser-wasm
/// where reflection-based deserialization can't bind constructors. Settings
/// mirror ContentData.JsonOpts (snake_case, string enums, lenient parsing).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    Converters = [typeof(JsonStringEnumConverter)])]
[JsonSerializable(typeof(List<OriginDef>))]
[JsonSerializable(typeof(List<EvolutionDef>))]
[JsonSerializable(typeof(List<ComboDef>))]
[JsonSerializable(typeof(List<RegionDef>))]
[JsonSerializable(typeof(List<KeystoneDef>))]
[JsonSerializable(typeof(List<EndingDef>))]
[JsonSerializable(typeof(List<ScenarioDef>))]
[JsonSerializable(typeof(List<EventDef>))]
[JsonSerializable(typeof(List<NudgeDef>))]
[JsonSerializable(typeof(List<HumanTechDef>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class QHContentJson : JsonSerializerContext;

/// State save/load — plain defaults to match SimEngine.SerOpts exactly.
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WorldState))]
public partial class QHStateJson : JsonSerializerContext;
