namespace XDrishti.Application.Confidence;

/// <summary>One completed window of the decay monitor.</summary>
/// <param name="Index">1-based.</param>
/// <param name="Trades">Trades in the window.</param>
/// <param name="FirstDay">First trade day.</param>
/// <param name="LastDay">Last trade day.</param>
/// <param name="MeanR">The window's mean R.</param>
/// <param name="DesignEffect">The mean design effect of its days (CF-01; the correlation is the placeholder until CF-01 estimates it).</param>
/// <param name="Threshold">The alert level for this window.</param>
/// <param name="Below">Whether the mean is below the level (an alert).</param>
public sealed record DecayWindowView(int Index, int Trades, DateOnly FirstDay, DateOnly LastDay, double MeanR, double DesignEffect, double Threshold, bool Below);

/// <summary>One calendar quarter of the by-quarter table.</summary>
/// <param name="Label">For example <c>2026-Q4</c>.</param>
/// <param name="Trades">Trades in the quarter.</param>
/// <param name="MeanR">Their mean R.</param>
/// <param name="Weak">Whether the mean is below the quarter floor.</param>
public sealed record DecayQuarterView(string Label, int Trades, double MeanR, bool Weak);

/// <summary>The P6-09 CUSUM on the same series, against the same reference.</summary>
/// <param name="Alarm">Whether the last CUSUM event is an alarm.</param>
/// <param name="Events">How many alarms and clears the series has had.</param>
/// <param name="Latest">The latest event's evidence sentence, or null.</param>
public sealed record DecayCusumView(bool Alarm, int Events, string? Latest);

/// <summary>The reference a series is judged against.</summary>
/// <param name="State"><c>available</c> or <c>unavailable</c> (a series with no reference is never judged).</param>
/// <param name="Reason">Why it is unavailable, or null.</param>
/// <param name="Trades">Backtest trades the reference is made of (N_ref).</param>
/// <param name="MeanR">mu_ref.</param>
/// <param name="Sigma">sigma, the sample standard deviation of the reference.</param>
/// <param name="Basis"><c>net</c> or <c>gross</c> of costs, as the backtest was run.</param>
public sealed record DecayReferenceView(string State, string? Reason, int? Trades, double? MeanR, double? Sigma, string? Basis);

/// <summary>One kill rule's evaluation, with its numbers. An evaluation only: nothing is proposed, stored or executed in this part.</summary>
/// <param name="Rule"><c>cost_stress</c>, <c>drawdown</c>, <c>decay_twice</c> or <c>failure_boundary</c>.</param>
/// <param name="State"><c>not_evaluated</c>, <c>holds</c>, <c>fires</c> or <c>held</c>. <c>not_evaluated</c> is not a pass.</param>
/// <param name="Value">The figure judged, or null.</param>
/// <param name="Limit">What it was judged against, or null.</param>
/// <param name="Trades">Trades the figure is based on.</param>
/// <param name="ProposalOnly">True for the CF-09 failure boundary.</param>
/// <param name="Text">The words, with the numbers.</param>
public sealed record DecayKillView(string Rule, string State, double? Value, double? Limit, int Trades, bool ProposalOnly, string Text);

/// <summary>One series run through the decay monitor.</summary>
/// <param name="Basis">What the R is.</param>
/// <param name="Note">What the reader should know (no series, held back sessions, trades left out); null when nothing.</param>
/// <param name="State"><c>no_series</c>, <c>unavailable</c> (no reference), <c>monitoring</c> (no completed window yet), <c>ok</c> or <c>alert</c> (the latest completed window is below its level). A flag; never a pass.</param>
/// <param name="Trades">Trades in the series.</param>
/// <param name="PendingTrades">Trades after the last completed window.</param>
/// <param name="Streak">Completed windows below their level in a row at the end.</param>
/// <param name="TwiceInARow">Whether the last two completed windows are both below.</param>
/// <param name="FirstTwiceAtWindow">The window at which two in a row was first reached, or null.</param>
/// <param name="Windows">The completed windows.</param>
/// <param name="Cusum">The P6-09 CUSUM; null when the series is not judged.</param>
/// <param name="Quarters">The by-quarter table.</param>
/// <param name="WeakQuarterShare">The share of quarters below the floor, or null.</param>
public sealed record DecaySeriesView(
    string Basis,
    string? Note,
    string State,
    int Trades,
    int PendingTrades,
    int Streak,
    bool TwiceInARow,
    int? FirstTwiceAtWindow,
    IReadOnlyList<DecayWindowView> Windows,
    DecayCusumView? Cusum,
    IReadOnlyList<DecayQuarterView> Quarters,
    double? WeakQuarterShare);

/// <summary>The settings that produced the figures.</summary>
/// <param name="WindowTrades">Window size.</param>
/// <param name="Sigmas">Standard errors below the reference mean.</param>
/// <param name="QuarterFloorR">The weak-quarter floor.</param>
/// <param name="SettingsHash">The SHA-256 of the settings.</param>
/// <param name="Settings">The text of the settings.</param>
/// <param name="Rho">The same-day correlation used.</param>
/// <param name="RhoIsPlaceholder">Whether it is the placeholder until CF-01 estimates it.</param>
public sealed record DecaySettingsView(int WindowTrades, double Sigmas, double QuarterFloorR, string SettingsHash, string Settings, double Rho, bool RhoIsPlaceholder);

/// <summary>
/// The decay monitor of a strategy (spec CF-12 D1 and the kill-rule evaluations of D2, part 12a). The edge series (graded shadow trades that count for edge) is the primary reading and the P10 behavioural series sits beside it. Report only: no proposal is
/// written, nothing is paused and no order exists.
/// </summary>
/// <param name="Strategy">The strategy.</param>
/// <param name="Epoch">The paper epoch both series are read for; null when there is none.</param>
/// <param name="HasSeries">Whether the strategy has any shadow edge candidate or any counted paper trade in the epoch (the endpoint answers 404 when it has neither).</param>
/// <param name="Settings">The settings.</param>
/// <param name="Reference">The reference, as the edge series sees it.</param>
/// <param name="Edge">The edge series.</param>
/// <param name="Behavioural">The behavioural paper series, beside it.</param>
/// <param name="Kill">The kill rules evaluated on the edge series, proposals only (none is stored or executed).</param>
/// <param name="Statements">The honesty sentences.</param>
/// <param name="Line">One line for a report.</param>
public sealed record DecayReportView(
    string Strategy,
    long? Epoch,
    bool HasSeries,
    DecaySettingsView Settings,
    DecayReferenceView Reference,
    DecaySeriesView Edge,
    DecaySeriesView Behavioural,
    IReadOnlyList<DecayKillView> Kill,
    IReadOnlyList<string> Statements,
    string Line);
