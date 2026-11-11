using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Market.History;

/// <summary>
/// The 1-minute history backfill as a service (<see cref="Id"/>, CF-04 Part B). It starts <b>paused</b>; with <c>Data:History:Backfill:Enabled</c> off (the default) a run makes no request and ends as skipped; outside the run window (default 20:00 to 06:00 India time) it ends as
/// skipped too. Recovery class <b>Convergent</b> (ADR 0240): the chunk ledger skips what is done, so one run closes every missing chunk and a missed run is made up by the next. While it works the ledger shows planned chunks, which the AU-12 entry gate reads
/// (<see cref="IBackfillProgress"/>), so the simulation job waits for it. A failed chunk never fails the run.
/// </summary>
public sealed class HistoryBackfillService(IServiceScopeFactory scopes, IBackfillSettings settings, TimeProvider clock) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "history-backfill";

    public const string DisabledReason = "disabled: Data:History:Backfill:Enabled is off";

    public const string OutsideWindowReason = "outside the run window (Data:History:Backfill:RunWindow)";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "1-minute history backfill",
        "Fetches Dhan's 1-minute bars for the years before each instrument's first CSV bar, in chunks of at most 90 days, newest first, resumable, off-hours. Writes only before the first CSV bar and never replaces your CSV bars. Off until you enable Data:History:Backfill:Enabled; with the switch off a run makes no request.",
        "xd-api",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(22, 0)), 0, 240, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.Convergent, CatchUpReach = CatchUpReach.SameDay });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    /// <summary>True when the India time of <paramref name="now"/> lies inside <c>[from, to)</c>; a window with <c>from &gt; to</c> crosses midnight, and one with <c>from == to</c> is the whole day.</summary>
    public static bool InWindow(DateTimeOffset now, TimeOnly from, TimeOnly to)
    {
        var time = TimeOnly.FromDateTime(IndiaTime.ToIst(now).DateTime);
        return from == to || (from < to ? time >= from && time < to : time >= from || time < to);
    }

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!settings.Enabled)
        {
            return ServiceOutcome.Skipped(DisabledReason);
        }

        if (!InWindow(clock.GetUtcNow(), settings.RunWindowFrom, settings.RunWindowTo))
        {
            return ServiceOutcome.Skipped(OutsideWindowReason);
        }

        await using var scope = scopes.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<RunBackfillCommand, BackfillSummary>>().HandleAsync(new RunBackfillCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Code + ": " + result.Error.Message);
        }

        return result.Value.Stopped is not null ? ServiceOutcome.Failed(result.Value.Text) : ServiceOutcome.Succeeded(result.Value.Text);
    }
}
