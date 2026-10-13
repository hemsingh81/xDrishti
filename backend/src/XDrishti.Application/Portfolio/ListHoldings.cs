using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The holdings request as received (query string values, unchecked).</summary>
public sealed record HoldingsRequest(
    Guid? AccountId, Guid? BucketId, string? Status, string? Q, string? Sort, int? Page, int? PageSize, string? GroupBy = null, string? GroupKey = null);

/// <summary>
/// Lists holdings with their lots summary and returns. <c>status</c> is <c>open</c> (default), <c>closed</c> or <c>all</c>; <c>q</c> matches symbol,
/// name and ISIN; <c>sort</c> is one of <c>symbol|name|qty|avgCost|lastPrice|invested|value|pnl|pnlPct|xirr|dayChange|dayChangePct|acquiredOn|bucket|account</c>, prefix
/// <c>-</c> for descending (default <c>symbol</c>; rows without a value come last); <c>page</c> from 1, <c>pageSize</c> up to 100. <c>groupBy</c> (<c>account|bucket|sector|basket</c>) with <c>groupKey</c> (an account id, bucket id, sector name
/// or basket id, or <c>~none</c> for "Unclassified" / "No basket") keeps only the holdings of that group (ADR 0021); both are required together, an unknown key gives an empty page. With a group, accounts that do not count in totals are left
/// out when no account is chosen, as in the group figures.
/// </summary>
public sealed record ListHoldingsQuery(HoldingsRequest Request) : IQuery<Result<HoldingPage>>;

internal sealed class ListHoldingsHandler(IHoldingQueries queries, IHoldingClassifications classifications, TimeProvider clock) : IQueryHandler<ListHoldingsQuery, Result<HoldingPage>>
{
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    private static readonly Dictionary<string, Func<HoldingRow, object?>> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["symbol"] = r => r.Symbol,
        ["name"] = r => r.Name,
        ["qty"] = r => r.Qty,
        ["avgCost"] = r => r.AvgCost,
        ["lastPrice"] = r => r.LastPrice,
        ["invested"] = r => r.Returns.Invested,
        ["value"] = r => r.Returns.Value,
        ["pnl"] = r => r.Returns.Pnl,
        ["pnlPct"] = r => r.Returns.PnlPct,
        ["xirr"] = r => r.Returns.Xirr,
        ["dayChange"] = r => r.DayChange,
        ["dayChangePct"] = r => r.DayChangePct,
        ["acquiredOn"] = r => r.EarliestAcquiredOn,
        ["bucket"] = r => r.BucketName,
        ["account"] = r => r.AccountName,
    };

    public async ValueTask<Result<HoldingPage>> HandleAsync(ListHoldingsQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var errors = new FieldErrors();
        var (text, status) = HoldingScope.Parse(request.Q, request.Status, errors);

        var page = request.Page ?? 1;
        if (page is < 1 or > MaxPage)
        {
            errors.Add("page", $"Use 1 to {MaxPage}.");
        }

        var pageSize = request.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {MaxPageSize}.");
        }

        var sort = "symbol";
        var descending = false;
        if (!string.IsNullOrWhiteSpace(request.Sort))
        {
            var wanted = request.Sort.Trim();
            descending = wanted.StartsWith('-');
            sort = descending ? wanted[1..] : wanted;
            if (!SortKeys.ContainsKey(sort))
            {
                errors.Add("sort", "Use symbol, name, qty, avgCost, lastPrice, invested, value, pnl, pnlPct, xirr, dayChange, dayChangePct, acquiredOn, bucket or account, optionally with a leading '-'.");
            }
        }

        var grouping = ParseGroup(request, errors);
        if (errors.HasErrors)
        {
            return Result.Failure<HoldingPage>(PortfolioErrors.Invalid(errors));
        }

        var valuationDate = IndiaTime.Today(clock.GetUtcNow());
        var holdings = await queries.ListAsync(new HoldingFilter(request.AccountId, request.BucketId, status, text, TotalsOnly: grouping is not null && request.AccountId is null), cancellationToken);
        if (grouping is { } group)
        {
            var classification = await ClassificationLoader.LoadAsync(classifications, holdings, group.Dimension == GroupDimension.Sector, group.Dimension == GroupDimension.Basket, cancellationToken);
            holdings = [.. holdings.Where(h => HoldingPartitions.InGroup(h, group.Dimension, group.Key, classification))];
        }

        var rows = holdings.Select(h => HoldingViews.Row(h, valuationDate)).ToList();
        var ordered = Sort(rows, SortKeys[sort], descending);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Result.Success(new HoldingPage(items, page, pageSize, rows.Count));
    }

    /// <summary><c>groupBy</c> and <c>groupKey</c> come together; null when neither is given.</summary>
    private static (GroupDimension Dimension, string Key)? ParseGroup(HoldingsRequest request, FieldErrors errors)
    {
        if (request.GroupBy is null && request.GroupKey is null)
        {
            return null;
        }

        var valid = true;
        if (!GroupDimensions.TryParse(request.GroupBy, out var dimension))
        {
            errors.Add("groupBy", "Use account, bucket, sector or basket.");
            valid = false;
        }

        var key = request.GroupKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            errors.Add("groupKey", "Give the key of the group (with groupBy).");
            valid = false;
        }
        else if (key.Length > HoldingScope.MaxTextLength || key.Any(char.IsControl))
        {
            errors.Add("groupKey", $"Use at most {HoldingScope.MaxTextLength} characters, without control characters.");
            valid = false;
        }

        return valid ? (dimension, key!) : null;
    }

    /// <summary>Ordered by the key (rows without one last), ties by symbol and id, so pages never overlap or skip.</summary>
    private static List<HoldingRow> Sort(List<HoldingRow> rows, Func<HoldingRow, object?> key, bool descending)
    {
        var withKey = rows.Where(r => key(r) is not null).ToList();
        var without = rows.Where(r => key(r) is null);
        var comparer = SortKeyComparer.Instance;
        var sorted = descending
            ? withKey.OrderByDescending(key, comparer)
            : withKey.OrderBy(key, comparer);
        return
        [
            .. sorted.ThenBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id),
            .. without.OrderBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id),
        ];
    }
}
