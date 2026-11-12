using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sweeps;

/// <summary>One start of a sweep: its run and the figures so far (each null where it cannot be told yet).</summary>
public sealed record ReplaySweepRunView(Guid RunId, DateOnly From, string State, int SessionsDone, int SessionsTotal, decimal? FinalEquity, double? Cagr, decimal? MaxDrawdownPct, int? Trades, bool? BeatsBenchmark, string? FailReason);

/// <summary>
/// A sweep (SR-02): the hindsight labels first, "this sweep adds N replay looks", one row per start in date order, and the spread over the finished starts (min, median and max of CAGR and max drawdown, the share with CAGR above 0, the share beating the benchmark).
/// <see cref="State"/> is running while any run is active, else completed, cancelled or failed; <see cref="Complete"/> is true only when every start completed.
/// </summary>
public sealed record ReplaySweepView(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid SweepId,
    DateOnly To,
    DateTimeOffset CreatedAt,
    string State,
    bool Complete,
    int LooksAdded,
    int CompletedStarts,
    string CostsLabel,
    IReadOnlyList<ReplaySweepRunView> Starts,
    ReplaySweepSpread Spread);

/// <summary>The answer of a cancel of a sweep: how many runs were stopped or cancelled.</summary>
public sealed record ReplaySweepCancelView(int RunsStopped);
