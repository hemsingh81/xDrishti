using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Autonomy;

/// <summary>Reads the autonomy settings in force and the latest changes. Read only.</summary>
internal sealed class GetAutonomySettingsHandler(IAutonomySettingsStore store) : IQueryHandler<GetAutonomySettingsQuery, AutonomySettingsView>
{
    internal const int HistoryLimit = 50;

    public async ValueTask<AutonomySettingsView> HandleAsync(GetAutonomySettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var current = await store.CurrentAsync(cancellationToken);
        var history = await store.HistoryAsync(HistoryLimit, cancellationToken);
        return View(current, history);
    }

    internal static AutonomySettingsView View(Domain.Autonomy.AutonomySettings current, IReadOnlyList<AutonomyChangeRow> history) =>
        new(current.Id, current.Hash, current.AutoApproveTickets, current.PromoteFromReserve, current.MinPWin, current.MaxAutoPerDay, PlanWording.NoOrder,
            [.. history.Select(h => new AutonomyChangeView(h.Id, h.Hash, h.AutoApproveTickets, h.PromoteFromReserve, h.MinPWin, h.MaxAutoPerDay, h.Actor, h.Reason, h.ChangedAt))]);
}
