using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;

namespace XDrishti.Application.Market.Refresh;

/// <summary>The refresh in progress, the last finished one, the automatic schedule (as set on the Services screen) and the number of active instruments.</summary>
public sealed record GetRefreshStatusQuery : IQuery<RefreshStatusView>;

internal sealed class GetRefreshStatusHandler(
    IRefreshRunStore runs,
    IInstrumentCatalog catalog,
    IServiceScheduleReader schedules) : IQueryHandler<GetRefreshStatusQuery, RefreshStatusView>
{
    public async ValueTask<RefreshStatusView> HandleAsync(GetRefreshStatusQuery query, CancellationToken cancellationToken)
    {
        var running = await runs.GetRunningAsync(cancellationToken);
        var last = await runs.GetLastFinishedAsync(cancellationToken);
        var active = await catalog.CountActiveAsync(cancellationToken);
        var schedule = await schedules.GetAsync(InstrumentMasterService.Id, cancellationToken);

        return new RefreshStatusView(
            running is null ? null : RefreshRunView.From(running),
            last is null ? null : RefreshRunView.From(last),
            new AutoRefreshView(schedule?.Enabled ?? false, schedule?.TimeIst?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty, schedule?.NextRunAt),
            active);
    }
}
