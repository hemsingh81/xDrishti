namespace XDrishti.Domain.Platform;

/// <summary>The keys of the <see cref="AppSetting"/> rows in use.</summary>
public static class AppSettingKeys
{
    /// <summary>The master-list instrument (an index) the portfolio is compared with: its id as text (ADR 0020 §2).</summary>
    public const string PortfolioBenchmarkInstrument = "portfolio.benchmark_instrument";

    /// <summary>The thresholds of the Action-center rules as a small JSON object (ADR 0022); without the row the documented defaults apply.</summary>
    public const string PortfolioActionThresholds = "portfolio.action_thresholds";

    /// <summary>The prefix of the keys of the charge sheets (P3-03f, ADR 0044): <c>research.cost_sheet.&lt;id&gt;</c>, the value the canonical JSON of the sheet. The research module checks the value; the id is lower case.</summary>
    public const string CostSheetPrefix = "research.cost_sheet.";

    /// <summary>The owner's confirmation of a stored charge sheet as their own (ISS-25, P8-06, ADR 0100): <c>{"sheet_id":"...","confirmed_on":"yyyy-MM-dd"}</c>. Only the owner's own act writes it (not part of P8-06); a report is net only while it exists and names a stored sheet.</summary>
    public const string ReportsChargeSheetConfirmation = "reports.charge_sheet_confirmation";
}
