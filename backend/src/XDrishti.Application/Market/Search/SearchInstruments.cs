using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Search;

/// <summary>
/// Searches the master list: <c>q</c> (symbol, name, display name, ISIN), <c>type</c>, <c>sector</c> (or "Unclassified"), <c>fno</c>, <c>asmGsm</c>,
/// <c>series</c>, <c>tracked</c> (true = tracked now, retired-but-tracked ones included whatever <c>includeRetired</c> says; false = not tracked now), <c>includeRetired</c>, <c>sort</c> (<c>symbol|name|type|series|sector|lot|tick</c>, prefix <c>-</c> for descending), <c>page</c> from 1,
/// <c>pageSize</c> up to 100. The order is stable.
/// </summary>
public sealed record SearchInstrumentsQuery(InstrumentSearchRequest Request) : IQuery<Result<InstrumentPage>>;

internal sealed class SearchInstrumentsHandler(IInstrumentQueries queries) : IQueryHandler<SearchInstrumentsQuery, Result<InstrumentPage>>
{
    public async ValueTask<Result<InstrumentPage>> HandleAsync(SearchInstrumentsQuery query, CancellationToken cancellationToken)
    {
        var checkedSearch = Check(query.Request);
        return checkedSearch.IsFailure
            ? Result.Failure<InstrumentPage>(checkedSearch.Error)
            : Result.Success(await queries.SearchAsync(checkedSearch.Value, cancellationToken));
    }

    internal static Result<InstrumentSearch> Check(InstrumentSearchRequest r)
    {
        var errors = new FieldErrors();
        var text = r.Q?.Trim();
        CheckText(errors, "q", text, InstrumentSearch.MaxTextLength);
        CheckText(errors, "sector", r.Sector?.Trim(), InstrumentSearch.MaxSectorLength);
        CheckText(errors, "series", r.Series?.Trim(), InstrumentSearch.MaxSeriesLength);

        InstrumentType? type = null;
        if (!string.IsNullOrWhiteSpace(r.Type))
        {
            if (InstrumentTypeCodes.TryParse(r.Type.Trim(), out var parsed))
            {
                type = parsed;
            }
            else
            {
                errors.Add("type", "Use share, etf, index or future.");
            }
        }

        var page = r.Page ?? 1;
        if (page is < 1 or > InstrumentSearch.MaxPage)
        {
            errors.Add("page", $"Use 1 to {InstrumentSearch.MaxPage}.");
        }

        var pageSize = r.PageSize ?? InstrumentSearch.DefaultPageSize;
        if (pageSize is < 1 or > InstrumentSearch.MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {InstrumentSearch.MaxPageSize}.");
        }

        var (field, descending) = (InstrumentSortField.Symbol, false);
        if (!string.IsNullOrWhiteSpace(r.Sort) && !TrySort(r.Sort.Trim(), out field, out descending))
        {
            errors.Add("sort", "Use symbol, name, type, series, sector, lot or tick, optionally with a leading '-'.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<InstrumentSearch>(errors.ToError(MarketErrors.InvalidCode, "Some values are not valid."));
        }

        return Result.Success(new InstrumentSearch(
            string.IsNullOrEmpty(text) ? null : text,
            type,
            NullIfBlank(r.Sector),
            r.Fno,
            r.AsmGsm,
            NullIfBlank(r.Series),
            r.IncludeRetired ?? false,
            field,
            descending,
            page,
            pageSize,
            r.Tracked));
    }

    private static void CheckText(FieldErrors errors, string field, string? value, int maxLength)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > maxLength)
        {
            errors.Add(field, $"Use at most {maxLength} characters.");
        }
        else if (value.Any(char.IsControl))
        {
            errors.Add(field, "Control characters are not allowed.");
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool TrySort(string text, out InstrumentSortField field, out bool descending)
    {
        descending = text.StartsWith('-');
        var name = descending ? text[1..] : text;
        var found = Enum.TryParse(name, ignoreCase: true, out field) && Enum.IsDefined(field) && !int.TryParse(name, out _);
        return found;
    }
}
