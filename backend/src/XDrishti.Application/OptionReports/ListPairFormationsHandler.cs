using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary><c>GET /api/research/pair-formations</c> (spec P14-16): the stored formations whose window overlaps [from, to] (default the last 400 days up to today, India time; at most 400 days), oldest quarter first, at most 400 rows. An inverted or over-long range is <c>options.invalid_query</c>; nothing stored is an empty list.</summary>
internal sealed class ListPairFormationsHandler(IOptionReportReader reader, TimeProvider clock) : IQueryHandler<ListPairFormationsQuery, Result<PairFormationList>>
{
    public const int MaxRangeDays = 400;

    public const int MaxRows = 400;

    public async ValueTask<Result<PairFormationList>> HandleAsync(ListPairFormationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var to = query.To ?? IndiaTime.Today(clock.GetUtcNow());
        var from = query.From ?? to.AddDays(1 - MaxRangeDays);
        if (from > to)
        {
            return Result.Failure<PairFormationList>(OptionReportErrors.InvalidQuery("from must not be after to."));
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return Result.Failure<PairFormationList>(OptionReportErrors.InvalidQuery($"The range is at most {MaxRangeDays} days."));
        }

        var rows = await reader.PairFormationsAsync(PairFamilies.All, from, to, MaxRows + 1, cancellationToken);
        return Result.Success(rows.Count == 0
            ? new PairFormationList(ReportStates.Unavailable, ReportStates.Reasons.NoRuns, from, to, [], false)
            : new PairFormationList(ReportStates.Ok, null, from, to, [.. rows.Take(MaxRows).Select(PairFormationMapper.ToSummary)], rows.Count > MaxRows));
    }
}
