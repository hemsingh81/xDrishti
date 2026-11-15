using XDrishti.Application.Replay.Reads;

namespace XDrishti.Application.Replay.Sim;

/// <summary>One day of a simulation (SR-04, AC 7): the picks (the plan made at its close), the blocked with reasons, the fills, the trades that entered or exited on it, the closing equity and the result hash. <see cref="Stored"/> is false for the settled cursor day before it is advanced.</summary>
public sealed record SimDaySnapshotView(
    Guid RunId,
    DateOnly Session,
    bool Stored,
    IReadOnlyList<ReplayCandidateView> Picks,
    IReadOnlyList<ReplayBlockedView> Blocked,
    IReadOnlyList<ReplayFillView> Fills,
    IReadOnlyList<SimDayTrade> Trades,
    decimal EquityClose,
    decimal? DrawdownPct,
    int OpenPositions,
    IReadOnlyList<string> Notes,
    string ResultHash,
    IReadOnlyList<ReplayLabelView> Hindsight,
    string CostsLabel);
