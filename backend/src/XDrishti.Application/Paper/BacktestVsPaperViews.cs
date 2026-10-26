using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Paper;

/// <summary>Backtest versus paper for one epoch (spec P10-04). Read only.</summary>
/// <param name="EpochId">One epoch, or null for the newest.</param>
/// <param name="Draws">Resamples (1,000 to 100,000), or null for 10,000.</param>
public sealed record GetBacktestVsPaperQuery(long? EpochId, int? Draws) : IQuery<Result<BacktestVsPaperView>>;

/// <summary>The counts of the epoch's tickets: what counts and what is set apart.</summary>
/// <param name="Counted">Tickets that count.</param>
/// <param name="Reserve">Reserve tickets.</param>
/// <param name="Straddle">Tickets that straddle a change.</param>
/// <param name="Open">Open tickets.</param>
/// <param name="OtherEpochs">Entries of other epochs.</param>
public sealed record CompareCountsView(int Counted, int Reserve, int Straddle, int Open, int OtherEpochs);

/// <summary>The charge sheet(s) of the counted trades and the owner's confirmation.</summary>
/// <param name="Id">The sheet id(s), or null when no counted trade was charged.</param>
/// <param name="ConfirmedOn">The day the owner confirmed them; null when not confirmed.</param>
public sealed record CompareChargeSheetView(string? Id, DateOnly? ConfirmedOn);

/// <summary>A 90% interval; each end is a figure.</summary>
/// <param name="Lo">Lower end.</param>
/// <param name="Hi">Upper end.</param>
public sealed record CompareIntervalView(MetricView Lo, MetricView Hi);

/// <summary>The paper side.</summary>
/// <param name="Mean">The mean R.</param>
/// <param name="Ci90">The mean's own 90% bootstrap interval.</param>
/// <param name="MaxDrawdownR">The largest fall of cumulative R in exit order.</param>
public sealed record ComparePaperView(MetricView Mean, CompareIntervalView Ci90, MetricView MaxDrawdownR);

/// <summary>The backtest band of the mean.</summary>
/// <param name="P05">5th percentile.</param>
/// <param name="P50">Median.</param>
/// <param name="P95">95th percentile.</param>
/// <param name="Rank">Percentile rank of the paper mean.</param>
/// <param name="Position">below, within or above; null when the band is not a figure.</param>
/// <param name="Draws">Resamples.</param>
/// <param name="Seed">The seed as text (64 bits).</param>
public sealed record CompareBandView(MetricView P05, MetricView P50, MetricView P95, MetricView Rank, string? Position, int Draws, string? Seed);

/// <summary>The backtest reference of the largest fall at the paper's trade count.</summary>
/// <param name="P50">Median.</param>
/// <param name="P90">90th percentile.</param>
/// <param name="P95">95th percentile.</param>
/// <param name="Position">within or above; null when the reference is not a figure.</param>
public sealed record CompareDrawdownView(MetricView P50, MetricView P90, MetricView P95, string? Position);

/// <summary>One verdict.</summary>
/// <param name="Verdict">met, not_met or not_evaluable.</param>
/// <param name="Label">The fact in words.</param>
public sealed record CompareVerdictView(string Verdict, string Label);

/// <summary>The two exit statistics.</summary>
/// <param name="Expectancy">E3.</param>
/// <param name="Drawdown">E4.</param>
public sealed record CompareCriteriaView(CompareVerdictView Expectancy, CompareVerdictView Drawdown);

/// <summary>One cell of the counted set against its reference.</summary>
/// <param name="CellKey">The cell.</param>
/// <param name="PaperN">Counted paper trades of the cell.</param>
/// <param name="PaperMean">Their mean R.</param>
/// <param name="ReferenceN">Backtest trades before the epoch.</param>
/// <param name="ReferenceMean">Their mean R.</param>
/// <param name="State">ok, no_reference, thin_reference or basis_differs.</param>
/// <param name="Reason">Why, when not ok.</param>
public sealed record CompareCellView(string CellKey, int PaperN, MetricView PaperMean, int ReferenceN, MetricView ReferenceMean, string State, string? Reason);

/// <summary>One point of the fan.</summary>
/// <param name="Index">Trades so far.</param>
/// <param name="P05">5th percentile of the draws' cumulative R.</param>
/// <param name="P50">Median.</param>
/// <param name="P95">95th percentile.</param>
/// <param name="Paper">The paper cumulative R.</param>
public sealed record CompareFanPointView(int Index, double P05, double P50, double P95, double Paper);

/// <summary>The plan-price grades of all primary tickets of the epoch, beside the comparison and never used by it.</summary>
/// <param name="Label">What it is.</param>
/// <param name="N">Graded tickets.</param>
/// <param name="Mean">Their mean R.</param>
public sealed record CompareSensitivityView(string Label, int N, MetricView Mean);

/// <summary>What the report rests on.</summary>
/// <param name="EpochStart">The first session of the epoch.</param>
/// <param name="FirstExit">First counted exit day.</param>
/// <param name="LastExit">Last counted exit day.</param>
/// <param name="SnapshotKey">The cell snapshot in force at the start.</param>
/// <param name="ReferenceAsOf">Its as-of date.</param>
/// <param name="RunKeys">The runs the reference was read from.</param>
/// <param name="Statement">The basis sentence.</param>
public sealed record CompareEvidenceView(DateOnly? EpochStart, DateOnly? FirstExit, DateOnly? LastExit, string? SnapshotKey, DateOnly? ReferenceAsOf, IReadOnlyList<string> RunKeys, string Statement);

/// <summary>The backtest-versus-paper report (spec P10-04, R14). Every figure is a state and a number only for a value or provisional figure.</summary>
/// <param name="EpochId">The epoch, or null before the first capture.</param>
/// <param name="N">Counted paper trades.</param>
/// <param name="Counts">What counts and what is set apart.</param>
/// <param name="Basis">net, gross, mixed or none.</param>
/// <param name="ChargeSheet">The sheet(s) and the confirmation.</param>
/// <param name="Paper">The paper side.</param>
/// <param name="Band">The band.</param>
/// <param name="Drawdown">The drawdown reference.</param>
/// <param name="Criteria">The verdicts.</param>
/// <param name="EarlyWarning">The warning sentence, or null.</param>
/// <param name="Cells">The cells.</param>
/// <param name="Fan">The fan; empty unless the band is a figure.</param>
/// <param name="SensitivityAllProposals">The plan-price sensitivity.</param>
/// <param name="Evidence">What it rests on.</param>
/// <param name="Limits">The honest limits.</param>
public sealed record BacktestVsPaperView(
    long? EpochId,
    int N,
    CompareCountsView Counts,
    string Basis,
    CompareChargeSheetView ChargeSheet,
    ComparePaperView Paper,
    CompareBandView Band,
    CompareDrawdownView Drawdown,
    CompareCriteriaView Criteria,
    string? EarlyWarning,
    IReadOnlyList<CompareCellView> Cells,
    IReadOnlyList<CompareFanPointView> Fan,
    CompareSensitivityView SensitivityAllProposals,
    CompareEvidenceView Evidence,
    IReadOnlyList<string> Limits);
