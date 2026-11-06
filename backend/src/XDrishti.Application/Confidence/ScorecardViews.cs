using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>One row of the 15-row edge checklist as the API sends it.</summary>
/// <param name="Number">1 to 15.</param>
/// <param name="Name">The criterion.</param>
/// <param name="Producer">The slice or source that produces the input.</param>
/// <param name="Status"><c>pass</c>, <c>fail</c> or <c>unavailable</c>. Unavailable is never a pass.</param>
/// <param name="Threshold">The go threshold in words and numbers.</param>
/// <param name="Value">The figure it was judged on, or why it could not be.</param>
public sealed record ScorecardRowView(int Number, string Name, string Producer, string Status, string Threshold, string Value);

/// <summary>One of the six sub-scores as the API sends it.</summary>
/// <param name="Name">The sub-score.</param>
/// <param name="State"><c>scored</c>, <c>not_applicable</c> or <c>unavailable</c> (a hard fail).</param>
/// <param name="Score">0 to 100 when scored, else null.</param>
/// <param name="Reading">The figure it was computed from, or why it could not be.</param>
/// <param name="WhatWouldMoveIt">The distance to the next anchor, computed from the anchors; null when already 100 or when it does not apply.</param>
public sealed record SubScoreView(string Name, string State, double? Score, string Reading, string? WhatWouldMoveIt);

/// <summary>One gate with what stops it.</summary>
/// <param name="Gate"><c>R</c> (Candidate to Paper) or <c>S</c> (Paper to Active).</param>
/// <param name="Passed">Whether it passes now.</param>
/// <param name="LabelOk">For gate S, whether the label is "Ready for the next phase"; true for gate R.</param>
/// <param name="Failing">The required rows that are not a pass.</param>
public sealed record ReadinessGateView(string Gate, bool Passed, bool LabelOk, IReadOnlyList<ScorecardRowView> Failing);

/// <summary>One stored daily snapshot of a strategy's score (the trend).</summary>
/// <param name="Id">The snapshot, which a gate decision can cite.</param>
/// <param name="Date">The India date.</param>
/// <param name="Score">The score then.</param>
/// <param name="Label">The label code then.</param>
/// <param name="SettingsHash">The settings hash it was computed with.</param>
public sealed record ScorecardPointView(long Id, DateOnly Date, double Score, string Label, string SettingsHash);

/// <summary>
/// The scorecard of one strategy version (spec CF-11 D1 to D4): the 15-row checklist, the six sub-scores, the hard fails, the label, the two gates and what would move the weakest link. Derived on every read from stored results; read only: it raises no size and places no order.
/// </summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Version">The strategy version the Lab results are read for; null when the strategy has none.</param>
/// <param name="Score">0 to 100: the minimum of the applicable sub-scores, or 0 with a hard fail.</param>
/// <param name="RawMinimum">The minimum before the hard fails.</param>
/// <param name="Label">The label code: <c>not_ready</c>, <c>building</c>, <c>usable</c> or <c>ready</c>.</param>
/// <param name="LabelText">The label in words.</param>
/// <param name="SubScores">The six sub-scores, in order.</param>
/// <param name="HardFails">Each hard fail by name.</param>
/// <param name="NotApplicable">What was left out because it does not apply.</param>
/// <param name="WeakestSubScore">The weakest sub-score by name; null when none.</param>
/// <param name="WhatWouldMoveIt">What would move the weakest sub-score; null when nothing is computed.</param>
/// <param name="Rows">The 15 checklist rows.</param>
/// <param name="RowsPassed">How many rows pass.</param>
/// <param name="GateR">Gate R.</param>
/// <param name="GateS">Gate S.</param>
/// <param name="Waiting">What the card is waiting for: the producers of the rows and sub-scores that are unavailable (unavailable is a fail, so this is the list of what to build or run).</param>
/// <param name="Notes">The honesty sentences: the basis of the figures and the interpretations.</param>
/// <param name="Enforce">Whether the owner's enforce switch is ON now.</param>
/// <param name="SettingsHash">The SHA-256 of the readiness settings.</param>
/// <param name="Settings">The text of those settings.</param>
/// <param name="History">The newest daily snapshots (the trend), newest first.</param>
public sealed record ScorecardView(
    string Strategy,
    int? Version,
    double Score,
    double RawMinimum,
    string Label,
    string LabelText,
    IReadOnlyList<SubScoreView> SubScores,
    IReadOnlyList<string> HardFails,
    IReadOnlyList<string> NotApplicable,
    string? WeakestSubScore,
    string? WhatWouldMoveIt,
    IReadOnlyList<ScorecardRowView> Rows,
    int RowsPassed,
    ReadinessGateView GateR,
    ReadinessGateView GateS,
    IReadOnlyList<string> Waiting,
    IReadOnlyList<string> Notes,
    bool Enforce,
    string SettingsHash,
    string Settings,
    IReadOnlyList<ScorecardPointView> History);

