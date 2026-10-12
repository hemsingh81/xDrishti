using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Gaps;

/// <summary>The sessions with missing bars, newest first: <c>status</c> (open|filled|unfillable), <c>instrumentId</c>, <c>page</c> from 1, <c>pageSize</c> up to 200. Anything else is <c>data.invalid</c>.</summary>
public sealed record ListBarGapsQuery(GapRequest Request) : IQuery<Result<GapPage>>;

internal sealed class ListBarGapsHandler(IBarGaps gaps) : IQueryHandler<ListBarGapsQuery, Result<GapPage>>
{
    public async ValueTask<Result<GapPage>> HandleAsync(ListBarGapsQuery query, CancellationToken cancellationToken)
    {
        var search = Check(query.Request);
        return search.IsFailure ? Result.Failure<GapPage>(search.Error) : Result.Success(await gaps.ListAsync(search.Value, cancellationToken));
    }

    internal static Result<GapSearch> Check(GapRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new FieldErrors();
        GapStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (GapStatusCodes.TryParse(request.Status.Trim().ToLowerInvariant(), out var parsed))
            {
                status = parsed;
            }
            else
            {
                errors.Add("status", "Use open, filled or unfillable.");
            }
        }

        var page = request.Page ?? 1;
        if (page is < 1 or > GapSearch.MaxPage)
        {
            errors.Add("page", $"Use 1 to {GapSearch.MaxPage}.");
        }

        var pageSize = request.PageSize ?? GapSearch.DefaultPageSize;
        if (pageSize is < 1 or > GapSearch.MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {GapSearch.MaxPageSize}.");
        }

        return errors.HasErrors
            ? Result.Failure<GapSearch>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new GapSearch(status, request.InstrumentId, page, pageSize));
    }
}
