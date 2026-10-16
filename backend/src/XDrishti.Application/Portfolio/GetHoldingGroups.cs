using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Groups;
using XDrishti.Application.Portfolio.Income;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The holding groups request as received (query string values, unchecked).</summary>
public sealed record HoldingGroupsRequest(Guid? AccountId, Guid? BucketId, string? Status, string? Q, string? By, string? Sort, string? StackBy);

/// <summary>
/// The holdings of the scope (the filters of the holdings list, and the accounts that count in totals when no account is chosen, like the summary) grouped by <c>by</c> = <c>account|bucket|sector|basket</c> (ADR 0021): per group invested,
/// value, profit, realised, XIRR from the union of its cash flows, day change, counts and weight, plus the scope's total. <c>sort</c> is <c>value|invested|pnl|pnlPct|xirr|dividends|dayChange|name</c>, prefix <c>-</c> for descending (default:
/// <c>-value</c>, the largest first; groups without a value come last); at most 200 groups. <c>stackBy</c> (<c>account</c> when <c>by=sector</c>, <c>sector</c> when <c>by=account</c>) adds each group's value split by that dimension.
/// One read of holdings and lots, one for the sectors of their instruments (sector grouping or stacking) and one for the baskets holding them (basket grouping), and one for the dividends and costs recorded for the scope (ADR 0026): per group the
/// dividends and trade charges of its holdings and the net returns that include them; the dividends and costs of no holding (unallocated dividends, ledger costs) are in the total, in the account's own group when grouping by account and otherwise
/// in an extra last group with the key <c>~unallocated</c> — only when no bucket and no text narrows the scope.
/// </summary>
public sealed record GetHoldingGroupsQuery(HoldingGroupsRequest Request) : IQuery<Result<HoldingGroups>>;

internal sealed class GetHoldingGroupsHandler(
    IHoldingQueries queries,
    IHoldingClassifications classifications,
    IIncomeQueries income,
    TimeProvider clock,
    ILogger<GetHoldingGroupsHandler> logger) : IQueryHandler<GetHoldingGroupsQuery, Result<HoldingGroups>>
{
    public const int MaxGroups = 200;

    private const string DefaultSort = "value";

    private static readonly Dictionary<string, Func<HoldingGroup, object?>> SortKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["value"] = g => g.Value,
        ["invested"] = g => g.Invested,
        ["pnl"] = g => g.Pnl,
        ["pnlPct"] = g => g.PnlPct,
        ["xirr"] = g => g.Xirr,
        ["dividends"] = g => g.Income is { DividendsFrom: not null } income ? income.Dividends : null, // no dividend entered is no value, not zero
        ["dayChange"] = g => g.DayChange,
        ["name"] = g => g.Name,
    };

    public async ValueTask<Result<HoldingGroups>> HandleAsync(GetHoldingGroupsQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var errors = new FieldErrors();
        var (text, status) = HoldingScope.Parse(request.Q, request.Status, errors);

        var validBy = GroupDimensions.TryParse(request.By, out var by);
        if (!validBy)
        {
            errors.Add("by", "Use account, bucket, sector or basket.");
        }

        GroupDimension? stack = null;
        if (!string.IsNullOrWhiteSpace(request.StackBy))
        {
            if (validBy && GroupDimensions.TryParse(request.StackBy, out var wanted) && GroupDimensions.StackFor(by) == wanted)
            {
                stack = wanted;
            }
            else
            {
                errors.Add("stackBy", "Use account when grouping by sector, or sector when grouping by account.");
            }
        }

        // The default is the largest group first; an explicit sort follows the usual rule (a leading '-' for descending).
        var sort = DefaultSort;
        var descending = string.IsNullOrWhiteSpace(request.Sort);
        if (!descending)
        {
            var wanted = request.Sort!.Trim();
            descending = wanted.StartsWith('-');
            sort = descending ? wanted[1..] : wanted;
            if (!SortKeys.ContainsKey(sort))
            {
                errors.Add("sort", "Use value, invested, pnl, pnlPct, xirr, dividends, dayChange or name, optionally with a leading '-'.");
            }
        }

        if (errors.HasErrors)
        {
            return Result.Failure<HoldingGroups>(PortfolioErrors.Invalid(errors));
        }

        var started = clock.GetTimestamp();
        var valuationDate = IndiaTime.Today(clock.GetUtcNow());
        var holdings = await queries.ListAsync(new HoldingFilter(request.AccountId, request.BucketId, status, text, TotalsOnly: request.AccountId is null), cancellationToken);
        var classification = await ClassificationLoader.LoadAsync(
            classifications, holdings, by == GroupDimension.Sector || stack == GroupDimension.Sector, by == GroupDimension.Basket, cancellationToken);

        var book = await IncomeLoader.LoadAsync(income, IncomeLoader.ScopeOf(request.AccountId, request.AccountId is null, holdings, complete: request.BucketId is null && status is null && text is null), cancellationToken);

        // The dividends and costs of no holding belong to an account, not to a bucket or to a text match: they are part of the scope only when neither narrows it.
        IReadOnlyCollection<Guid> accountLevel = request.BucketId is null && text is null ? book.AccountIds : [];
        var scopeValue = GroupCalculator.ValueOf(holdings);
        var groups = HoldingPartitions.Partition(holdings, by, classification)
            .Select(slice => GroupCalculator.Group(
                slice,
                valuationDate,
                scopeValue,
                stack is { } second ? GroupCalculator.Parts(slice, second, classification) : null,
                book,
                by == GroupDimension.Account && accountLevel.Count > 0 ? [slice.Holdings[0].AccountId] : []))
            .ToList();
        var ordered = Sort(groups, SortKeys[sort], descending).Take(MaxGroups).ToList();

        // What no group holds: with accounts as groups only the accounts without a group left over, otherwise every account's own dividends and costs.
        HashSet<Guid> absorbed = by == GroupDimension.Account ? ordered.Select(g => Guid.Parse(g.Key)).ToHashSet() : [];
        if (GroupCalculator.Unallocated(book, [.. accountLevel.Where(id => !absorbed.Contains(id))]) is { } unallocated)
        {
            ordered.Add(unallocated);
        }

        var byName = GroupDimensions.Name(by);
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        HoldingGroupsLog.Computed(logger, byName, holdings.Count, ordered.Count, elapsedMs);
        return Result.Success(new HoldingGroups(
            byName,
            GroupDimensions.IsAdditive(by),
            stack is { } used ? GroupDimensions.Name(used) : null,
            ordered,
            GroupCalculator.Total(holdings, valuationDate, book, accountLevel)));
    }

    /// <summary>Ordered by the key (groups without one last), ties by name and key, so the order is the same on every call.</summary>
    private static List<HoldingGroup> Sort(List<HoldingGroup> groups, Func<HoldingGroup, object?> key, bool descending)
    {
        var withKey = groups.Where(g => key(g) is not null).ToList();
        var without = groups.Where(g => key(g) is null);
        var sorted = descending
            ? withKey.OrderByDescending(key, SortKeyComparer.Instance)
            : withKey.OrderBy(key, SortKeyComparer.Instance);
        return
        [
            .. sorted.ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key, StringComparer.Ordinal),
            .. without.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.Key, StringComparer.Ordinal),
        ];
    }
}
