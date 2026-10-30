using XDrishti.Domain.Execution;
using XDrishti.Domain.Execution.Limits;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Domain.Limits;

/// <summary>One place where an always-ask item is enforced, with a pure probe that proves the rule holds in this build.</summary>
public sealed record EnforcementPoint(string Where, Func<bool> Holds);

/// <summary>One item of doc 19 §4's always-ask list and the points that keep it out of automation.</summary>
public sealed record AlwaysAskItem(string Code, string Meaning, IReadOnlyList<EnforcementPoint> Points);

/// <summary>
/// The always-ask list (spec P12-09 criterion 8, doc 19 §4): what software never does on its own, each mapped to where it is enforced. A test fails if an item has no point or a probe does not hold. The probes are pure calls of the rules
/// themselves, so a rule that is loosened later breaks the list.
/// </summary>
public static class AlwaysAskList
{
    public const string RiskIncrease = "risk_increase";
    public const string NewStrategyLive = "new_strategy_live";
    public const string EnableExecution = "enable_execution";
    public const string AccountChanges = "account_changes";

    private static readonly ActionClass[] RiskAdding = [ActionClass.StopLoosen, ActionClass.AddToPosition, ActionClass.ReEntry];

    public static readonly IReadOnlyList<AlwaysAskItem> Items =
    [
        new(RiskIncrease, "Loosening a stop, adding to a position, re-entering, or raising any cap.",
        [
            new("ModeRouting: StopLoosen, AddToPosition and ReEntry are refused in every mode until P12-13", () =>
                Enum.GetValues<ExecMode>().Cast<ExecMode?>().Append(null).All(m => RiskAdding.All(a => ModeRouting.Decide(m, a, ExecSide.Buy).Kind == RouteKind.Refused))),
            new("LimitChange: any loosening of the limits sheet is a raise (password, effective from the next session after a full closed one)", () =>
                LimitChange.Classify(ExecutionLimits.Empty, ExecutionLimits.Empty with { MaxOrdersPerDay = 1 }).IsRaise
                && LimitChange.Classify(ExecutionLimits.Empty with { ExitAllOnLossHalt = true }, ExecutionLimits.Empty).IsRaise),
            new("ExecSettingRegistry: every auto_live switch has a riskier direction (owner act with password and cooling-off, P12-07)", () =>
                new[] { AutoLiveSettings.AllowAutoApprovedKey, AutoLiveSettings.RequireDayUnlockKey, AutoLiveSettings.RequireAttendedKey, AutoLiveSettings.AttendedMinutesKey }
                    .All(k => ExecSettingRegistry.Find(k)?.Risk is ExecRiskDirection.HigherIsRiskier or ExecRiskDirection.LowerIsRiskier)),
        ]),
        new(NewStrategyLive, "The first live entry of a strategy version for an account.",
        [
            new("AutoLiveGuard: strategy.first_use refuses the automatic entry and routes it to a ticket", () =>
                AutoLiveGuard.Evaluate(ExecMode.AutoLive, AutoLiveSettings.None with { ExecutionEnabled = true, AutoLiveEnabled = true }, Probe, Facts(strategyUsed: false), DateTimeOffset.UnixEpoch).Codes.Contains(AutoLiveCodes.StrategyFirstUse)
                && new GuardVerdict([AutoLiveCodes.StrategyFirstUse], false).RouteToTicket),
        ]),
        new(EnableExecution, "Switching execution or auto_live on, or enabling a mode.",
        [
            new("AutoLiveSettings: no settings row is OFF and an OFF switch is mode.not_auto with nothing written", () =>
                !AutoLiveSettings.None.AutoLiveEnabled && !AutoLiveSettings.None.ExecutionEnabled
                && AutoLiveGuard.Evaluate(ExecMode.AutoLive, AutoLiveSettings.None, Probe, Facts(strategyUsed: true), DateTimeOffset.UnixEpoch).Silent),
            new("ExecutionModes (ADR 0163): no parser accepts semi_auto or auto_live, so no account can be promoted to them", () =>
                ExecutionModes.TryParse("semi_auto") is null && ExecutionModes.TryParse("auto_live") is null),
        ]),
        new(AccountChanges, "A client-id change, a role removal or an archive.",
        [
            new("DemotionPolicy (P12-07): an archived account or one whose Trading role is off goes to paper", () =>
                DemotionPolicy.Evaluate(LadderMode.Assisted, new DemotionFacts(false, false))?.Target == LadderMode.Paper
                && DemotionPolicy.Evaluate(LadderMode.Assisted, new DemotionFacts(true, true))?.Target == LadderMode.Paper),
        ]),
    ];

    private static AutoEntry Probe => new(Guid.Empty, Guid.Empty, 0, "NSE:PROBE", "h", LimitsCeilings.EquityIntraday, false, ExecSide.Buy, 1, 1, 1m, 0.5m, 2m, "owner", null, DateTimeOffset.UnixEpoch);

    private static AutoFacts Facts(bool strategyUsed) => new(null, null, new EntryFacts(false, [], true, true, false, false, true, false, 0, 0m, 0m), strategyUsed, null, null);
}
