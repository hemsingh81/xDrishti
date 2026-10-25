using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Lists the stored comparisons of exits on identical entries (spec P8-10 part b, ADR 0114). Headers only; the numbers of a comparison come from the report engine over its member runs, so there is one source of them.</summary>
internal sealed class ListReportComparisonsHandler(IReportComparisonSource source) : IQueryHandler<ListReportComparisonsQuery, Result<ReportComparisonsView>>
{
    internal static readonly string[] Limits =
    [
        "Same entries, different exits: every member of a comparison enters on the same signals, so n is the same for every exit.",
        "In-sample on replayed history with modelled fills: the best exit here is not validated out of sample (see the stored exit choices for that).",
        "A report over the members is split by exit; the screen adds the split.",
    ];

    public async ValueTask<Result<ReportComparisonsView>> HandleAsync(ListReportComparisonsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var comparisons = await source.ListComparisonsAsync(cancellationToken);
        return Result.Success(new ReportComparisonsView(
            [.. comparisons
                .OrderByDescending(c => c.CreatedAt)
                .ThenBy(c => c.ComparisonKey, StringComparer.Ordinal)
                .Select(c => new ComparisonView(c.ComparisonKey, c.EntriesKey, c.StrategyId, c.Unit, c.CommonEntries, [.. c.Members.Select(m => new ComparisonMemberView(m.RunKey, m.CandidateId))], c.CreatedAt))],
            Limits));
    }
}
