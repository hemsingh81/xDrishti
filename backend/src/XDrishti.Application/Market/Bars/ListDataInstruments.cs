using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars;

/// <summary>
/// The coverage of the instruments that are tracked or have bars: first bar, last bar, 1-minute bars, source and how far behind. <c>q</c> (symbol, name), <c>status</c>
/// (<c>current|behind|empty</c>), <c>sort</c> (<c>symbol|name|firstBar|lastBar|bars</c>, prefix <c>-</c> for descending), <c>page</c> from 1, <c>pageSize</c> up to 200.
/// </summary>
public sealed record ListDataInstrumentsQuery(DataInstrumentRequest Request) : IQuery<Result<DataInstrumentPage>>;

internal sealed class ListDataInstrumentsHandler(IBarQueries queries, TimeProvider clock) : IQueryHandler<ListDataInstrumentsQuery, Result<DataInstrumentPage>>
{
    public async ValueTask<Result<DataInstrumentPage>> HandleAsync(ListDataInstrumentsQuery query, CancellationToken cancellationToken)
    {
        var search = Check(query.Request);
        if (search.IsFailure)
        {
            return Result.Failure<DataInstrumentPage>(search.Error);
        }

        var expected = DataFreshness.LastClosedSession(clock.GetUtcNow());
        var expectedStart = new DateTimeOffset(expected.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        return Result.Success(await queries.ListInstrumentsAsync(search.Value, expectedStart, expected, cancellationToken));
    }

    internal static Result<DataInstrumentSearch> Check(DataInstrumentRequest r)
    {
        var errors = new FieldErrors();
        var text = r.Q?.Trim();
        if (text is { Length: > DataInstrumentSearch.MaxTextLength })
        {
            errors.Add("q", $"Use at most {DataInstrumentSearch.MaxTextLength} characters.");
        }
        else if (text is not null && text.Any(char.IsControl))
        {
            errors.Add("q", "Control characters are not allowed.");
        }

        DataInstrumentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(r.Status))
        {
            if (Enum.TryParse<DataInstrumentStatus>(r.Status.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) && !int.TryParse(r.Status.Trim(), out _))
            {
                status = parsed;
            }
            else
            {
                errors.Add("status", "Use current, behind or empty.");
            }
        }

        var page = r.Page ?? 1;
        if (page is < 1 or > DataInstrumentSearch.MaxPage)
        {
            errors.Add("page", $"Use 1 to {DataInstrumentSearch.MaxPage}.");
        }

        var pageSize = r.PageSize ?? DataInstrumentSearch.DefaultPageSize;
        if (pageSize is < 1 or > DataInstrumentSearch.MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {DataInstrumentSearch.MaxPageSize}.");
        }

        var (field, descending) = (DataSortField.Symbol, false);
        if (!string.IsNullOrWhiteSpace(r.Sort) && !TrySort(r.Sort.Trim(), out field, out descending))
        {
            errors.Add("sort", "Use symbol, name, firstBar, lastBar or bars, optionally with a leading '-'.");
        }

        return errors.HasErrors
            ? Result.Failure<DataInstrumentSearch>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new DataInstrumentSearch(string.IsNullOrEmpty(text) ? null : text, status, field, descending, page, pageSize));
    }

    private static bool TrySort(string text, out DataSortField field, out bool descending)
    {
        descending = text.StartsWith('-');
        var name = descending ? text[1..] : text;
        return Enum.TryParse(name, ignoreCase: true, out field) && Enum.IsDefined(field) && !int.TryParse(name, out _);
    }
}
