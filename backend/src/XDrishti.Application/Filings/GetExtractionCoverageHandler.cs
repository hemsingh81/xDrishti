using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Filings;

/// <summary><c>GET /api/filings/extractions/coverage</c>: counts of the stored extractions.</summary>
public sealed record GetExtractionCoverageQuery : IQuery<ExtractionCoverageView>;

/// <summary>The coverage of the LLM announcement flag (P15-08): counts per month of the announcement, model file hash, prompt version, schema version and state. No text, no reason, no answer; the model hash is a file hash, not a path.</summary>
public sealed record ExtractionCoverageView(int Total, IReadOnlyList<ExtractionCoverageRow> Rows);

/// <summary>Reads the counts of <c>filings.extractions</c>; read only, nothing else uses the table.</summary>
internal sealed class GetExtractionCoverageHandler(IAnnouncementExtractionStore store) : IQueryHandler<GetExtractionCoverageQuery, ExtractionCoverageView>
{
    public async ValueTask<ExtractionCoverageView> HandleAsync(GetExtractionCoverageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await store.CoverageAsync(cancellationToken);
        return new ExtractionCoverageView(rows.Sum(r => r.Count), rows);
    }
}
