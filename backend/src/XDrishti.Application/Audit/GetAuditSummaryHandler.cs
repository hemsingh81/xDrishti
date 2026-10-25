using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Audit;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Audit;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Audit;

/// <summary>Counts of the window per store, category and outcome (spec P8-22): one grouped count per store, so the cost does not grow with the rows shown.</summary>
internal sealed class GetAuditSummaryHandler(IEnumerable<IAuditSource> sources, TimeProvider clock, ILogger<GetAuditSummaryHandler> logger) : IQueryHandler<GetAuditSummaryQuery, Result<AuditSummaryView>>
{
    public async ValueTask<Result<AuditSummaryView>> HandleAsync(GetAuditSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var problems = new List<FieldError>();
        var window = AuditCriteria.ParseWindow(query.From, query.To, clock.GetUtcNow(), problems);
        if (problems.Count > 0)
        {
            return Result.Failure<AuditSummaryView>(AuditErrors.Invalid(problems));
        }

        var counts = await Task.WhenAll(sources.OrderBy(s => s.Source).Select(async s =>
        {
            try
            {
                return (s.Source, Rows: (IReadOnlyList<AuditKindCount>?)await s.CountAsync(window.Start, window.End, cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AuditReadLog.SourceFailed(logger, AuditSources.Code(s.Source), ex.GetType().Name);
                return (s.Source, Rows: null);
            }
        }));

        var states = AuditSources.All.Select(source =>
        {
            var rows = counts.FirstOrDefault(c => c.Source == source).Rows;
            var code = AuditSources.Code(source);
            return rows is null ? new AuditSourceState(code, "unavailable", "read_failed") : rows.Sum(r => r.Count) == 0 ? new AuditSourceState(code, "empty", "no_rows") : new AuditSourceState(code, "value", null);
        }).ToList();

        var all = counts.Where(c => c.Rows is not null).SelectMany(c => c.Rows!.Select(r => (c.Source, r.Kind, r.Count))).ToList();
        return Result.Success(new AuditSummaryView(
            new AuditWindowView(window.From, window.To),
            all.Sum(x => x.Count),
            [.. all.GroupBy(x => x.Source).OrderBy(g => g.Key).Select(g => new AuditCount(AuditSources.Code(g.Key), g.Sum(x => x.Count)))],
            [.. all.GroupBy(x => AuditKinds.CategoryOf(x.Source, x.Kind)).OrderByDescending(g => g.Sum(x => x.Count)).Select(g => new AuditCount(g.Key, g.Sum(x => x.Count)))],
            [.. all.GroupBy(x => AuditKinds.OutcomeOf(x.Source, x.Kind)).OrderByDescending(g => g.Sum(x => x.Count)).Select(g => new AuditCount(g.Key, g.Sum(x => x.Count)))],
            states));
    }
}
