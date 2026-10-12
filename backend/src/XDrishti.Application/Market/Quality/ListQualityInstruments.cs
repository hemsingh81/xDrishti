using XDrishti.Application.Abstractions.Market.Quality;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Market.Quality;

/// <summary>
/// The tracked instruments that have bars, each with the current result of its checks. <c>q</c> (symbol, name), <c>status</c> (<c>pass|warning|fail|notChecked</c>), <c>sort</c>
/// (<c>symbol|name|status|score|coverage|barsChecked|missingMinutes|checkedAt</c>, prefix <c>-</c> for descending; <c>status</c> orders by severity), <c>page</c> from 1, <c>pageSize</c> up to 200.
/// </summary>
public sealed record ListQualityInstrumentsQuery(QualityInstrumentRequest Request) : IQuery<Result<QualityInstrumentPage>>;

internal sealed class ListQualityInstrumentsHandler(IQualityQueries queries) : IQueryHandler<ListQualityInstrumentsQuery, Result<QualityInstrumentPage>>
{
    public async ValueTask<Result<QualityInstrumentPage>> HandleAsync(ListQualityInstrumentsQuery query, CancellationToken cancellationToken)
    {
        var search = Check(query.Request);
        return search.IsFailure ? Result.Failure<QualityInstrumentPage>(search.Error) : Result.Success(await queries.ListInstrumentsAsync(search.Value, cancellationToken));
    }

    internal static Result<QualityInstrumentSearch> Check(QualityInstrumentRequest r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var errors = new FieldErrors();
        var text = r.Q?.Trim();
        if (text is { Length: > QualityInstrumentSearch.MaxTextLength })
        {
            errors.Add("q", $"Use at most {QualityInstrumentSearch.MaxTextLength} characters.");
        }
        else if (text is not null && text.Any(char.IsControl))
        {
            errors.Add("q", "Control characters are not allowed.");
        }

        string? status = null;
        if (!string.IsNullOrWhiteSpace(r.Status))
        {
            status = new[] { CheckStatus.Pass, CheckStatus.Warning, CheckStatus.Fail, QualityInstrumentSearch.NotChecked }
                .FirstOrDefault(s => string.Equals(s, r.Status.Trim(), StringComparison.OrdinalIgnoreCase));
            if (status is null)
            {
                errors.Add("status", "Use pass, warning, fail or notChecked.");
            }
        }

        var page = r.Page ?? 1;
        if (page is < 1 or > QualityInstrumentSearch.MaxPage)
        {
            errors.Add("page", $"Use 1 to {QualityInstrumentSearch.MaxPage}.");
        }

        var pageSize = r.PageSize ?? QualityInstrumentSearch.DefaultPageSize;
        if (pageSize is < 1 or > QualityInstrumentSearch.MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {QualityInstrumentSearch.MaxPageSize}.");
        }

        var (field, descending) = (QualitySortField.Symbol, false);
        if (!string.IsNullOrWhiteSpace(r.Sort) && !TrySort(r.Sort.Trim(), out field, out descending))
        {
            errors.Add("sort", "Use symbol, name, status, score, coverage, barsChecked, missingMinutes or checkedAt, optionally with a leading '-'.");
        }

        return errors.HasErrors
            ? Result.Failure<QualityInstrumentSearch>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new QualityInstrumentSearch(string.IsNullOrEmpty(text) ? null : text, status, field, descending, page, pageSize));
    }

    private static bool TrySort(string text, out QualitySortField field, out bool descending)
    {
        descending = text.StartsWith('-');
        var name = descending ? text[1..] : text;
        return Enum.TryParse(name, ignoreCase: true, out field) && Enum.IsDefined(field) && !int.TryParse(name, out _);
    }
}
