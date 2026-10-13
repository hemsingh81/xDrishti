using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The holdings request as received (query string values, unchecked).</summary>
public sealed record HoldingsRequest(Guid? AccountId, Guid? BucketId, string? Status, string? Q, string? Sort, int? Page, int? PageSize);

/// <summary>
/// Lists holdings with their lots summary and returns. <c>status</c> is <c>open</c> (default), <c>closed</c> or <c>all</c>; <c>q</c> matches symbol,
/// name and ISIN; <c>sort</c> is one of <c>symbol|name|qty|avgCost|lastPrice|invested|value|pnl|pnlPct|xirr|dayChange|dayChangePct|acquiredOn|bucket|account</c>, prefix
/// <c>-</c> for descending (default <c>symbol</c>; rows without a value come last); <c>page</c> from 1, <c>pageSize</c> up to 100.
/// </summary>
public sealed record ListHoldingsQuery(HoldingsRequest Request) : IQuery<Result<HoldingPage>>;

internal sealed class ListHoldingsHandler(IHoldingQueries queries, TimeProvider clock) : IQueryHandler<ListHoldingsQuery, Result<HoldingPage>>
{
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    public const int MaxTextLength = 100;

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
        var text = request.Q?.Trim();
        if (text is { Length: > MaxTextLength })
        {
            errors.Add("q", $"Use at most {MaxTextLength} characters.");
        }
        else if (text is not null && text.Any(char.IsControl))
        {
            errors.Add("q", "Control characters are not allowed.");
        }

        string? status = "open";
        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            switch (request.Status.Trim().ToLowerInvariant())
            {
                case "open":
                    break;
                case "closed":
                    status = "closed";
                    break;
                case "all":
                    status = null;
                    break;
                default:
                    errors.Add("status", "Use open, closed or all.");
                    break;
            }
        }

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

        if (errors.HasErrors)
        {
            return Result.Failure<HoldingPage>(PortfolioErrors.Invalid(errors));
        }

        var valuationDate = IndiaTime.Today(clock.GetUtcNow());
        var holdings = await queries.ListAsync(new HoldingFilter(request.AccountId, request.BucketId, status, string.IsNullOrEmpty(text) ? null : text), cancellationToken);
        var rows = holdings.Select(h => HoldingViews.Row(h, valuationDate)).ToList();
        var ordered = Sort(rows, SortKeys[sort], descending);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Result.Success(new HoldingPage(items, page, pageSize, rows.Count));
    }

    /// <summary>Ordered by the key (rows without one last), ties by symbol and id, so pages never overlap or skip.</summary>
    private static List<HoldingRow> Sort(List<HoldingRow> rows, Func<HoldingRow, object?> key, bool descending)
    {
        var withKey = rows.Where(r => key(r) is not null).ToList();
        var without = rows.Where(r => key(r) is null);
        var comparer = new KeyComparer();
        var sorted = descending
            ? withKey.OrderByDescending(key, comparer)
            : withKey.OrderBy(key, comparer);
        return
        [
            .. sorted.ThenBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id),
            .. without.OrderBy(r => r.Symbol, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id),
        ];
    }

    private sealed class KeyComparer : IComparer<object?>
    {
        public int Compare(object? x, object? y) =>
            x is string a && y is string b ? StringComparer.OrdinalIgnoreCase.Compare(a, b) : Comparer<object>.Default.Compare(x!, y!);
    }
}
