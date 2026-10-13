namespace XDrishti.Domain.Platform;

/// <summary>The keys of the <see cref="AppSetting"/> rows in use.</summary>
public static class AppSettingKeys
{
    /// <summary>The master-list instrument (an index) the portfolio is compared with: its id as text (ADR 0020 §2).</summary>
    public const string PortfolioBenchmarkInstrument = "portfolio.benchmark_instrument";

    /// <summary>The thresholds of the Action-center rules as a small JSON object (ADR 0022); without the row the documented defaults apply.</summary>
    public const string PortfolioActionThresholds = "portfolio.action_thresholds";
}
