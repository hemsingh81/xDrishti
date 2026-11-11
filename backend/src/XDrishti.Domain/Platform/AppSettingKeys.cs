namespace XDrishti.Domain.Platform;

/// <summary>The keys of the <see cref="AppSetting"/> rows in use.</summary>
public static class AppSettingKeys
{
    /// <summary>The master-list instrument (an index) the portfolio is compared with: its id as text (ADR 0020 §2).</summary>
    public const string PortfolioBenchmarkInstrument = "portfolio.benchmark_instrument";

    /// <summary>The index the basket profile and ranking are compared with: an instrument id as text (BK-06c); without a row the default is Nifty 500, never the portfolio benchmark.</summary>
    public const string BasketBenchmarkInstrument = "reports.basket_benchmark_instrument";

    /// <summary>The thresholds of the Action-center rules as a small JSON object (ADR 0022); without the row the documented defaults apply.</summary>
    public const string PortfolioActionThresholds = "portfolio.action_thresholds";

    /// <summary>The prefix of the keys of the charge sheets (P3-03f, ADR 0044): <c>research.cost_sheet.&lt;id&gt;</c>, the value the canonical JSON of the sheet. The research module checks the value; the id is lower case.</summary>
    public const string CostSheetPrefix = "research.cost_sheet.";

    /// <summary>The option underlyings (P14-01, ADR 0175): a JSON array of canonical symbols; without the row the default is built and written once at the next refresh.</summary>
    public const string OptionUnderlyings = "options.underlyings";
}