/// <summary>One line of the strategy table.</summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Version">The version read.</param>
/// <param name="Score">The score.</param>
/// <param name="Label">The label code.</param>
/// <param name="LabelText">The label in words.</param>
/// <param name="WeakestSubScore">The weakest sub-score by name.</param>
/// <param name="RowsPassed">Rows passing of 15.</param>
/// <param name="HardFails">The hard fails by name.</param>
/// <param name="GateR">Whether Gate R passes.</param>
/// <param name="GateS">Whether Gate S passes.</param>
public sealed record ScorecardSummaryView(string Strategy, int? Version, double Score, string Label, string LabelText, string? WeakestSubScore, int RowsPassed, IReadOnlyList<string> HardFails, bool GateR, bool GateS);

/// <summary>The system card (spec CF-11 D5).</summary>
/// <param name="Score">0 to 100.</param>
/// <param name="Label">The label code.</param>
/// <param name="LabelText">The label in words.</param>
/// <param name="Integrity">100 when the integrity checks are clear, else 0 (an unreadable check is 0).</param>
/// <param name="WeightedMean">The weighted mean of the strategy scores.</param>
/// <param name="EqualWeights">True when the weights are equal because no plan risk share is available (labelled).</param>
/// <param name="WeakestStrategy">The strategy with the lowest score, or null when there is none.</param>
/// <param name="WeakestSubScore">Its weakest sub-score, or null.</param>
/// <param name="Strategies">How many strategies were combined.</param>
/// <param name="Reason">Why it is 0 when the cause is not a strategy (integrity, no strategy), else null.</param>
public sealed record SystemScorecardView(double Score, string Label, string LabelText, double Integrity, double WeightedMean, bool EqualWeights, string? WeakestStrategy, string? WeakestSubScore, int Strategies, string? Reason);

/// <summary>The readiness settings now: the owner's switch and the thresholds beside it.</summary>
/// <param name="Enforce">Whether a failed gate refuses a lifecycle move (ON) or only warns (OFF, the default; no row means OFF).</param>
/// <param name="SwitchId">The row of the switch in force, or null when none was ever stored.</param>
/// <param name="SettingsHash">The SHA-256 of the thresholds.</param>
/// <param name="Settings">The text of the thresholds.</param>
public sealed record ReadinessSettingsView(bool Enforce, long? SwitchId, string SettingsHash, string Settings);

/// <summary>The system card and the strategy table (spec CF-11).</summary>
/// <param name="System">The system card.</param>
/// <param name="Strategies">One line per strategy, weakest first.</param>
/// <param name="Settings">The settings now.</param>
/// <param name="Statements">The honesty sentences.</param>
public sealed record ScorecardListView(SystemScorecardView System, IReadOnlyList<ScorecardSummaryView> Strategies, ReadinessSettingsView Settings, IReadOnlyList<string> Statements);

/// <summary>The scorecard of one strategy version. Read only.</summary>
/// <param name="Strategy">The strategy id.</param>
/// <param name="Version">The version; 404 <c>confidence.scorecard_not_found</c> when the strategy or the version does not exist.</param>
public sealed record GetScorecardQuery(string Strategy, int Version) : IQuery<Result<ScorecardView>>;

/// <summary>The system card and the table of every strategy's scorecard (its latest version). Read only.</summary>
public sealed record ListScorecardsQuery : IQuery<Result<ScorecardListView>>;

/// <summary>The readiness settings now. Read only.</summary>
public sealed record GetReadinessSettingsQuery : IQuery<ReadinessSettingsView>;

/// <summary>
/// Switches <c>confidence.readiness.enforce</c> ON or OFF as the owner (spec CF-11 D6, ADR 0221): the actor must be <c>owner:...</c>, from the authenticated identity after a fresh password, ON or OFF (the endpoint), never from a body. Every call appends a row; none is
/// edited. ON makes a failed gate refuse the lifecycle move it judges; it moves nothing itself and places no order. No row means OFF.
/// </summary>
/// <param name="Actor">The owner, <c>owner:...</c>.</param>
/// <param name="Enforce">The new value.</param>
/// <param name="Reason">Why, 1 to 500 characters.</param>
public sealed record ChangeReadinessSwitchCommand(string Actor, bool Enforce, string Reason) : ICommand;

/// <summary>The answer of a switch change: the row and the new value.</summary>
/// <param name="Id">The row appended.</param>
/// <param name="Enforce">The value now in force.</param>
public sealed record ReadinessSwitched(long Id, bool Enforce);

/// <summary>The daily run: store one snapshot per strategy and one for the system (idempotent per day).</summary>
public sealed record SnapshotScorecardsCommand : ICommand;

/// <summary>What the daily run did.</summary>
/// <param name="Strategies">Strategies found.</param>
/// <param name="Stored">Snapshots stored (strategies plus the system).</param>
/// <param name="AlreadyDone">Snapshots that existed for today already.</param>
/// <param name="Failed">Strategies whose scorecard could not be built.</param>
public sealed record SnapshotRunOutcome(int Strategies, int Stored, int AlreadyDone, int Failed)
{
    /// <summary>One line for the service log.</summary>
    public string Summary => $"{Strategies} strategies, {Stored} snapshot(s) stored, {AlreadyDone} already done today, {Failed} failed";
}
