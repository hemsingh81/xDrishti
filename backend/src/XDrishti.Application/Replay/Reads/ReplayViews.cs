using System.Text.Json;

namespace XDrishti.Application.Replay.Reads;

/// <summary>One hindsight label: its code and the plain sentence the screen shows first (SR-01, AC 10).</summary>
public sealed record ReplayLabelView(string Code, string Text);

/// <summary>How far a run has got (AC 14): <see cref="Complete"/> is true only for a run whose every session was replayed.</summary>
public sealed record ReplayCompleteness(bool Complete, int SessionsDone, int SessionsTotal, DateOnly? AsOfSession);

/// <summary>One row of the list of runs.</summary>
public sealed record ReplayRunListItem(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid Id,
    Guid? PairId,
    DateTimeOffset CreatedAt,
    DateOnly From,
    DateOnly To,
    string Mode,
    string State,
    ReplayCompleteness Completeness,
    decimal? FinalEquity,
    decimal? MaxDrawdownPct,
    int TrialNumber,
    string CostsLabel,
    string Arm = "selection",
    Guid? SweepId = null);

/// <summary>The figures of a run over the sessions stored so far; each is null where it cannot be told (too few sessions, no trades).</summary>
public sealed record ReplayFigures(decimal Capital, decimal FinalEquity, decimal ReturnPct, double? Cagr, decimal? MaxDrawdownPct, double? Sharpe, double? Exposure, int Trades, int ClosedTrades, double? HitRate, double? MeanR);

/// <summary>What one strategy contributed: closed trades, result in rupees (net, or gross when the run is) and the sum of R.</summary>
public sealed record ReplayStrategyContribution(string Strategy, int Trades, decimal Result, double SumR);

/// <summary>The summary of one run: settings snapshot, hashes, hindsight labels first, completeness, figures and the costs label.</summary>
public sealed record ReplayRunView(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid Id,
    Guid? PairId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FinishedAt,
    DateOnly From,
    DateOnly To,
    string Mode,
    string State,
    string? FailReason,
    ReplayCompleteness Completeness,
    string CostsLabel,
    string Basis,
    int TrialNumber,
    string InputHash,
    string? ResultHash,
    JsonElement Parameters,
    JsonElement SettingsSnapshot,
    ReplayFigures? Figures,
    IReadOnlyList<ReplayStrategyContribution> Contribution,
    string Arm = "selection",
    Guid? SweepId = null);

/// <summary>One day of the equity curve: the equity, the drawdown from the peak so far, the cash (equity less the cost of the positions open at the close) and the benchmark rebased to the capital.</summary>
public sealed record ReplayEquityPoint(DateOnly Session, decimal Equity, decimal DrawdownPct, decimal Cash, decimal? Benchmark);

/// <summary>The equity curve of a run against its benchmark (Nifty 500 unless the owner chose another index for the basket reports).</summary>
public sealed record ReplayEquityView(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid RunId,
    ReplayCompleteness Completeness,
    string CostsLabel,
    decimal Capital,
    string? BenchmarkSymbol,
    string? BenchmarkReason,
    bool CashComplete,
    IReadOnlyList<ReplayEquityPoint> Points);

/// <summary>One session of the list: what was taken, what was blocked by reason, the day's result and the equity.</summary>
public sealed record ReplaySessionListItem(DateOnly Session, int Candidates, int Taken, IReadOnlyDictionary<string, int> BlockedByReason, decimal Pnl, decimal Equity, decimal? DrawdownPct, int OpenPositions, IReadOnlyList<ReplayLabelView> Labels);

/// <summary>The sessions of a run, one row each.</summary>
public sealed record ReplaySessionListView(IReadOnlyList<ReplayLabelView> Hindsight, Guid RunId, ReplayCompleteness Completeness, string CostsLabel, IReadOnlyList<ReplaySessionListItem> Sessions);

/// <summary>A candidate of a session.</summary>
public sealed record ReplayCandidateView(string Symbol, string Side, string CellKey, int Rank);

