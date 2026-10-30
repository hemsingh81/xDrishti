using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Application.Halts;

/// <summary>How an automatic clear is earned: a quiet period of good state, a number of consecutive good reads, or the readiness fact of P12-11.</summary>
public enum AutoClearKind
{
    QuietSeconds,
    GoodReads,
    ReadyFact,
}

public sealed record AutoClearRule(AutoClearKind Kind, int Amount);

/// <summary>One trigger code: the level it asks for by default, the scope it normally has and how it ends (null rule: only the owner ends it).</summary>
public sealed record HaltTrigger(string Code, HaltLevel DefaultLevel, HaltScope DefaultScope, AutoClearRule? AutoClear);

/// <summary>The 18 trigger codes of P12-05 (the owner's button and the 17 automatic ones) and the drill. A code outside this list cannot be a halt: the store and the database both refuse it.</summary>
public static class HaltTriggers
{
    public const string OwnerButton = "owner_button";
    public const string DailyLoss = "daily_loss";
    public const string LossCap = "loss_cap";
    public const string Drawdown = "drawdown";
    public const string DisasterLoss = "disaster_loss";
    public const string FeedLoss = "feed_loss";
    public const string ReconciliationBreak = "reconciliation_break";
    public const string ProtectionBreach = "protection_breach";
    public const string FlattenIncomplete = "flatten_incomplete";
    public const string Oversold = "oversold";
    public const string ProtectionUnknown = "protection_unknown";
    public const string RunawayRate = "runaway_rate";
    public const string TokenInvalid = "token_invalid";
    public const string IpNotAllowed = "ip_not_allowed";
    public const string CrashLoop = "crash_loop";
    public const string ClockSkew = "clock_skew";
    public const string ExchangeHalt = "exchange_halt";
    public const string BrokerOutage = "broker_outage";
    public const string Drill = "drill";

    public static readonly IReadOnlyList<HaltTrigger> All =
    [
        new(OwnerButton, HaltLevel.Cancel, HaltScope.Account, null),
        new(DailyLoss, HaltLevel.Entries, HaltScope.Account, null),
        new(LossCap, HaltLevel.Entries, HaltScope.Account, null),
        new(Drawdown, HaltLevel.Entries, HaltScope.Account, null),
        new(DisasterLoss, HaltLevel.Cancel, HaltScope.Account, null),
        new(FeedLoss, HaltLevel.Entries, HaltScope.System, new(AutoClearKind.QuietSeconds, 60)),
        new(ReconciliationBreak, HaltLevel.Freeze, HaltScope.Account, null),
        new(ProtectionBreach, HaltLevel.Entries, HaltScope.Account, null),
        new(FlattenIncomplete, HaltLevel.Entries, HaltScope.Account, null),
        new(Oversold, HaltLevel.Freeze, HaltScope.Account, null),
        new(ProtectionUnknown, HaltLevel.Entries, HaltScope.Account, new(AutoClearKind.QuietSeconds, 60)),
        new(RunawayRate, HaltLevel.Cancel, HaltScope.System, null),
        new(TokenInvalid, HaltLevel.Entries, HaltScope.Account, new(AutoClearKind.ReadyFact, 0)),
        new(IpNotAllowed, HaltLevel.Entries, HaltScope.Account, new(AutoClearKind.ReadyFact, 0)),
        new(CrashLoop, HaltLevel.Cancel, HaltScope.System, null),
        new(ClockSkew, HaltLevel.Entries, HaltScope.System, new(AutoClearKind.GoodReads, 5)),
        new(ExchangeHalt, HaltLevel.Entries, HaltScope.System, null),
        new(BrokerOutage, HaltLevel.Entries, HaltScope.System, new(AutoClearKind.GoodReads, 3)),
        new(Drill, HaltLevel.Cancel, HaltScope.Account, new(AutoClearKind.QuietSeconds, 0)),
    ];

    public static HaltTrigger? Find(string code) => All.FirstOrDefault(t => t.Code == code);

    /// <summary>True for the codes that may end without the owner; the broker's kill switch is never activated for one of them.</summary>
    public static bool IsAutoClearing(string code) => Find(code)?.AutoClear is not null;
}
