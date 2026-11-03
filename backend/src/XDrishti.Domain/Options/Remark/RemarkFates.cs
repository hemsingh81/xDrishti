namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The fates of a re-marked cash trade (spec P14-08): <see cref="Paired"/> or exactly one reason it is not. The list of reasons is closed; each is counted and none carries a premium or an R. <see cref="SpreadUnavailable"/> and <see cref="RiskInvalid"/> are the two reasons beyond the spec's ten:
/// a net-basis trade whose half-spread is unavailable (P14-07) has no honest net figure and is counted, never costed as zero; a cash trade whose stop is at its entry has no rupee risk to scale. Also the exit words the option side adds to the cash ones and the labels every row carries.
/// </summary>
public static class RemarkFates
{
    public const string Paired = "paired";
    public const string NoSeries = "no_series";
    public const string NoSeriesForExpiry = "no_series_for_expiry";
    public const string ExpiryUnknown = "expiry_unknown";
    public const string StrikeLeftWindow = "strike_left_window";
    public const string ArchiveUnusable = "archive_unusable";
    public const string NoIv = "no_iv";
    public const string Illiquid = "illiquid";
    public const string LiquidityUnknown = "liquidity_unknown";
    public const string BasisMismatch = "basis_mismatch";
    public const string OpenAtEnd = "open_at_end";
    public const string SpreadUnavailable = "spread_unavailable";
    public const string RiskInvalid = "risk_invalid";

    /// <summary>The option exit that follows a premium stop (the bar low reached the level, or the bar opened below it).</summary>
    public const string PremiumStop = "premium_stop";

    /// <summary>The D6 exit: still open at the close of the last session before the expiry session.</summary>
    public const string ExpiryExit = "expiry_exit";

    public const string BsFallback = "bs_fallback";
    public const string DeltaLabel = "delta: bs_on_vendor_iv";
    public const string ExpiryContract = "expiry: contract";
    public const string ExpiryDerived = "expiry: derived";
    public const string BarTimestampLabel = "bar_ts: start (unverified)";
    public const string FillLabel = "fill: next option bar open at or after the cash minute";
    public const string QtyPerShare = "qty: per share (gross cash trade has no quantity)";

    /// <summary>The ten reasons of the spec plus <see cref="SpreadUnavailable"/> and <see cref="RiskInvalid"/>, in a fixed order.</summary>
    public static IReadOnlyList<string> Unpaired { get; } =
    [
        NoSeries, NoSeriesForExpiry, ExpiryUnknown, StrikeLeftWindow, ArchiveUnusable, NoIv, Illiquid, LiquidityUnknown, BasisMismatch, OpenAtEnd, SpreadUnavailable, RiskInvalid,
    ];
}
