using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>The suggestions inbox of a plan with the owner's seen marks (spec P7-10, ADR 0095). Read only.</summary>
internal sealed class ListSuggestionsHandler(IPlanStore plans, ISuggestionStore suggestions) : IQueryHandler<ListSuggestionsQuery, Result<IReadOnlyList<SuggestionView>>>
{
    public async ValueTask<Result<IReadOnlyList<SuggestionView>>> HandleAsync(ListSuggestionsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await plans.FindAsync(query.PlanId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<SuggestionView>>(PlanErrors.NotFound(query.PlanId));
        }

        var stored = await suggestions.ForPlanAsync(query.PlanId, cancellationToken);
        var seen = (await suggestions.SeenIdsAsync(query.PlanId, cancellationToken)).ToHashSet();
        return Result.Success<IReadOnlyList<SuggestionView>>([.. stored.Select(s => new SuggestionView(s.Id, s.Suggestion.Kind, s.Suggestion.Subject, s.Suggestion.Text, s.CreatedAt, seen.Contains(s.Id)))]);
    }
}

/// <summary>The owner marks a suggestion as seen (spec P7-10): one insert-only row per suggestion, repeated marks change nothing. It touches no plan and no ticket.</summary>
internal sealed class MarkSuggestionSeenHandler(ISuggestionStore suggestions, TimeProvider time, ILogger<MarkSuggestionSeenHandler> logger) : ICommandHandler<MarkSuggestionSeenCommand>
{
    public async ValueTask<Result> HandleAsync(MarkSuggestionSeenCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (ReviewLookup.OwnerGuard(command.Actor) is { } notOwner)
        {
            ReviewLog.Refused(logger, notOwner.Code);
            return Result.Failure(notOwner);
        }

        if (!await suggestions.MarkSeenAsync(command.PlanId, command.SuggestionId, command.Actor, time.GetUtcNow(), cancellationToken))
        {
            ReviewLog.Refused(logger, "plans.suggestion_not_found");
            return Result.Failure(Error.NotFound("plans.suggestion_not_found", $"No suggestion {command.SuggestionId} on plan {command.PlanId}."));
        }

        ReviewLog.SuggestionSeen(logger, command.SuggestionId);
        return Result.Success();
    }
}
