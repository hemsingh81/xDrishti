using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Shadow;

/// <summary>The sequential failure test of the paper edge series (spec CF-09, ADR 0213). Read only: it derives the answer from stored grades and the settings and changes nothing.</summary>
/// <param name="Scope"><c>system</c> (every edge trade) or <c>strategy</c> (the trades of one strategy, named in <paramref name="Strategy"/>).</param>
/// <param name="Strategy">The strategy id of a <c>strategy</c> scope; null for <c>system</c>.</param>
public sealed record GetSequentialStatusQuery(string Scope, string? Strategy) : IQuery<Result<SequentialStatusView>>
{
    /// <summary>Every edge trade.</summary>
    public const string SystemScope = "system";

    /// <summary>The edge trades of one strategy.</summary>
    public const string StrategyScope = "strategy";
}

/// <summary>The boundaries of the test, computed from the settings (never stored).</summary>
/// <param name="Caution">The caution boundary.</param>
/// <param name="Failure">The failure boundary.</param>
/// <param name="OnTrack">The level the sum is reflected at; a label, never a pass.</param>
/// <param name="CautionOdds">The evidence ratio at the caution boundary.</param>
/// <param name="FailureOdds">The evidence ratio at the failure boundary.</param>
public sealed record SequentialBoundariesView(double Caution, double Failure, double OnTrack, double CautionOdds, double FailureOdds);

/// <summary>A short summary of the sum path (the whole path is not sent).</summary>
/// <param name="Lowest">The lowest sum of the path.</param>
/// <param name="LowestAtTrade">The 1-based trade of the lowest sum; 0 with no trade.</param>
/// <param name="SigmaUsed">The sigma of the last step.</param>
public sealed record SequentialPathView(double Lowest, int LowestAtTrade, double SigmaUsed);

/// <summary>One series run through the test.</summary>
/// <param name="Basis">What the R is: gross or net, and whose fills.</param>
/// <param name="Note">What the reader should know about the series: why there is nothing to test (no epoch, no graded trade), which session holds trades back, how many graded trades have no gross figure; null when there is nothing to say.</param>
/// <param name="State"><c>monitoring</c>, <c>on_track</c>, <c>caution</c> or <c>failure</c>. None of them is a pass.</param>
/// <param name="StateText">The words of the state.</param>
/// <param name="Sum">The running sum now.</param>
/// <param name="TradesCounted">Trades in the series.</param>
/// <param name="EffectiveN">The trades over the design effect of their days.</param>
/// <param name="FirstCautionTrade">The 1-based number of the first trade at or below the caution boundary; null when never.</param>
/// <param name="FirstCautionDate">Its day; null when never.</param>
/// <param name="FirstFailureTrade">The 1-based number of the first trade at or below the failure boundary; null when never.</param>
/// <param name="FirstFailureDate">Its day; null when never.</param>
/// <param name="HeldBackTrades">Graded trades not in the series because an earlier session still has a candidate without a grade (the test runs only over the complete sessions from the start of the epoch, so a late grade can only add to the end and never rewrite a shown path); 0 for the behavioural series.</param>
/// <param name="HeldBackFromSession">The first session with a candidate still without a grade, where the series stops (it may never be graded: a replay can wait or fail for good); null when the series is complete.</param>
/// <param name="Path">The path summary.</param>
public sealed record SequentialSeriesView(
    string Basis,
    string? Note,
    string State,
    string StateText,
    double Sum,
    int TradesCounted,
    double EffectiveN,
    int? FirstCautionTrade,
    DateOnly? FirstCautionDate,
    int? FirstFailureTrade,
    DateOnly? FirstFailureDate,
    int HeldBackTrades,
    DateOnly? HeldBackFromSession,
    SequentialPathView Path);

/// <summary>The sequential failure test (spec CF-09): the edge series of the shadow record with the P10 behavioural series beside it. A flag only.</summary>
/// <param name="Scope"><c>system</c> or <c>strategy</c>.</param>
/// <param name="Strategy">The strategy of a <c>strategy</c> scope.</param>
/// <param name="Epoch">The paper epoch both series are read for (the current one); null when there is none.</param>
/// <param name="HasSeries">Whether any shadow edge candidate of the epoch exists in the scope (the endpoint answers 404 when not).</param>
/// <param name="Boundaries">The boundaries.</param>
/// <param name="Rho">The same-day correlation used.</param>
/// <param name="RhoIsPlaceholder">Whether it is the placeholder until CF-01 estimates it.</param>
/// <param name="SettingsHash">The SHA-256 of the settings that produced the figures.</param>
/// <param name="Settings">The text of those settings.</param>
/// <param name="Edge">The edge series (graded shadow trades that count for edge); the primary reading.</param>
/// <param name="Behavioural">The P10 counted trades of the epoch, run through the same test, beside it.</param>
/// <param name="Statements">The honesty sentences: the model, the odds, the indifference point, the same-day discount, that the fixed gates decide.</param>
/// <param name="Line">One line for the report beside the P10 phase check.</param>
public sealed record SequentialStatusView(
    string Scope,
    string? Strategy,
    long? Epoch,
    bool HasSeries,
    SequentialBoundariesView Boundaries,
    double Rho,
    bool RhoIsPlaceholder,
    string SettingsHash,
    string Settings,
    SequentialSeriesView Edge,
    SequentialSeriesView Behavioural,
    IReadOnlyList<string> Statements,
    string Line);
