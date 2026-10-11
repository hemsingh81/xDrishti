using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Market.Refresh;

/// <summary>The refresh in progress, the last finished one, the automatic schedule and the number of active instruments.</summary>
public sealed record GetRefreshStatusQuery : IQuery<RefreshStatusView>;

internal sealed class GetRefreshStatusHandler(
    IRefreshRunStore runs,
    IInstrumentCatalog catalog,
    IInstrumentRefreshSettings settings,
    TimeProvider clock) : IQueryHandler<GetRefreshStatusQuery, RefreshStatusView>
{
    public async ValueTask<RefreshStatusView> HandleAsync(GetRefreshStatusQuery query, CancellationToken cancellationToken)
    {
        var running = await runs.GetRunningAsync(cancellationToken);
        var last = await runs.GetLastFinishedAsync(cancellationToken);
        var active = await catalog.CountActiveAsync(cancellationToken);
        DateTimeOffset? next = settings.AutoRefreshEnabled ? RefreshSchedule.NextRun(clock.GetUtcNow(), settings.AutoRefreshTimeIst) : null;

        return new RefreshStatusView(
            running is null ? null : RefreshRunView.From(running),
            last is null ? null : RefreshRunView.From(last),
            new AutoRefreshView(settings.AutoRefreshEnabled, settings.AutoRefreshTimeIst.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), next),
            active);
    }
}
