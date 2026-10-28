using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>Reads the live switches with their history. Read only; no row means both OFF.</summary>
internal sealed class GetLiveSettingsHandler(ILiveSettingsStore store, ILiveWritesGate gate) : IQueryHandler<GetLiveSettingsQuery, LiveSettingsView>
{
    public const int HistoryLimit = 50;

    public async ValueTask<LiveSettingsView> HandleAsync(GetLiveSettingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return View(await store.CurrentAsync(cancellationToken), await store.HistoryAsync(HistoryLimit, cancellationToken), gate.Enabled);
    }

    internal static LiveSettingsView View(LiveSettings settings, IReadOnlyList<StoredLiveSettings> history, bool writesEnabled) =>
        new(settings.Id, settings.AutoApproveTickets, settings.AutoRampUp, writesEnabled, LiveNotice.NoOrder, [.. history.Select(h => new LiveSettingsChangeView(h.Id, h.AutoApproveTickets, h.AutoRampUp, h.Actor, h.Reason, h.ChangedAt))]);
}
