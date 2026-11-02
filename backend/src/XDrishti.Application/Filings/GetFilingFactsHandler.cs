using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Filings;

/// <summary><c>GET /api/filings/{id}/facts</c>: the parse outcome and stored facts of one filing; <c>filings.not_found</c> for an unknown id.</summary>
internal sealed class GetFilingFactsHandler(IFilingReader reader, IResultFactRows rows) : IQueryHandler<GetFilingFactsQuery, Result<FilingFactsView>>
{
    public async ValueTask<Result<FilingFactsView>> HandleAsync(GetFilingFactsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await reader.FindAsync(query.Id, cancellationToken) is null)
        {
            return Result.Failure<FilingFactsView>(FilingErrors.NotFound());
        }

        var version = Domain.Filings.ResultsParser.Version;
        var parse = await rows.ParseOfAsync(query.Id, version, cancellationToken);
        if (parse is null)
        {
            return Result.Success(new FilingFactsView(query.Id, version, "not_parsed", null, 0, new Dictionary<string, int>(), []));
        }

        var facts = await rows.FactsOfAsync(query.Id, version, cancellationToken);
        return Result.Success(new FilingFactsView(query.Id, version, parse.Outcome, parse.Reason, parse.Facts, parse.Flags, [.. facts.Select(f => new FilingFactView(f.Field, f.Span, f.Basis, f.State, f.Value, f.Unit, f.Reason))]));
    }
}
