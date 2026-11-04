namespace XDrishti.Application.Lab;

/// <summary>
/// What the one look at the hold-out said, as served and as stored (spec CF-06, D5). <c>State</c> is <c>used</c> (the first look at the block) or <c>reused</c> (a later look: the numbers are shown, the result counts as a trial and cannot count toward a pass).
/// <c>Mode</c> is <c>owner</c> or <c>auto</c> (the auto option, recorded as auto and never as an owner approval). <c>RunSawHoldOut</c> is true when the run the plan came from had a window reaching into the block: such a look is never a pass.
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
    IReadOnlyList<string> Notes);

/// <summary>The locked block as served.</summary>
public sealed record HoldOutBlockView(DateOnly Start, DateOnly HistoryFirst, DateOnly HistoryLast, int LengthMonths, string SettingHash, DateTimeOffset CreatedAt);

/// <summary>The sizing the policy gives for the stored history now (D4).</summary>
public sealed record HoldOutPlanView(double HistoryMonths, int LengthMonths, double DevelopmentMonths, int Folds, DateOnly? Start, string Status);

/// <summary><c>GET /api/lab/holdout</c>: the block (or what it would be), whether the Lab enforces it, and every look taken. Numbers appear only in an unlock that was taken.</summary>
public sealed record HoldOutView(bool Enforce, bool AutoUnlock, HoldOutBlockView? Block, HoldOutPlanView? Proposed, IReadOnlyList<HoldOutResultView> Unlocks);

/// <summary>The registration row of a report (spec CF-06, AC12): <c>registered</c> or <c>not_registered</c>, and the texts when registered.</summary>
public sealed record ReportRegistrationView(string State, bool Enforce, RegistrationView? Registration, string Text);

/// <summary>The hold-out row of a report (spec CF-06, AC12): <c>not_fixed</c>, <c>kept</c>, <c>used</c> or <c>reused</c>; the numbers only after the look was taken.</summary>
public sealed record ReportHoldOutView(string State, bool Enforce, DateOnly? Start, int? LengthMonths, bool RunReachesHoldOut, string Text, HoldOutResultView? Result);
