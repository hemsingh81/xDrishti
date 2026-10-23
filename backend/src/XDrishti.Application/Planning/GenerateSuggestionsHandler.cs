using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>
/// Reads the newest stored plan (or the one for a date), runs <see cref="SuggestionRules"/> and appends the suggestions that are new for the plan, kind and subject (spec P7-09, ADR 0092). Reads plans and cell statistics, inserts suggestions;
/// no ticket state changes, no approval, no order.
/// </summary>
internal sealed class GenerateSuggestionsHandler(
    IPlanStore plans,
    ISuggestionStore suggestions,
    ICellStatsStore cellStats,
    TimeProvider time,
    ILogger<GenerateSuggestionsHandler> logger) : ICommandHandler<GenerateSuggestionsCommand, SuggestionsOutcome>
{
    public async ValueTask<Result<SuggestionsOutcome>> HandleAsync(GenerateSuggestionsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var found = await plans.ListAsync(command.ForDate, null, 1, cancellationToken);
        if (found.Count == 0)
        {
            PlanWiringLog.NoPlan(logger, command.ForDate);
            return Result.Success(new SuggestionsOutcome(null, command.ForDate, 0, 0, []));
        }

        var plan = found[0];
        var items = await plans.ItemsAsync(plan.Id, cancellationToken);
        var statsAsOf = await cellStats.NewestAsOfAsync(cancellationToken);
        var produced = SuggestionRules.Evaluate(new SuggestionInput(
            plan.Plan.ForDate,
            plan.Plan.Basis,
            [.. items.Select(i => new SuggestionItem(i.Item.Group, i.Item.Symbol, i.Item.Reasons))],
            statsAsOf));
        var stored = await suggestions.InsertAsync(plan.Id, produced, time.GetUtcNow(), cancellationToken);
        PlanWiringLog.Generated(logger, plan.Id, plan.Plan.ForDate, produced.Count, stored);
        return Result.Success(new SuggestionsOutcome(plan.Id, plan.Plan.ForDate, produced.Count, stored, produced));
    }
}
