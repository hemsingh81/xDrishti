using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>The corporate actions, newest ex-date first: <c>status</c> (suggested|applied|ignored), <c>instrumentId</c>, <c>page</c> from 1, <c>pageSize</c> up to 200. Anything else is <c>data.invalid</c>. Each item carries <c>csvAdjustedThrough</c>.</summary>
public sealed record ListCorporateActionsQuery(CorporateActionRequest Request) : IQuery<Result<CorporateActionPage>>;

internal sealed class ListCorporateActionsHandler(ICorporateActionQueries queries) : IQueryHandler<ListCorporateActionsQuery, Result<CorporateActionPage>>
{
    public async ValueTask<Result<CorporateActionPage>> HandleAsync(ListCorporateActionsQuery query, CancellationToken cancellationToken)
    {
        var search = Check(query.Request);
        if (search.IsFailure)
        {
            return Result.Failure<CorporateActionPage>(search.Error);
        }

        var page = await queries.ListAsync(search.Value, cancellationToken);
        var through = await queries.ListCsvAdjustedThroughAsync([.. page.Items.Select(i => i.InstrumentId).Distinct()], cancellationToken);
        return Result.Success(through.Count == 0
            ? page
            : page with { Items = [.. page.Items.Select(i => through.TryGetValue(i.InstrumentId, out var last) ? i with { CsvAdjustedThrough = last } : i)] });
    }

    internal static Result<CorporateActionSearch> Check(CorporateActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new FieldErrors();
        CorporateActionStatus? status = null;
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            if (CorporateActionStatusCodes.TryParse(request.Status.Trim().ToLowerInvariant(), out var parsed))
            {
                status = parsed;
            }
            else
            {
                errors.Add("status", "Use suggested, applied or ignored.");
            }
        }

        var page = request.Page ?? 1;
        if (page is < 1 or > CorporateActionSearch.MaxPage)
        {
            errors.Add("page", $"Use 1 to {CorporateActionSearch.MaxPage}.");
        }

        var pageSize = request.PageSize ?? CorporateActionSearch.DefaultPageSize;
        if (pageSize is < 1 or > CorporateActionSearch.MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {CorporateActionSearch.MaxPageSize}.");
        }

        return errors.HasErrors
            ? Result.Failure<CorporateActionSearch>(errors.ToError(DataErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new CorporateActionSearch(status, request.InstrumentId, page, pageSize));
    }
}
