using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Planning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Shadow;

/// <summary>
/// The shadow writer as a service (<see cref="Id"/>, spec CF-08, ADR 0211): every trading day at 19:00, after the nightly plan and well before the 09:00 cut-off, it carries out <see cref="WriteShadowCommand"/>: every candidate of the stored plan becomes a row of the
/// shadow record, written once and stamped by the database. It starts <b>paused</b>, and even when switched on it writes nothing until the owner's setting <c>paper.shadow.enabled</c> (default OFF) is ON. It places no order, calls no broker, moves no ticket.
/// </summary>
public sealed class ShadowWriteService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "shadow-write";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Shadow record",
        "At 19:00, after the nightly plan, records every candidate of the plan (primary, reserve and rejected) before the session opens, so that it can be graded after the close as edge evidence. It writes only when the setting paper.shadow.enabled is ON (it is OFF by default), never after 09:00 of the session, and never back-fills a night it missed. The record is simulated: no order was placed and none exists.",
        "xd-api",
        [ScheduleType.Daily],
        new ServiceSettings(false, Schedule.Daily(new TimeOnly(19, 0)), 0, 10, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.TimeBound,
            MustFollow = [NightlyService.Id],
            StartNotBefore = new TimeOnly(18, 30),
            StartAdvice = "It must start after the nightly plan (18:30) has stored the plan; a night it misses is recorded as a gap and never filled in after the open.",
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<WriteShadowCommand, ShadowWriteOutcome>>();
        var result = await handler.HandleAsync(new WriteShadowCommand(null), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
