using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Lab;

/// <summary>
/// The evening hold-out run (<see cref="Id"/>, spec HO-01 D10, ADR 0283): after the end-of-day run it reads the chain and carries out the seal steps in order (the strategy auto look, then the auto release; CF-10e part 3 and SR-03 add the model and system steps). It is enabled at
/// first start (ADR 0282): it acts only through the auto settings <c>HoldOut:AutoUnlock</c> and <c>HoldOut:AutoRelease</c>, each of which fails closed, never claims block 1, never releases an unlooked block, and places no order. Latest only.
/// </summary>
public sealed class HoldOutEpochsService(IServiceScopeFactory scopes) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "holdout-epochs";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Hold-out epochs",
        "After the end-of-day run, reads the chain of hold-out blocks, takes the strategy look of a sealed block with a pre-dated, passing version when the auto look setting is ON (never block 1), and releases a sealed block that has a fresh look and no look still available when the auto release setting is ON (never an unlooked block). Fails closed on any read it cannot answer. It places no order.",
        "xd-api",
        [ScheduleType.After, ScheduleType.Daily],
        new ServiceSettings(true, Schedule.AfterService(EodService.Id), 0, 60, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            RecoveryClass = RecoveryClass.LatestOnly,
            MustFollow = [EodService.Id],
            NotBeforeSessionEnd = true,
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    public ServiceSettings? FirstStartSettings() => Definition.Defaults with { Enabled = true };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunHoldOutEpochsCommand, HoldOutEpochsOutcome>>();
        var result = await handler.HandleAsync(new RunHoldOutEpochsCommand(), cancellationToken);
        if (result.IsFailure)
        {
            return ServiceOutcome.Failed(Clip(result.Error.Message));
        }

        return result.Value.Failed > 0 ? ServiceOutcome.Failed(Clip(result.Value.Summary)) : ServiceOutcome.Succeeded(Clip(result.Value.Summary));
    }

    private static string Clip(string text) => text.Length <= ServiceRun.MaxMessageLength ? text : text[..(ServiceRun.MaxMessageLength - 1)] + "~";
}
