namespace XDrishti.Domain.Checklist;

/// <summary>Stable codes of the pre-trade checks (doc 10 section 6b, doc 15 section 2, spec P7-05).</summary>
public static class ChecklistCodes
{
    /// <summary>The account is a Trading account in paper mode. Anything else is refused outright.</summary>
    public const string AccountPaper = "account_paper";

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

    /// <summary>Whether the code is one the owner can approve despite (every check but the account gate).</summary>
    public static bool IsConfirmable(string code) => code != AccountPaper && All.Contains(code);
}
