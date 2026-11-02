using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>GET /api/filings/coverage</c> (spec P15-02 criterion 11): per kind and quarter the counts of stored, unavailable (by reason), superseded and out-of-order filings and the tracked instruments with none, for a range of at most 800 days (default: the last 365). Counts
/// only; a kind with nothing stored answers <c>unavailable</c>.
/// </summary>
internal sealed class GetFilingCoverageHandler(IFilingReader reader, TimeProvider clock) : IQueryHandler<GetFilingCoverageQuery, Result<FilingCoverageView>>
{
    public const int MaxRangeDays = 800;

    public const int DefaultDays = 365;

    public async ValueTask<Result<FilingCoverageView>> HandleAsync(GetFilingCoverageQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var to = query.To ?? IndiaTime.Today(clock.GetUtcNow());
        var from = query.From ?? to.AddDays(1 - DefaultDays);
        if (from > to)
        {
            return Result.Failure<FilingCoverageView>(FilingErrors.InvalidQuery("from must not be after to."));
        }

        if (to.DayNumber - from.DayNumber + 1 > MaxRangeDays)
        {
            return Result.Failure<FilingCoverageView>(FilingErrors.InvalidQuery($"The range is at most {MaxRangeDays} days."));
        }

        var data = await reader.CoverageAsync(from, to, cancellationToken);
        var kinds = new List<FilingKindCoverage>();
        foreach (var kind in Enum.GetValues<FilingKind>())
        {
            var code = FilingKinds.Code(kind);
            var quarters = data.Cells.Where(c => c.Kind == code).OrderBy(c => c.QuarterEnd)
                .Select(c => new FilingQuarterCoverage(c.QuarterEnd, c.Ok, [.. c.Unavailable.OrderBy(u => u.Key, StringComparer.Ordinal).Select(u => new FilingReasonCount(u.Key, u.Value))], c.Superseded, c.OutOfOrder))
                .ToList();
            kinds.Add(new FilingKindCoverage(code, quarters.Count > 0 ? AsOfResult.Value : AsOfResult.Unavailable, data.InstrumentsWithout.GetValueOrDefault(code, data.TrackedInstruments), quarters));
        }

        return Result.Success(new FilingCoverageView(from, to, data.TrackedInstruments, kinds));
    }
}
