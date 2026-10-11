namespace XDrishti.Infrastructure.Portfolio;

/// <summary>Deployment setting of the automatic holdings refresh (configuration section <c>Portfolio:AutoRefresh</c>).</summary>
internal sealed class PortfolioRefreshOptions
{
    public const string SectionName = "Portfolio:AutoRefresh";

    /// <summary>Whether the API evaluates the accounts' refresh schedules (each account's own mode is off unless the owner turns it on).</summary>
    public bool Enabled { get; set; } = true;
}
