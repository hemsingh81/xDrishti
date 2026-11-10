using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.SettingsCatalog;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The scope in force (spec AU-16). Read only.</summary>
internal sealed class GetAutoScopeHandler(IAutoScopeStore store) : IQueryHandler<GetAutoScopeQuery, AutoScopeView>
{
    public async ValueTask<AutoScopeView> HandleAsync(GetAutoScopeQuery query, CancellationToken cancellationToken) =>
        AutoScopeViews.Of(await store.LatestAsync(cancellationToken));
}

internal static class AutoScopeViews
{
    public const string NoScope = "eligible";

    public static AutoScopeView Of(StoredAutoScope? s) =>
        s is null
            ? new AutoScopeView(false, null, [], [], null, null, null, NoScope)
            : new AutoScopeView(true, s.Id, s.BasketIds, s.InstrumentIds, s.Hash, s.CreatedBy, s.CreatedAt, string.Create(CultureInfo.InvariantCulture, $"{s.BasketIds.Count} baskets, {s.InstrumentIds.Count} stocks"));
}

/// <summary>Sets the scope as the owner (spec AU-16): baskets and stocks, at least one, every id known. Appends a version and an audit row.</summary>
internal sealed class SetAutoScopeHandler(
    IAutoScopeStore store,
    IBasketQueries baskets,
    IInstrumentQueries instruments,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<SetAutoScopeHandler> logger) : ICommandHandler<SetAutoScopeCommand, AutoScopeView>
{
    public const string UnknownCode = "auto.scope_unknown";

    public async ValueTask<Result<AutoScopeView>> HandleAsync(SetAutoScopeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EssentialAudit.IsOwner(command.Actor))
        {
            return Result.Failure<AutoScopeView>(Error.Forbidden("auto.owner_only", "Only the signed-in owner sets the scope."));
        }

        var scope = AutoScope.Create(command.BasketIds, command.InstrumentIds);
        if (scope.IsFailure)
        {
            return Result.Failure<AutoScopeView>(scope.Error);
        }

        var unknown = new List<FieldError>();
        foreach (var id in scope.Value.BasketIds)
        {
            if (await baskets.GetAsync(id, cancellationToken) is null)
            {
                unknown.Add(new FieldError("basketIds", $"No basket {id}.", Code: UnknownCode));
            }
        }

        foreach (var id in scope.Value.InstrumentIds)
        {
            if (await instruments.GetAsync(id, cancellationToken) is null)
            {
                unknown.Add(new FieldError("instrumentIds", $"No instrument {id}.", Code: UnknownCode));
            }
        }

        if (unknown.Count > 0)
        {
            return Result.Failure<AutoScopeView>(new Error(UnknownCode, "The scope names an id that does not exist: " + string.Join(' ', unknown.Select(f => f.Message)), ErrorKind.Validation, Fields: unknown));
        }

        var now = clock.GetUtcNow();
        var stored = await store.AppendAsync(scope.Value, command.Actor, now, cancellationToken);
        await EssentialAudit.RecordAsync(events, unitOfWork, AccountEventType.EssentialSettingChanged, string.Create(CultureInfo.InvariantCulture, $"suggest.scope={stored.Hash[..12]} baskets={stored.BasketIds.Count} stocks={stored.InstrumentIds.Count}"), command.Actor, now, logger);
        return Result.Success(AutoScopeViews.Of(stored));
    }
}

/// <summary>The suggestions of the newest plan of a date (spec AU-16): what, why and on what evidence. Read only; creates no approval, no ticket move and no order.</summary>
internal sealed class GetAutoSuggestionsHandler(IPlanStore plans) : IQueryHandler<GetAutoSuggestionsQuery, Result<AutoSuggestionsView>>
{
    public const string NoPlanCode = "auto.no_plan";

    public const string NightFailedCode = "auto.night_failed";

    public async ValueTask<Result<AutoSuggestionsView>> HandleAsync(GetAutoSuggestionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var newest = await plans.ListAsync(query.ForDate, null, 1, cancellationToken);
        var plan = newest.Count == 0 ? null : newest[0];
        var runs = await plans.ListRunsAsync(query.ForDate, 1, cancellationToken);
        if (runs.Count > 0 && runs[0].Run.Outcome == NightlyOutcomes.FailedClosed && (plan is null || plan.Plan.ForDate < runs[0].Run.ForDate))
        {
            // a night that failed closed published nothing: an older plan is not shown as if it were the suggestions of that night (AU-17)
            var failed = runs[0].Run;
            return Result.Failure<AutoSuggestionsView>(Error.Conflict(NightFailedCode, $"The nightly run for {failed.ForDate:yyyy-MM-dd} failed closed{(failed.FailedGate is null ? string.Empty : " at " + failed.FailedGate)}: {failed.Reason} No suggestions are shown."));
        }

        if (plan is null)
        {
            return Result.Failure<AutoSuggestionsView>(Error.NotFound(NoPlanCode, query.ForDate is { } d ? $"No plan is recorded for {d:yyyy-MM-dd}." : "No plan is recorded."));
        }

        var suggestions = new List<AutoSuggestionView>();
        var notSuggested = new List<AutoSuggestionView>();
        var reserve = new List<AutoSuggestionView>();
        var rejected = new List<AutoSuggestionView>();
        foreach (var row in await plans.ItemsAsync(plan.Id, cancellationToken))
        {
            var item = row.Item;
            var (figures, missing) = AutoSuggestionRules.Evidence(item.Evidence);
            var reasons = item.Group == TicketGroup.Primary ? [AutoSuggestionRules.ChosenInWords(item.Rank)] : item.Reasons.Select(AutoSuggestionRules.ReasonInWords).ToList();
            var levels = PlanItemView.Of(row).Levels;
            AutoSuggestionView Make(string? why) => new(row.Id, item.Rank, item.Symbol, item.Side, item.StrategyId, item.StrategyVersionHash, levels, reasons, figures, why);
            switch (item.Group)
            {
                case TicketGroup.Primary when missing.Count == 0:
                    suggestions.Add(Make(null));
                    break;
                case TicketGroup.Primary:
                    notSuggested.Add(Make("Not suggested: " + string.Join(", ", missing) + " unavailable."));
                    break;
                case TicketGroup.Reserve:
                    reserve.Add(Make(null));
                    break;
                default:
                    rejected.Add(Make(null));
                    break;
            }
        }

        return Result.Success(new AutoSuggestionsView(plan.Plan.ForDate, plan.Id, plan.State.Code(), plan.Plan.Basis, AutoSuggestionRules.NoOrderNotice, plan.Plan.BasisFlags?.ScopeHash, suggestions, notSuggested, reserve, rejected));
    }
}
