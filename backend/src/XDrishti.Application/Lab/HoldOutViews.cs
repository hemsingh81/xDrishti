namespace XDrishti.Application.Lab;

/// <summary>
/// What the one look at the hold-out said, as served and as stored (spec CF-06, D5). <c>State</c> is <c>used</c> (the first look at the block) or <c>reused</c> (a later look: the numbers are shown, the result counts as a trial and cannot count toward a pass).
/// <c>Mode</c> is <c>owner</c> or <c>auto</c> (the auto option, recorded as auto and never as an owner approval). <c>ReusedReason</c> is <c>not_predated</c> when the look was stored re-used because its subject was not frozen before the block's horizon (HO-01 D5). <c>RunSawHoldOut</c> is true when the run the plan came from had a window reaching into the block: such a look is never a pass.
/// </summary>
public sealed record HoldOutResultView(
    string StrategyId,
    int Version,
    string Kind,
    string Mode,
    string State,
    DateTimeOffset UnlockedAt,
    DateOnly From,
    DateOnly To,
    bool ReusedWindow,
    bool RunSawHoldOut,
    bool Passed,
    string Status,
    string Reason,
    double? DevelopmentExpectancyR,
    double? HoldOutExpectancyR,
    double? Ratio,
    int Trades,
    string? PlanHash,
    string SettingHash,
    string Actor,
    IReadOnlyList<string> Notes,
    string? ReusedReason = null);

/// <summary>
/// A block of the hold-out chain as served (HO-01): <c>HistoryLast</c> is its last date. <c>State</c> is <c>filling</c>, <c>sealed</c>, <c>looked</c> or <c>released</c>; <c>Looks</c> holds the three looks (strategy, model, system); <c>BlockedBy</c> names what keeps the block in force from being released by the auto option (set for the block in force only);
/// <c>ReleasedAt</c> and <c>Mode</c> (<c>owner</c> or <c>auto</c>) are set once it is released.
/// </summary>
public sealed record HoldOutBlockView(DateOnly Start, DateOnly HistoryFirst, DateOnly HistoryLast, int LengthMonths, string SettingHash, DateTimeOffset CreatedAt, string? State = null, HoldOutLooksView? Looks = null, DateTimeOffset? ReleasedAt = null, string? Mode = null, string? BlockedBy = null);

/// <summary>The three looks of a block (HO-01 D5): one per kind.</summary>
public sealed record HoldOutLooksView(HoldOutLookView Strategy, HoldOutLookView Model, HoldOutLookView System);

/// <summary>
/// One kind's look at a block: <c>State</c> is <c>fresh</c> (the slot is taken, with its subject, verdict and mode), <c>available</c> (free, an eligible subject exists), <c>no_subject</c> (free, none exists or the kind's path is not built), <c>unavailable</c> (the read failed)
/// or <c>none</c> (not read: the block is released without a look of this kind, or not in force).
/// </summary>
public sealed record HoldOutLookView(string State, string? Subject = null, bool? Passed = null, string? Status = null, DateTimeOffset? At = null, string? Mode = null);

/// <summary>The sizing the policy gives for the stored history now (D4).</summary>
public sealed record HoldOutPlanView(double HistoryMonths, int LengthMonths, double DevelopmentMonths, int Folds, DateOnly? Start, string Status);

/// <summary><c>GET /api/lab/holdout</c>: the block in force (or what it would be), whether the Lab enforces it, the auto options, the chain of <c>Blocks</c> with their states, and every look taken. Numbers appear only in an unlock that was taken.</summary>
public sealed record HoldOutView(bool Enforce, bool AutoUnlock, HoldOutBlockView? Block, HoldOutPlanView? Proposed, IReadOnlyList<HoldOutResultView> Unlocks, IReadOnlyList<HoldOutBlockView>? Blocks = null, bool AutoRelease = true);

/// <summary>The registration row of a report (spec CF-06, AC12): <c>registered</c> or <c>not_registered</c>, and the texts when registered.</summary>
public sealed record ReportRegistrationView(string State, bool Enforce, RegistrationView? Registration, string Text);

/// <summary>The hold-out row of a report (spec CF-06, AC12): <c>not_fixed</c>, <c>kept</c>, <c>used</c> or <c>reused</c>; the numbers only after the look was taken.</summary>
public sealed record ReportHoldOutView(string State, bool Enforce, DateOnly? Start, int? LengthMonths, bool RunReachesHoldOut, string Text, HoldOutResultView? Result);
