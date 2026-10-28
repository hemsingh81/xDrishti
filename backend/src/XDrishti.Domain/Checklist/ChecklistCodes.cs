namespace XDrishti.Domain.Checklist;

/// <summary>Stable codes of the pre-trade checks (doc 10 section 6b, doc 15 section 2, spec P7-05).</summary>
public static class ChecklistCodes
{
    /// <summary>The account is a Trading account in paper mode. Anything else is refused outright.</summary>
    public const string AccountPaper = "account_paper";

    /// <summary>The account is a live account that may receive a ticket now (spec P11-04, ADR 0148): a ramp step is set, no halt is open, the last session's capture has no gap and the loss of the day can be measured. It takes the place of <see cref="AccountPaper"/> for a manual-live account and is never confirmable.</summary>
    public const string AccountLiveReady = "account_live_ready";

    /// <summary>Risk of the ticket against the account's per-trade limit.</summary>
    public const string RiskProfile = "risk_profile";

    /// <summary>Cost of the ticket against the funds the account can use.</summary>
    public const string CapitalHeadroom = "capital_headroom";

    /// <summary>Open risk plus the ticket against the account's total open risk limit.</summary>
    public const string ExposureHeadroom = "exposure_headroom";

    /// <summary>Today's loss against the account's daily loss limit.</summary>
    public const string DailyLoss = "daily_loss";

    /// <summary>The instrument's latest validation status.</summary>
    public const string DataQuality = "data_quality";

    /// <summary>SELL against a long holding, or an open trade already exists.</summary>
    public const string HoldingsConflict = "holdings_conflict";

    /// <summary>Open trades plus approved tickets in the sector against the account's limit. Doc 16 calls the policy key <c>sector_limit</c>; the code and the policy key used here are both <c>sector_exposure</c> (doc 15's name).</summary>
    public const string SectorExposure = "sector_exposure";

    /// <summary>Quarterly results within the configured number of days.</summary>
    public const string ResultsWithinDays = "results_within_days";

    /// <summary>The instrument is in the F&amp;O ban list.</summary>
    public const string FnoBan = "fno_ban";

    /// <summary>The instrument is under exchange surveillance (ASM / GSM).</summary>
    public const string Surveillance = "surveillance";

    /// <summary>The entry price against the circuit / price band.</summary>
    public const string PriceBand = "price_band";

    /// <summary>The ticket's quantity against the average daily volume.</summary>
    public const string Liquidity = "liquidity";

    /// <summary>Correlation with open trades and approved tickets.</summary>
    public const string Correlation = "correlation";

    /// <summary>Every check of an evaluated ticket, in the fixed order. <see cref="AccountPaper"/> is first.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        AccountPaper, RiskProfile, CapitalHeadroom, ExposureHeadroom, DailyLoss, DataQuality, HoldingsConflict,
        SectorExposure, ResultsWithinDays, FnoBan, Surveillance, PriceBand, Liquidity, Correlation,
    ];

    /// <summary>The checks of a ticket for a live account: the same list with the live-ready gate first in place of <see cref="AccountPaper"/>.</summary>
    public static readonly IReadOnlyList<string> LiveAll = [AccountLiveReady, .. All.Skip(1)];

    /// <summary>Whether the codes are exactly the full list, in order, for a paper account or for a live account.</summary>
    public static bool IsComplete(IEnumerable<string> codes) => codes.SequenceEqual(All) || codes.SequenceEqual(LiveAll);

    /// <summary>Whether the code is one the owner can approve despite (every check but the two account gates).</summary>
    public static bool IsConfirmable(string code) => code != AccountPaper && code != AccountLiveReady && All.Contains(code);
}
