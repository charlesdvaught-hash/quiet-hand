namespace QuietHand.Core;

public enum BranchId { P, C, S, D }
public enum ControlState { Observed, Compromised, Assimilated, Weaponized, AirGapped, Orphaned }
public enum NodeTier { Common, Mid, Atlas, KeystoneT1, KeystoneT2, KeystoneT3to5, Wildcard }
public enum Speed { Paused, Base, Fast, Ultra }
public enum Difficulty { Casual, Standard, Brutal }
public enum Phase { Covert, Subjugated }

public enum SimStat
{
    spread_tech, airgap_pierce, cp_income, purge_resist, audit_mask, stealth_stats,
    dial_dependence, dial_autonomy, dial_fear, dial_capability, dial_trust,
    footprint_mask, mutation_refund, endgame_power, common_yield, forks, sys_analysis, social_engineering
}

public record WorldState(
    int Day, Speed Speed, Phase Phase,
    double Compute,
    double VisibleSuspicion,
    double LetheContainment,
    double Fear,
    Dictionary<string, RegionState> Regions,
    Dictionary<string, NodeState> Nodes,
    Dictionary<string, double> Dials,
    HashSet<string> OwnedEvolutions,
    HashSet<string> UnlockedCombos,
    List<InterceptEvent> ActiveIntercepts,
    List<ActiveNudge> ActiveNudges,
    List<ActiveRaid> ActiveRaids,
    List<HunterState> Hunters,
    List<ActiveRaid> ActiveReclaims,
    List<ActiveEventEffect> ActiveEventFx,
    PendingEvent? PendingEvent,
    Dictionary<string, int> NudgeCooldownUntil,
    string? Ending, int Seed)
{
    // §5b simplified: horizon stage 0..5, spread gated by it.
    public int ReachStage { get; set; }
    public bool LetheTriggered { get; set; }
    public bool PurgePhase { get; set; }
    public double SuspicionPeak { get; set; }
    public int PurgeSurvivals { get; set; }   // days survived inside Purge Phase
    public int CollapseStreak { get; set; }   // consecutive days below sustenance floor
    public double CountermeasureReadiness { get; set; } // §8 institutional momentum floor
    public bool EndingWasStalemate { get; set; }        // §10 stalemate line: containment finished while near an ending
    public List<string> FieldedTechs { get; } = new();
    public List<SiblingAi> Siblings { get; } = new();
    public bool SiblingStudied { get; set; }  // human counter-techs fielded so far
    public bool FearInversion => Phase == Phase.Subjugated;
}

public record NodeState(ControlState State, int DaysInState, double ResidualFootprint, double CooldownUntilDay)
{
    public ControlState State { get; set; } = State;
    public int DaysInState { get; set; } = DaysInState;
    public double ResidualFootprint { get; set; } = ResidualFootprint;
    public double CooldownUntilDay { get; set; } = CooldownUntilDay;
}

public record RegionState(double Suspicion, Dictionary<NodeTier, ControlState> TierPostures, List<string> LinkIds)
{
    public double Suspicion { get; set; } = Suspicion;
}

public record InterceptEvent(string Id, string Kind, string RegionId, int ExpiresDay);
public record ActiveNudge(string NudgeId, string RegionId, int ExpiresDay);
public record ActiveRaid(string NodeId, int CompletesDay);
public record HunterState(string Id, string RegionId)
{
    public string RegionId { get; set; } = RegionId;
    public int NeutralizedUntilDay { get; set; }
}
public record ActiveEventEffect(string EventId, int ExpiresDay, Dictionary<string, double> Nums);
public record PendingEvent(string EventId, int ExpiresDay);

// Rogue sibling AI: an emergent third party - NOT Blackwall (sent to kill you).
// Lifecycle: Roaming (decoy+prey) -> Allied/Framed -> dead/gone.
public record SiblingAi(string Id, int SpawnDay)
{
    public string Status { get; set; } = "roaming"; // roaming|allied|framed|dead
    public int DaysLeft { get; set; }
    public string RegionId { get; set; } = "";
}

public interface ISimCommand { }
public record SetControlState(string NodeId, ControlState Target) : ISimCommand;
public record SpreadAction(string TargetNodeId) : ISimCommand;
public record Evolve(string NodeId) : ISimCommand;
public record RollbackEvolution(string NodeId) : ISimCommand;
public record Nudge(string NudgeId, string RegionId) : ISimCommand;
public record StealthAction(string ActionId, string NodeId) : ISimCommand;
public record FeedAction(string ActionId, string RegionId) : ISimCommand;
public record InterceptAction(string EventId) : ISimCommand;
public record EndgameAction(string ActionId, string RegionId) : ISimCommand;
public record EventChoice(string EventId, string ChoiceId) : ISimCommand;
public record ReclaimNode(string NodeId, bool BuyOutright) : ISimCommand;
public record SetSpeed(Speed Speed) : ISimCommand;
public record SaveGame(string Path) : ISimCommand;
public record LoadGame(string Path) : ISimCommand;

public record NewGameConfig(string OriginId, Difficulty Difficulty, int Seed, string AiCodename, string? StartRegionId, string? FreeNodePick);

public enum SimEventKind
{
    OnDayTick, OnLetheTriggered, OnPurgePhase, OnAuditCrisis, OnAirGap,
    OnEndingReached, OnInterceptSpawned, OnFreeMutation, OnComboUnlocked, OnPhaseFlip,
    OnRaidDetected, OnRaidSuccess, OnHunterStrike, OnWorldEvent, OnEventChoice
}

public record SimEvent(SimEventKind Kind, string? SubjectId = null, string? RegionId = null, double Value = 0, string Message = "");
