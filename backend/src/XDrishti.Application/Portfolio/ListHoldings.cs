using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Application.Portfolio.Income;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The holdings request as received (query string values, unchecked).</summary>
public sealed record HoldingsRequest(
    Guid? AccountId, Guid? BucketId, string? Status, string? Q, string? Sort, int? Page, int? PageSize, string? GroupBy = null, string? GroupKey = null);

/// <summary>
/// Lists holdings with their lots summary and returns. <c>status</c> is <c>open</c> (default), <c>closed</c> or <c>all</c>; <c>q</c> matches symbol,
/// name and ISIN; <c>sort</c> is one of <c>symbol|name|qty|avgCost|lastPrice|invested|value|pnl|pnlPct|xirr|dividends|dayChange|dayChangePct|acquiredOn|bucket|account</c>, prefix
/// <c>-</c> for descending (default <c>symbol</c>; rows without a value come last); <c>page</c> from 1, <c>pageSize</c> up to 100. <c>groupBy</c> (<c>account|bucket|sector|basket</c>) with <c>groupKey</c> (an account id, bucket id, sector name
/// or basket id, or <c>~none</c> for "Unclassified" / "No basket") keeps only the holdings of that group (ADR 0021); both are required together, an unknown key gives an empty page. With a group, accounts that do not count in totals are left
/// out when no account is chosen, as in the group figures.
/// </summary>
public sealed record ListHoldingsQuery(HoldingsRequest Request) : IQuery<Result<HoldingPage>>;

internal sealed class ListHoldingsHandler(IHoldingQueries queries, IHoldingClassifications classifications, IIncomeQueries income, TimeProvider clock) : IQueryHandler<ListHoldingsQuery, Result<HoldingPage>>
{
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    /// <summary>
    /// What a holding is ordered by. Each key works out only what it needs, and once: the sums of the money figures and the day change are cheap; the XIRR (the gross one, only for sort=xirr) is not, and the net XIRR is never needed to order — it is
    /// worked out for the rows of the page only, after ordering and paging.
    /// </summary>
    private static readonly Dictionary<string, Func<SortRow, object?>> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["symbol"] = r => r.Holding.TradingSymbol,
        ["name"] = r => r.Holding.Name,
        ["qty"] = r => r.Holding.Qty,
        ["avgCost"] = r => r.Holding.AvgCost,
        ["lastPrice"] = r => r.Holding.LastPrice,
        ["invested"] = r => r.Money.Invested,
        ["value"] = r => r.Money.Value,
        ["pnl"] = r => r.Money.Pnl,
        ["pnlPct"] = r => r.Money.PnlPct,
        ["xirr"] = r => r.Returns.Xirr,
        ["dividends"] = r => r.Dividends, // no dividend entered is no value (last), not zero
        ["dayChange"] = r => r.DayChange?.Amount,
        ["dayChangePct"] = r => r.DayChange?.Pct,
        ["acquiredOn"] = r => r.EarliestAcquiredOn,
        ["bucket"] = r => r.Holding.BucketName,
        ["account"] = r => r.Holding.AccountName,
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
                errors.Add("sort", "Use symbol, name, qty, avgCost, lastPrice, invested, value, pnl, pnlPct, xirr, dividends, dayChange, dayChangePct, acquiredOn, bucket or account, optionally with a leading '-'.");
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

        // The rows need the holdings' own dividends and trade charges only, never the ledger's costs. The keys the read matches a trade's security against are the holdings already read when these are all the holdings of the accounts.
        var complete = request.BucketId is null && status is null && text is null && grouping is null;
        var book = holdings.Count == 0
            ? IncomeBook.Empty
            : await IncomeLoader.LoadAsync(income, IncomeLoader.ScopeOf(request.AccountId, grouping is not null && request.AccountId is null, holdings, complete, accountLevel: false), cancellationToken);
        var candidates = holdings.Select(h => new SortRow(h, valuationDate, book)).ToList();
        var items = Sort(candidates, SortKeys[sort], descending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => c.Row())
            .ToList();
        return Result.Success(new HoldingPage(items, page, pageSize, candidates.Count));
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
    private static List<SortRow> Sort(List<SortRow> rows, Func<SortRow, object?> key, bool descending)
    {
        // The key of a row is worked out once, here.
        var keyed = rows.Select(r => (Row: r, Key: key(r))).ToList();
        var withKey = keyed.Where(r => r.Key is not null).ToList();
        var without = keyed.Where(r => r.Key is null).Select(r => r.Row);
        var comparer = SortKeyComparer.Instance;
        var sorted = descending
            ? withKey.OrderByDescending(r => r.Key, comparer)
            : withKey.OrderBy(r => r.Key, comparer);
        return
        [
            .. sorted.ThenBy(r => r.Row.Holding.TradingSymbol, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Row.Holding.Id).Select(r => r.Row),
            .. without.OrderBy(r => r.Holding.TradingSymbol, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Holding.Id),
        ];
    }

    /// <summary>A holding on its way to a page: its ordering keys are worked out on demand, once; the row (with the net XIRR) only when it is on the page.</summary>
    private sealed class SortRow(HoldingData holding, DateOnly valuationDate, IncomeBook book)
    {
        private ReturnsView? _money;

        private ReturnsView? _full;

        private ReturnFigures? _figures;

        private bool _dayChangeRead;

        private HoldingDayChange? _dayChange;

        public HoldingData Holding { get; } = holding;

        /// <summary>The money figures; the XIRR is not worked out.</summary>
        public ReturnsView Money => _money ??= HoldingViews.Returns(ReturnsCalculator.ForHolding(Holding, valuationDate, withXirr: false));

        /// <summary>All the gross figures, the XIRR included.</summary>
        public ReturnsView Returns => _full ??= HoldingViews.Returns(Figures);

        public decimal? Dividends => IncomeCalculator.DividendsOf(book, Holding.Id);

        public HoldingDayChange? DayChange
        {
            get
            {
                if (!_dayChangeRead)
                {
                    _dayChange = DayChangeCalculator.ForHolding(Holding);
                    _dayChangeRead = true;
                }

                return _dayChange;
            }
        }

        public DateOnly? EarliestAcquiredOn
        {
            get
            {
                var open = Holding.Lots.Where(l => l.IsOpen).ToList();
                return open.Count == 0 ? null : open.Min(l => l.AcquiredOn);
            }
        }

        private ReturnFigures Figures => _figures ??= ReturnsCalculator.ForHolding(Holding, valuationDate);

        public HoldingRow Row() => HoldingViews.Row(Holding, valuationDate, book, Figures);
    }
}
