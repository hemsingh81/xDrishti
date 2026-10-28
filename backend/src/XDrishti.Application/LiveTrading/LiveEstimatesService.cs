using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The weekly estimates of the execution feedback as a service (<see cref="Id"/>, spec P11-02, ADR 0150): slippage per liquidity bucket and the capacity score, one snapshot per week. It starts <b>paused</b>, reads stored rows only, writes insert-only rows, applies
/// nothing to the simulator or the selection, and places no order.
/// </summary>
public sealed class LiveEstimatesService(IServiceScopeFactory scopes, ILiveEstimateSettings settings) : IScheduledService
{
    /// <summary>The id of the service (part of the API and of the stored row: never rename it).</summary>
    public const string Id = "live-estimates";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Live estimates",
        "Once a week, measures how far your live entries landed from the plan: slippage per liquidity bucket (with how many trades each rests on) and the share of your orders that filled in full. It only stores the numbers for you to read; nothing is changed from them. It starts paused.",
        "xd-api",
        [ScheduleType.Weekly],
        new ServiceSettings(false, Schedule.Weekly(DayOfWeek.Saturday, new TimeOnly(10, 0)), 0, 30, true, true, new Dictionary<string, System.Text.Json.JsonElement>()),
        [],
        new ServiceConstraints
        {
            StartAdvice = "It reads the executions that the evening live settle stores; with few live trades most buckets stay 'insufficient' for weeks.",
            CatchUpReach = CatchUpReach.SameDay,
        });

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary><c>Live:Estimates:Enabled</c> (default off) seeds this service at the first start.</summary>
    public ServiceSettings? FirstStartSettings() => Definition.Defaults with { Enabled = settings.Enabled };

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ComputeLiveEstimatesCommand, LiveEstimatesOutcome>>();
        var result = await handler.HandleAsync(new ComputeLiveEstimatesCommand(null), cancellationToken);
        return result.IsFailure ? ServiceOutcome.Failed(result.Error.Message) : ServiceOutcome.Succeeded(result.Value.Summary);
    }
}
