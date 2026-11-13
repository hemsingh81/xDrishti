using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The weekly copy of the shadow chain digest outside the database (<see cref="Id"/>, spec CF-08 review fix 4, ADR 0232): Saturday 11:00 India time it appends the newest session and digest to a log file in the folder set by <c>Shadow:DigestFolder</c>.
/// It starts <b>enabled</b> (ADR 0282) and exports nothing while the switch <c>paper.shadow.enabled</c> is OFF. It writes no database row and places no order.
/// </summary>
public sealed class ShadowDigestService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "shadow-digest";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Shadow digest copy",
        "Saturday 11:00, appends the newest session and chain digest of the shadow record to a log file outside the database (setting Shadow:DigestFolder), after verifying that the chain recomputes; a broken chain is not copied and the run fails. Nothing is exported while the setting paper.shadow.enabled is OFF (it is ON by default, ADR 0282; with it ON the first run also probes the digest folder, so a folder that cannot be written shows as a failed run). It writes no database row and places no order.",
        "xd-api",
        [ScheduleType.Weekly],
        new ServiceSettings(true, Schedule.Weekly(DayOfWeek.Saturday, new TimeOnly(11, 0)), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints { RecoveryClass = RecoveryClass.LatestOnly, CatchUpReach = CatchUpReach.SameDay });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ExportShadowDigestCommand, ShadowDigestExported>>();
        var result = await handler.HandleAsync(new ExportShadowDigestCommand(), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
