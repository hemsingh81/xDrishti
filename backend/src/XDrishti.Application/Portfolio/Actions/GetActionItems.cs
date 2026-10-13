using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>The Action-center request as received (query string values, unchecked). <c>Status</c> is <c>open</c> (default), <c>snoozed</c>, <c>done</c> or <c>all</c>.</summary>
public sealed record ActionItemsRequest(Guid? AccountId, Guid? BucketId, string? Status);

/// <summary>
/// The holdings that need a decision, with the reasons (ADR 0022): the seven rules evaluated on read from the same holdings, lots and prices as the summary — one read of holdings and lots, one of the sectors, one of the baskets, one of the owner's
/// decisions, and the benchmark index's day bars only when a holding is old enough for the lagging-Nifty rule. Scope as the summary: one account (404 when it does not exist), or every active Portfolio account that counts in totals; a bucket keeps the
/// items about its holdings (weights and sector shares stay shares of the whole account). Items most important first (severity, then the size of the trigger), filtered by <c>status</c>; the counts cover the whole scope. Read-only, except for one
/// idempotent side effect: a <c>done</c> decision whose item the rules no longer produce is cleared (see <see cref="ActionReconciler"/>), so a recurrence shows again.
/// </summary>
public sealed record GetActionItemsQuery(ActionItemsRequest Request) : IQuery<Result<ActionList>>;

internal sealed class GetActionItemsHandler(
    ActionEvaluator evaluator,
    ActionReconciler reconciler,
    IAccountStore accounts,
    TimeProvider clock,
    ILogger<GetActionItemsHandler> logger) : IQueryHandler<GetActionItemsQuery, Result<ActionList>>
{
    public async ValueTask<Result<ActionList>> HandleAsync(GetActionItemsQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var status = ParseStatus(request.Status);
        if (status is null)
        {
            return Result.Failure<ActionList>(PortfolioErrors.Invalid("status", "Use open, snoozed, done or all."));
        }

        if (request.AccountId is { } accountId && await accounts.FindReadOnlyAsync(accountId, cancellationToken) is null)
        {
            return Result.Failure<ActionList>(AccountErrors.NotFound);
        }

        var started = clock.GetTimestamp();
        var evaluation = await evaluator.EvaluateAsync(request.AccountId, totalsOnly: request.AccountId is null, cancellationToken);
        await reconciler.ClearStaleAsync(evaluation, "read", cancellationToken);

        var inScope = request.BucketId is { } bucket ? evaluation.Items.Where(i => i.BucketIds.Contains(bucket)).ToList() : [.. evaluation.Items];
        var holdings = request.BucketId is { } b ? evaluation.Holdings.Where(h => h.BucketId == b).ToList() : [.. evaluation.Holdings];
        var unavailable = request.BucketId is { } c ? evaluation.LaggingUnavailable.Count(h => h.BucketId == c) : evaluation.LaggingUnavailable.Count;
        var counts = CountsOf(inScope, unavailable, holdings.Count(h => !h.IsPriced));

        var items = inScope
            .Where(i => status == ActionStatusFilter.All || i.Item.State == status)
            .Select(i => i.Item)
            .ToList();
        var elapsedMs = (long)clock.GetElapsedTime(started).TotalMilliseconds;
        ActionsLog.Evaluated(logger, evaluation.Holdings.Count, evaluation.Items.Count, counts.Open, unavailable, elapsedMs);
        return Result.Success(new ActionList(items, counts));
    }

    private static ActionCounts CountsOf(IReadOnlyList<EvaluatedItem> items, int unavailable, int noPrice)
    {
        var open = items.Where(i => i.Item.State == ActionItemStates.Open).Select(i => i.Item).ToList();
        var byRule = ActionRuleNames.All.ToDictionary(r => r, r => open.Count(i => i.Rule == r));
        var bySeverity = ActionSeverities.All.ToDictionary(s => s, s => open.Count(i => i.Severity == s));
        return new ActionCounts(
            open.Count,
            items.Count(i => i.Item.State == ActionItemStates.Snoozed),
            items.Count(i => i.Item.State == ActionItemStates.Done),
            byRule,
            bySeverity,
            new ActionUnavailable(unavailable),
            noPrice);
    }

    /// <summary>The item state asked for, or <see cref="ActionStatusFilter.All"/>; null when the value is not one of the four.</summary>
    private static string? ParseStatus(string? status) => string.IsNullOrWhiteSpace(status)
        ? ActionItemStates.Open
        : status.Trim().ToLowerInvariant() switch
        {
            ActionItemStates.Open => ActionItemStates.Open,
            ActionItemStates.Snoozed => ActionItemStates.Snoozed,
            ActionItemStates.Done => ActionItemStates.Done,
            ActionStatusFilter.All => ActionStatusFilter.All,
            _ => null,
        };
}

/// <summary>The extra value the status filter has besides the three item states.</summary>
internal static class ActionStatusFilter
{
    public const string All = "all";
}
