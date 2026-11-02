using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Filings;

/// <summary><c>GET /api/filings/{id}</c>: the metadata of one envelope; <c>filings.not_found</c> for an unknown id.</summary>
internal sealed class GetFilingHandler(IFilingReader reader) : IQueryHandler<GetFilingQuery, Result<FilingView>>
{
    public async ValueTask<Result<FilingView>> HandleAsync(GetFilingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await reader.FindAsync(query.Id, cancellationToken) is { } view ? Result.Success(view) : Result.Failure<FilingView>(FilingErrors.NotFound());
    }
}
