using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The hash of the client code (UCC) that the reports of an account carry (<c>accounts.report_client_codes</c>, ADR 0029). Saved by <see cref="IPortfolioUnitOfWork"/>; never changed or deleted.</summary>
public interface IReportClientCodes
{
    /// <summary>The remembered hash of the account; null when no report with a client code was imported yet.</summary>
    ValueTask<string?> FindHashAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Collects the code; <see cref="IPortfolioUnitOfWork"/> saves it. A second code of the same account makes the save fail.</summary>
    void Add(ReportClientCode code);
}
