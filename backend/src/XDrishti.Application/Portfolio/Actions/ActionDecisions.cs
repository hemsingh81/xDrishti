using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>
/// What snoozing, finishing and reopening an item have in common (ADR 0022). The item must be one the rules produce now (a stale id is <c>portfolio.not_found</c>), except for a reopen, which may clear a decision about an item that is gone. A decision
/// that is already the stored one changes and records nothing, so every command is idempotent. A change is saved together with its audit event (who, rule and holding or account; no figures).
/// </summary>
internal sealed class ActionDecisions(
    ActionEvaluator evaluator,
    IActionStates states,
    IAccountEventLog events,
    IPortfolioUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ActionDecisions> logger)
{
    /// <summary>A snooze lasts this many days (India date arithmetic): the item shows again on that date.</summary>
    public const int SnoozeDays = 7;

    public async ValueTask<Result<ActionDecisionView>> DecideAsync(string id, ActionDecision decision, string actor, CancellationToken cancellationToken)
    {
        if (ActionKey.Parse(id) is not { } key)
        {
            return Result.Failure<ActionDecisionView>(PortfolioErrors.ActionNotFound);
        }

        var evaluation = await evaluator.EvaluateAsync(null, false, cancellationToken);
        var produced = evaluation.Items.FirstOrDefault(i => i.Key == key)?.Item;
        if (produced is null && decision != ActionDecision.Cleared)
        {
            return Result.Failure<ActionDecisionView>(PortfolioErrors.ActionNotFound);
        }

        var now = clock.GetUtcNow();
        var state = StateOf(decision);
        DateOnly? until = decision == ActionDecision.Snoozed ? IndiaTime.Today(now).AddDays(SnoozeDays) : null;
        if (await states.SetAsync(key.Rule, key.Key, decision, until, actor, now, cancellationToken))
        {
            events.Record(AccountEvent.Record(EventOf(decision), produced?.AccountId ?? key.AccountId, Describe(decision, key, produced, until), actor, now));
            if (await unitOfWork.TrySaveAsync(cancellationToken))
            {
                ActionsLog.Decided(logger, state, key.Rule);
            }
            else if (!await StoredAsync(key, decision, until, cancellationToken))
            {
                // The save collided with a concurrent change (a second request inserted the same decision first). If what is stored now is the decision that was asked for, the request is done — it is idempotent; otherwise it is a conflict.
                return Result.Failure<ActionDecisionView>(PortfolioErrors.ActionChanged);
            }
        }

        return Result.Success(new ActionDecisionView(key.Id, state, until, produced is null ? null : produced with { State = state, SnoozedUntil = until }));
    }

    /// <summary>True when the decision stored now is the one asked for (for a reopen: when none applies any more).</summary>
    private async ValueTask<bool> StoredAsync(ActionKey key, ActionDecision decision, DateOnly? until, CancellationToken cancellationToken)
    {
        var stored = (await states.ListLiveAsync(cancellationToken)).FirstOrDefault(s => s.Rule == key.Rule && s.Key == key.Key);
        return decision switch
        {
            ActionDecision.Cleared => stored is null,
            ActionDecision.Done => stored is { State: ActionDecision.Done },
            _ => stored is { State: ActionDecision.Snoozed } && stored.SnoozedUntil == until,
        };
    }

    private static string StateOf(ActionDecision decision) => decision switch
    {
        ActionDecision.Snoozed => ActionItemStates.Snoozed,
        ActionDecision.Done => ActionItemStates.Done,
        _ => ActionItemStates.Open,
    };

    private static AccountEventType EventOf(ActionDecision decision) => decision switch
    {
        ActionDecision.Snoozed => AccountEventType.ActionSnoozed,
        ActionDecision.Done => AccountEventType.ActionDone,
        _ => AccountEventType.ActionReopened,
    };

    /// <summary>"Snoozed Below cost on INFY until 2026-10-20" · "Marked done Sector concentration (Banks) in Main account" · "Reopened below_cost:…" (an item that is gone is named by its id).</summary>
    private static string Describe(ActionDecision decision, ActionKey key, ActionItem? item, DateOnly? until)
    {
        var verb = decision switch
        {
            ActionDecision.Snoozed => "Snoozed",
            ActionDecision.Done => "Marked done",
            _ => "Reopened",
        };
        var what = item is null
            ? key.Id
            : item.Symbol is { } symbol
                ? $"{item.Title} on {symbol}"
                : $"{item.Title} ({item.Numbers.Sector}) in {item.AccountName}";
        return until is { } date ? $"{verb} {what} until {date:yyyy-MM-dd}" : $"{verb} {what}";
    }
}
