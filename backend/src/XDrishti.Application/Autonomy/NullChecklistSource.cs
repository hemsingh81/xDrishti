using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Checklist;

namespace XDrishti.Application.Autonomy;

/// <summary>No checklist inputs are wired yet (P7-10): every ticket has no checklist, so the auto-approval rule always declines it (unavailable never passes).</summary>
internal sealed class NullChecklistSource : IChecklistSource
{
    public ValueTask<IReadOnlyList<ChecklistResult>> ForTicketAsync(PlanItemRow item, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ChecklistResult>>([]);
}
