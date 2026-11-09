using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Learning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.LearnedSettings;

/// <summary>
/// The learned-settings service (<see cref="Id"/>, AU-10, ADR 0242): after the weekly digest it lets learning propose bounded changes to a small allow-list of settings. A tightening is applied by itself only when the Auto switch is ON (off by default) and
/// the weekly cap allows; a loosening is only ever a proposal for the owner's password. It starts <b>paused</b>, and with no evidence it changes nothing. It places no order.
/// </summary>
public sealed class LearnedSettingsService(IServiceScopeFactory scopes) : IScheduledService
{
    public const string Id = "learned-settings";

    public const string Requester = "system:learned-settings";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Learned setting changes",
        "After the weekly digest, proposes bounded changes to a small allow-list of settings from what was learned. A change toward the safer side is applied by itself only when Auto is on; anything else waits for your password. With no evidence nothing changes.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Weekly],
        new ServiceSettings(false, Schedule.AfterService(WeeklyService.Id), 0, 30, false, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.LatestOnly,
            MustFollow = [WeeklyService.Id],
            StartAdvice = "Run it after the weekly digest. It needs measured evidence; without it nothing changes.",
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunLearnedSettingsCommand, LearnedRunView>>();
        var manual = context.Trigger == ServiceRunTrigger.Manual;
        var result = await handler.HandleAsync(new RunLearnedSettingsCommand(manual ? context.RequestedBy : Requester), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Outcome);
    }
}
