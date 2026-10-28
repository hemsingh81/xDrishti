using System.Globalization;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Checklist;

namespace XDrishti.Application.Autonomy;

/// <summary>
/// The checklist of a stored ticket, read from what the plan stored (spec P7-10, ADR 0095): for each account the ticket is sized for, the checks are rebuilt by <see cref="StoredChecklist"/> from the
/// <c>checklist:{code}:{outcome}</c> standings. A ticket is read only if the wiring marked it (<see cref="PlanWiring.IndicativeEntry"/>: the allocation and the checklist ran for it), has at least one allocation and every standing is readable;
/// otherwise the answer is EMPTY, which neither the owner's review nor the auto-approval rule can approve (a missing checklist is not a pass). Pure over the row: no I/O.
/// </summary>
internal sealed class StoredChecklistSource : IChecklistSource
{
    public ValueTask<IReadOnlyList<ChecklistResult>> ForTicketAsync(PlanItemRow item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var stored = item.Item.Evidence.Unavailable;
        if (item.Item.Allocations.Count == 0 || !stored.Contains(PlanWiring.IndicativeEntry, StringComparer.Ordinal))
        {
            return ValueTask.FromResult<IReadOnlyList<ChecklistResult>>([]);
        }

        var results = new List<ChecklistResult>(item.Item.Allocations.Count);
        foreach (var allocation in item.Item.Allocations)
        {
            var result = StoredChecklist.Build(allocation.AccountId.ToString("D", CultureInfo.InvariantCulture), item.Item.Symbol, string.Equals(allocation.Mode, "paper", StringComparison.Ordinal), stored, liveMode: string.Equals(allocation.Mode, "manual_live", StringComparison.Ordinal));
            if (result is null)
            {
                return ValueTask.FromResult<IReadOnlyList<ChecklistResult>>([]);
            }

            results.Add(result);
        }

        return ValueTask.FromResult<IReadOnlyList<ChecklistResult>>(results);
    }
}
