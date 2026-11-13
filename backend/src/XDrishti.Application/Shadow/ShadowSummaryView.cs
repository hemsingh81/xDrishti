using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;

namespace XDrishti.Application.Shadow;

/// <summary>The summary of the shadow record (spec CF-08 part B, ADR 0212). Read only; no parameters.</summary>
public sealed record GetShadowSummaryQuery : IQuery<ShadowSummaryView>;

/// <summary>The two counts side by side (criterion 10). The behavioural count is P10's own: the owner-approved, armed, primary, measured trades of the newest epoch. The edge count is the graded shadow trades that count for edge. They are never added.</summary>
/// <param name="Behavioural">The P10 counted trades of the newest epoch (the exit criterion's n).</param>
/// <param name="EdgeRaw">Graded shadow trades that count for edge.</param>
/// <param name="EdgeEffective">The effective n of the edge count.</param>
/// <param name="Candidates">Every stored candidate, set-aside ones included.</param>
/// <param name="EdgeCandidates">Candidates that count for edge.</param>
/// <param name="Pending">Edge candidates with no stored grade yet.</param>
/// <param name="NotTriggered">Edge candidates graded as not triggered.</param>
/// <param name="NotGradable">Edge candidates graded as not gradable.</param>
/// <param name="OutsideSet">Candidates that would count for edge but whose strategy version is outside the epoch's frozen evidence set (spec AM-01 GAP-09): written and graded, labelled <c>outside_set</c>, left out of every edge figure.</param>
public sealed record ShadowCountsView(int Behavioural, int EdgeRaw, double EdgeEffective, int Candidates, int EdgeCandidates, int Pending, int NotTriggered, int NotGradable, int OutsideSet = 0);

/// <summary>The effective n of the edge count and the pace (criteria 8 and 9).</summary>
/// <param name="RawN">Graded shadow trades that count for edge.</param>
/// <param name="EffectiveN">Raw n over the design effect of each day.</param>
/// <param name="Rho">The same-day correlation used.</param>
/// <param name="RhoIsPlaceholder">Whether it is the placeholder until CF-01 estimates it.</param>
/// <param name="RhoNote">Says so when it is the placeholder, else null.</param>
/// <param name="Sessions">Sessions with at least one final grade: the sessions the pace is counted over (a session not graded yet does not lower the rate).</param>
/// <param name="TargetEffectiveN">The effective n the ETA aims at (60, as the paper programme).</param>
/// <param name="EtaSessions">Sessions still needed at the rate seen so far; null until 10 trades are graded.</param>
/// <param name="EtaNote">What the ETA is or why there is none.</param>
public sealed record ShadowEdgeView(int RawN, double EffectiveN, double Rho, bool RhoIsPlaceholder, string? RhoNote, int Sessions, int TargetEffectiveN, int? EtaSessions, string EtaNote);

/// <summary>The graded edge trades of one group: n, mean R with a 90% interval and the hit rate. A figure is a state and a number only when it is a value.</summary>
/// <param name="Group"><c>all</c>, <c>primary</c>, <c>reserve</c> or <c>rejected</c>.</param>
/// <param name="N">Graded trades.</param>
/// <param name="MeanR">The mean R.</param>
/// <param name="Low90">The 5th percentile of the bootstrapped mean.</param>
/// <param name="High90">The 95th percentile of the bootstrapped mean.</param>
/// <param name="HitRate">The share of trades with R above 0.</param>
public sealed record ShadowGroupView(string Group, int N, MetricView MeanR, MetricView Low90, MetricView High90, MetricView HitRate);

/// <summary>What the R is net of, and whether it may be called net (P10-00 D5).</summary>
/// <param name="Basis">The label of the figures.</param>
/// <param name="Gross">True when the figures are the gross R (before charges): no charge sheet, a sheet the owner has not confirmed, or a mix. The numbers, this flag and <paramref name="Basis"/> always agree.</param>
/// <param name="SheetIds">The charge sheets that charged any graded trade.</param>
/// <param name="Confirmed">Whether every such sheet is confirmed as the owner's.</param>
public sealed record ShadowCostView(string Basis, bool Gross, IReadOnlyList<string> SheetIds, bool Confirmed);

/// <summary>The chain of digests: the owner copies the last one outside the database, and <c>xd paper shadow verify</c> recomputes it.</summary>
/// <param name="Sessions">Stored sessions.</param>
/// <param name="Gaps">Gap sessions: the writer did not run before the open; never back-filled.</param>
/// <param name="LastSession">The newest session.</param>
/// <param name="LastDigest">Its digest.</param>
public sealed record ShadowChainView(int Sessions, int Gaps, DateOnly? LastSession, string? LastDigest);

/// <summary>The sequential failure test beside the shadow figures (spec CF-09 D7, ADR 0232): the same answer as <c>GET /api/paper/sequential</c> for the system scope, as a state and one line. A flag only, never a pass.</summary>
/// <param name="Available">Whether a shadow edge series exists in the current paper epoch (false: the other fields are null and <paramref name="Note"/> says why).</param>
/// <param name="Note">Why there is no test yet; null when available.</param>
/// <param name="EdgeState"><c>monitoring</c>, <c>on_track</c>, <c>caution</c> or <c>failure</c> of the edge series; null when not available.</param>
/// <param name="BehaviouralState">The same for the P10 behavioural series beside it; null when not available.</param>
/// <param name="Line">The one report line (never a pass; the fixed P10 gates decide); null when not available.</param>
public sealed record ShadowSequentialView(bool Available, string? Note, string? EdgeState, string? BehaviouralState, string? Line);

/// <summary>The shadow summary (criterion 10).</summary>
/// <param name="Enabled">Whether <c>paper.shadow.enabled</c> is ON.</param>
/// <param name="HasRows">Whether any shadow row is stored.</param>
/// <param name="Note">Says so when there are no rows (and why none is written while the switch is OFF); null otherwise.</param>
/// <param name="Statement">Always: modelled fills; no queue, no latency.</param>
/// <param name="SeparateCount">Always: this is a separate count and the P10 exit criterion is untouched.</param>
/// <param name="Counts">The behavioural and edge counts side by side.</param>
/// <param name="Edge">The effective n and the pace.</param>
/// <param name="Groups">Overall (<c>all</c>), then primary, reserve and rejected.</param>
/// <param name="Costs">What the R is net of.</param>
/// <param name="Chain">The chain head.</param>
/// <param name="Sequential">The sequential failure test line (CF-09).</param>
public sealed record ShadowSummaryView(
    bool Enabled,
    bool HasRows,
    string? Note,
    string Statement,
    string SeparateCount,
    ShadowCountsView Counts,
    ShadowEdgeView Edge,
    IReadOnlyList<ShadowGroupView> Groups,
    ShadowCostView Costs,
    ShadowChainView Chain,
    ShadowSequentialView Sequential);
