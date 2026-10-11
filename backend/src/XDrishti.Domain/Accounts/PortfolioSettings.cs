using XDrishti.Domain.Portfolio;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// Portfolio role of an account. Kept when the role is switched off, but inactive then. An account with the role has its own default
/// bucket (<see cref="PortfolioBucket.DefaultForAccountId"/>), which is where imports go unless a custom bucket is chosen here.
/// It also says whether the holdings refresh by themselves (<see cref="Refresh"/>, off by default) and which date a first import gives (<see cref="ImportDate"/>, ADR 0013).
/// </summary>
public sealed class PortfolioSettings
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private PortfolioSettings(
        bool enabled,
        Guid? defaultBucketId,
        bool includeInTotals,
        PortfolioRefreshMode refreshMode,
        TimeOnly refreshDailyTimeIst,
        DateOnly? firstImportDate)
    {
        Enabled = enabled;
        DefaultBucketId = defaultBucketId;
        IncludeInTotals = includeInTotals;
        RefreshMode = refreshMode;
        RefreshDailyTimeIst = refreshDailyTimeIst;
        FirstImportDate = firstImportDate;
    }

    public bool Enabled { get; private set; }

    /// <summary>The custom bucket imports go to; null means the account's own default bucket.</summary>
    public Guid? DefaultBucketId { get; private set; }

    public bool IncludeInTotals { get; private set; }

    public PortfolioRefreshMode RefreshMode { get; private set; }

    /// <summary>Time of day, India time, of the automatic refresh in <see cref="PortfolioRefreshMode.Daily"/> mode.</summary>
    public TimeOnly RefreshDailyTimeIst { get; private set; }

    /// <summary>The automatic refresh as one value.</summary>
    public PortfolioRefresh Refresh => new(RefreshMode, RefreshDailyTimeIst);

    /// <summary>The date the owner chose for holdings imported without a date of their own; null until chosen (see <see cref="ImportDate"/>).</summary>
    public DateOnly? FirstImportDate { get; private set; }

    /// <summary>The first import date in effect: the chosen one, otherwise 2025-01-01.</summary>
    public DateOnly ImportDate => FirstImportDate ?? HoldingRules.DefaultImportDate;

    internal static PortfolioSettings CreateDefault() =>
        new(false, null, true, PortfolioRefresh.Default.Mode, PortfolioRefresh.Default.DailyTimeIst, null);

    /// <summary>Stores the configuration. The caller has checked that the bucket is a custom bucket (or null for the account's own).</summary>
    internal void Configure(PortfolioSpec spec)
    {
        DefaultBucketId = spec.DefaultBucketId;
        IncludeInTotals = spec.IncludeInTotals;
        if (spec.Refresh is { } refresh)
        {
            RefreshMode = refresh.Mode;
            RefreshDailyTimeIst = refresh.DailyTimeIst;
        }

        if (spec.FirstImportDate is { } firstImportDate)
        {
            FirstImportDate = firstImportDate;
        }
    }

    /// <summary>Switches the role on or off (settings are kept).</summary>
    internal void SetEnabled(bool enabled) => Enabled = enabled;
}
