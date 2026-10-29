using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Execution.Domain.Checks;

public enum CheckClass
{
    Hard,
    Soft,
}

/// <summary><see cref="Unavailable"/> is a non-pass: nothing in this project turns it into a pass.</summary>
public enum CheckOutcome
{
    Pass,
    Fail,
    Unavailable,
}

public enum CheckRunKind
{
    /// <summary>Run at <c>checking</c>; the only kind that authorises a send.</summary>
    Full,

    /// <summary>The re-run of the time-critical checks just before an entry send.</summary>
    Fast,

    /// <summary>Every check except <c>gate_open</c>, for an assisted sheet; never authorises anything.</summary>
    Advisory,
}

public enum CheckVerdict
{
    Cleared,
    ClearedByConfirmation,
    Rejected,
}

/// <summary>The 34 check codes in their fixed order (spec P12-03): 26 Hard, 8 Soft.</summary>
public static class CheckCodes
{
    public const string GateOpen = "gate_open";
    public const string NoHalt = "no_halt";
    public const string BrokerKillSwitchOff = "broker_kill_switch_off";
    public const string TokenValid = "token_valid";
    public const string StaticIpOk = "static_ip_ok";
    public const string SessionWindow = "session_window";
    public const string FeedFresh = "feed_fresh";
    public const string StreamsAlive = "streams_alive";
    public const string TicketValid = "ticket_valid";
    public const string LevelsValid = "levels_valid";
    public const string QuantityValid = "quantity_valid";
    public const string ProductSegment = "product_segment";
    public const string InstrumentTradable = "instrument_tradable";
    public const string LtpCollar = "ltp_collar";
    public const string CircuitBand = "circuit_band";
    public const string SpreadDepth = "spread_depth";
    public const string FreezeQuantity = "freeze_quantity";
    public const string MarginFunds = "margin_funds";
    public const string RiskPerTrade = "risk_per_trade";
    public const string ExposureHeadroom = "exposure_headroom";
    public const string OpenPositions = "open_positions";
    public const string EntriesToday = "entries_today";
    public const string DailyLoss = "daily_loss";
    public const string OnePerSymbol = "one_per_symbol";
    public const string OrderBudget = "order_budget";
    public const string CanaryCap = "canary_cap";
    public const string SectorExposure = "sector_exposure";
    public const string HoldingsConflict = "holdings_conflict";
    public const string AdvShare = "adv_share";
    public const string ResultsWithinDays = "results_within_days";
    public const string FnoBan = "fno_ban";
    public const string Surveillance = "surveillance";
    public const string Correlation = "correlation";
    public const string DataQuality = "data_quality";

    public static readonly IReadOnlyList<string> All =
    [
        GateOpen, NoHalt, BrokerKillSwitchOff, TokenValid, StaticIpOk, SessionWindow, FeedFresh, StreamsAlive,
        TicketValid, LevelsValid, QuantityValid, ProductSegment, InstrumentTradable,
        LtpCollar, CircuitBand, SpreadDepth, FreezeQuantity,
        MarginFunds, RiskPerTrade, ExposureHeadroom, OpenPositions, EntriesToday, DailyLoss, OnePerSymbol, OrderBudget, CanaryCap,
        SectorExposure, HoldingsConflict, AdvShare, ResultsWithinDays, FnoBan, Surveillance, Correlation, DataQuality,
    ];

    /// <summary>The checks re-run just before an entry send (spec criterion 8).</summary>
    public static readonly IReadOnlyList<string> Fast = [GateOpen, NoHalt, BrokerKillSwitchOff, FeedFresh, LtpCollar, CircuitBand, StreamsAlive];

    private static readonly HashSet<string> SoftCodes = [SectorExposure, HoldingsConflict, AdvShare, ResultsWithinDays, FnoBan, Surveillance, Correlation, DataQuality];

    public static CheckClass ClassOf(string code) => SoftCodes.Contains(code) ? CheckClass.Soft : CheckClass.Hard;
}

/// <summary>One stored result. <c>Value</c>/<c>Limit</c> are the numbers judged; <c>InputAgeMs</c> the age of the oldest input read.</summary>
public sealed record CheckResult(string Code, CheckClass Class, CheckOutcome Outcome, double? Value, double? Limit, string Detail, long? InputAgeMs, string Source);

/// <summary>An owner's confirmation of Soft non-passes: bound to the exact set of codes (spec criterion 4).</summary>
public sealed record CheckConfirmation(string Actor, string Reason, IReadOnlyList<string> Codes, DateTimeOffset At);

/// <summary>The result of one run. Deterministic: the same inputs give the same run (the id is assigned by the store).</summary>
public sealed record CheckRun(CheckRunKind Kind, DateTimeOffset At, DateTimeOffset ValidUntil, CheckVerdict Verdict, string SettingsHash, string InputsHash, IReadOnlyList<CheckResult> Results, CheckConfirmation? Confirmation)
{
    /// <summary>Only a full, non-rejected run authorises a send; a fast or advisory run never does.</summary>
    public bool AuthorisesSend => Kind == CheckRunKind.Full && Verdict != CheckVerdict.Rejected;

    public IEnumerable<CheckResult> NonPass => Results.Where(r => r.Outcome != CheckOutcome.Pass);

    /// <summary>The Soft codes that did not pass, sorted: the set a confirmation is bound to.</summary>
    public IReadOnlyList<string> SoftNonPass => [.. Results.Where(r => r.Class == CheckClass.Soft && r.Outcome != CheckOutcome.Pass).Select(r => r.Code).Order(StringComparer.Ordinal)];

    public IReadOnlyList<string> HardNonPass => [.. Results.Where(r => r.Class == CheckClass.Hard && r.Outcome != CheckOutcome.Pass).Select(r => r.Code)];

    internal static string HashOf(IEnumerable<CheckResult> results)
    {
        var sb = new StringBuilder();
        foreach (var r in results)
        {
            sb.Append(r.Code).Append('|').Append(r.Outcome).Append('|').Append(Num(r.Value)).Append('|').Append(Num(r.Limit)).Append('|').Append(r.InputAgeMs?.ToString(CultureInfo.InvariantCulture)).Append('|').Append(r.Source).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static string Num(double? v) => v is { } x ? x.ToString("R", CultureInfo.InvariantCulture) : string.Empty;
}
