using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>The hash of the client code that the reports of an account carry (<c>accounts.report_client_codes</c>, ADR 0029). Reads are not tracked; a new code is saved by the portfolio unit of work. Nothing is updated or deleted.</summary>
internal sealed class ReportClientCodes(XDrishtiDbContext db) : IReportClientCodes
{
    public async ValueTask<string?> FindHashAsync(Guid accountId, CancellationToken cancellationToken) =>
        await db.ReportClientCodes.AsNoTracking().Where(x => x.AccountId == accountId).Select(x => x.Hash).FirstOrDefaultAsync(cancellationToken);

    public void Add(ReportClientCode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        db.ReportClientCodes.Add(code);
    }
}
