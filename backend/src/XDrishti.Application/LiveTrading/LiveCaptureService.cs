using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The live capture as a service (<see cref="Id"/>, spec P11-01, ADR 0149): during the session of a trading day (09:10 to 15:40 India time, every <c>Live:Capture:PollSeconds</c> rounded up to whole minutes) it reads the owner's trade book and, when its
/// own switch is on, the order book at Dhan - GET only - and stores what it saw. It starts <b>paused</b> (<c>Live:Capture:Enabled</c> false); switching it on is the owner's act. It places no order, modifies none and cancels none. A rate limit or an outage is a
/// quiet retry (the health of the day shows a gap if it lasts); an expired token fails the run so the owner is told.
/// </summary>
public sealed class LiveCaptureService(IServiceScopeFactory scopes, ILiveCaptureSettings settings) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "live-capture";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Live capture",
        "During the session, reads your trade book (and, when its own switch is on, your order book) at Dhan, read only, and stores what you did: every fill and every order and what became of it. It places, changes and cancels nothing. It starts paused.",
        "xd-api",
        [ScheduleType.Interval],
        new ServiceSettings(false, Schedule.Interval(2, CaptureHealth.WindowStart, CaptureHealth.WindowEnd), 0, 5, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartAdvice = "Polls from 09:10 to 15:40 on trading days; the trade book is the day's trades, so a poll outside the day reads nothing new.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary>The first-start seed: <c>Live:Capture:Enabled</c> (default off) and the poll interval from <c>Live:Capture:PollSeconds</c>.</summary>
    public ServiceSettings? FirstStartSettings() =>
        Definition.Defaults with { Enabled = settings.Enabled, Schedule = Schedule.Interval(Math.Max(1, (settings.PollSeconds + 59) / 60), CaptureHealth.WindowStart, CaptureHealth.WindowEnd) };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CaptureLiveSessionCommand, LiveCaptureOutcome>>();
        var result = await handler.HandleAsync(new CaptureLiveSessionCommand(null, context.Trigger == ServiceRunTrigger.Manual), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(result.Error.Message);
        }

        var outcome = result.Value;
        if (outcome.Accounts == 0)
        {
            return ServiceOutcome.Skipped(outcome.Summary);
        }

        // After each poll (spec P11-04): the loss limits of the day and the week, from the fills just stored. It writes a halt row and nothing else; a failure of it never fails the poll.
        try
        {
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<EvaluateRampCommand, RampEvaluationOutcome>>().HandleAsync(new EvaluateRampCommand(null, HaltsOnly: true), cancellationToken);
        }
#pragma warning disable CA1031 // The halt check is a second step of the poll: the poll itself has succeeded.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            RampLog.EvaluationFailed(scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LiveCaptureService>>(), failure.GetType().Name);
        }

        return outcome.Summary.Contains(OutcomeCodes.RunTokenExpired, StringComparison.Ordinal) || outcome.Summary.Contains(OutcomeCodes.RunRejected, StringComparison.Ordinal)
            ? ServiceOutcome.Failed(outcome.Summary)
            : ServiceOutcome.Succeeded(outcome.Summary);
    }
}
