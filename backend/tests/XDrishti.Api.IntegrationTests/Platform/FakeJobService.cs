using System.Text.Json;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>A service of the tests (registered on a derived host): daily at 10:00 on trading days, one parameter, and a run the test can hold open or make fail.</summary>
public sealed class FakeJobService : IScheduledService
{
    public const string Id = "fake-job";

    public static readonly ServiceDefinition Definition = new(
        Id,
        "Fake job",
        "A service of the integration tests.",
        "xd-test",
        [ScheduleType.Daily, ScheduleType.Weekly, ScheduleType.After, ScheduleType.Window, ScheduleType.Interval, ScheduleType.Continuous],
        new ServiceSettings(true, Schedule.Daily(new TimeOnly(10, 0)), 0, 5, true, true, new Dictionary<string, JsonElement>()),
        [ParameterDefinition.Whole("lookback", "Look-back", 1, 30, 5)],
        new ServiceConstraints());

    private TaskCompletionSource<ServiceOutcome>? _gate;

    public int Runs { get; private set; }

    ServiceDefinition IScheduledService.Definition => Definition;

    /// <summary>What the next runs do; by default they succeed at once.</summary>
    public Func<ServiceOutcome> Outcome { get; set; } = () => ServiceOutcome.Succeeded();

    /// <summary>Makes runs wait until <see cref="Release"/>.</summary>
    public void Hold() => _gate = new TaskCompletionSource<ServiceOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => _gate?.TrySetResult(Outcome());

    public ServiceSettings? FirstStartSettings() => null;

    public async ValueTask<ServiceOutcome> RunAsync(ServiceRunContext context, CancellationToken cancellationToken)
    {
        Runs++;
        return _gate is { } gate ? await gate.Task.WaitAsync(cancellationToken) : Outcome();
    }
}