/// <summary>A candidate that was not taken, with its one reason.</summary>
public sealed record ReplayBlockedView(string Symbol, string CellKey, string Reason, string Detail);

/// <summary>An entry replayed on the next session.</summary>
public sealed record ReplayFillView(string Symbol, string Side, string CellKey, DateOnly Session, string Status, int Qty, decimal? EntryPrice, string Detail);

/// <summary>A position open at the close of the session.</summary>
public sealed record ReplayOpenPositionView(string Symbol, string Side, int Qty, DateOnly EntrySession, decimal EntryPrice, decimal StopPrice, bool Swing);

/// <summary>The full record of one session.</summary>
public sealed record ReplaySessionView(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid RunId,
    DateOnly Session,
    string CostsLabel,
    IReadOnlyList<ReplayCandidateView> Candidates,
    IReadOnlyList<ReplayCandidateView> Ranked,
    IReadOnlyList<ReplayCandidateView> Taken,
    IReadOnlyList<ReplayBlockedView> Blocked,
    IReadOnlyList<ReplayFillView> Fills,
    IReadOnlyList<string> Exits,
    decimal Equity,
    decimal? DrawdownPct,
    decimal? OpenRiskPct,
    int OpenPositions,
    IReadOnlyList<string> Notes,
    bool OpenBookComplete,
    IReadOnlyList<ReplayOpenPositionView> OpenBook);

/// <summary>One trade of the replay book.</summary>
public sealed record ReplayBookTradeView(
    long TradeNo,
    string Symbol,
    string Side,
    string Strategy,
    string CellKey,
    int Qty,
    DateOnly EntrySession,
    decimal EntryPrice,
    decimal StopPrice,
    bool Swing,
    DateOnly? ExitSession,
    decimal? GrossPnl,
    decimal? Charges,
    decimal? NetPnl,
    double? R);

/// <summary>The trades of a run.</summary>
public sealed record ReplayTradeListView(IReadOnlyList<ReplayLabelView> Hindsight, Guid RunId, ReplayCompleteness Completeness, string CostsLabel, IReadOnlyList<ReplayBookTradeView> Trades);

/// <summary>
/// The selection run against the no-selection baseline of one request (SR-02, D2), computed on read by the same paired day bootstrap as the frozen-versus-learning pair. <see cref="Answer"/> is null while a run is not complete, with the reason; the probabilities are null with too little history.
/// <see cref="Label"/> says what the baseline is (no choosing among the system's candidates, not random stocks) and <see cref="Shortcut"/> that equity is realised only.
/// </summary>
public sealed record ReplayArmComparisonView(
    ReplayRunListItem Selection,
    ReplayRunListItem Baseline,
    string? Answer,
    string? Reason,
    int? Days,
    int? InformativeDays,
    double? MeanDailyDifference,
    double? ProbabilitySelectionBetter,
    double? ProbabilityBaselineBetter,
    double? Low,
    double? High,
    double? Cut,
    double? HurtsBar,
    int? MinDays,
    string Label,
    string Shortcut);

/// <summary>The comparison of a pair (AC 9): frozen versus learning, or (a baseline pair) <see cref="SelectionVsBaseline"/> with <see cref="Frozen"/> and <see cref="Learning"/> null. Computed on read from the stored runs. <see cref="Answer"/> is null while a run is not complete, with the reason.</summary>
public sealed record ReplayPairView(
    IReadOnlyList<ReplayLabelView> Hindsight,
    Guid PairId,
    ReplayRunListItem? Frozen,
    ReplayRunListItem? Learning,
    string? Answer,
    string? Reason,
    int? Days,
    int? InformativeDays,
    double? MeanDailyDifference,
    double? ProbabilityLearningBetter,
    double? ProbabilityFrozenBetter,
    double? Low,
    double? High,
    double? Cut,
    double? HurtsBar,
    int? MinDays,
    ReplayArmComparisonView? SelectionVsBaseline = null);
