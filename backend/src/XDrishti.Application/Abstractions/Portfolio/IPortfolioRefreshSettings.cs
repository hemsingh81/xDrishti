namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Deployment setting of the automatic holdings refresh (<c>Portfolio:AutoRefresh</c>). The per-account schedule is part of the account.</summary>
public interface IPortfolioRefreshSettings
{
    /// <summary>Whether the API evaluates the accounts' refresh schedules at all (default true; tests switch it off).</summary>
    bool AutoRefreshEnabled { get; }
}
