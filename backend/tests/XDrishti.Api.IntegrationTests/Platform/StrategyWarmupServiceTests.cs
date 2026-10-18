using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Api.Infrastructure;
using XDrishti.Api.Platform;

namespace XDrishti.Api.IntegrationTests.Platform;

/// <summary>
/// Criterion 19 of P3-03a: the compiler warm-up starts when the host starts, on a thread-pool thread, without delaying readiness; it logs one line when it ends (information with the elapsed time from the clock, or a warning
/// that carries the exception); <c>StopAsync</c> does not wait for it. A fake warm-up gated on a signal stands in for the real one; nothing sleeps: the logger completes a task that the test awaits with a 30 s real-time limit.
/// </summary>
public sealed class StrategyWarmupServiceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private sealed record Line(LogLevel Level, EventId Id, string Message, Exception? Exception);

    /// <summary>Captures what the service logs and completes <see cref="Logged"/> after the first line.</summary>
    private sealed class CapturingLogger : ILogger<StrategyWarmupService>
    {
        private readonly List<Line> _lines = [];

        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<Line> Lines
        {
            get
            {
                lock (_lines)
                {
                    return [.. _lines];
                }
            }
        }

        public Task Logged => _logged.Task;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lines)
            {
                _lines.Add(new Line(logLevel, eventId, formatter(state, exception), exception));
            }

            _logged.TrySetResult();
        }
    }

    private static StrategyWarmupService Service(FakeTimeProvider clock, CapturingLogger logger, Action warmUp) => new(clock, logger, warmUp);

    [Fact]
    public async Task Start_returns_before_the_warm_up_ends_runs_it_on_another_thread_and_logs_one_information_line_with_the_elapsed_time()
    {
        var clock = new FakeTimeProvider();
        var logger = new CapturingLogger();
        var gate = new ManualResetEventSlim();
        var started = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service(clock, logger, () =>
        {
            started.SetResult(Environment.CurrentManagedThreadId);
            gate.Wait(Limit, CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(42));
        });

        var start = service.StartAsync(TestContext.Current.CancellationToken);

        start.IsCompletedSuccessfully.ShouldBeTrue("readiness is not delayed");
        (await started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken)).ShouldNotBe(Environment.CurrentManagedThreadId);
        logger.Lines.ShouldBeEmpty("the warm-up has not ended yet");

        gate.Set();
        await logger.Logged.WaitAsync(Limit, TestContext.Current.CancellationToken);

        var line = logger.Lines.ShouldHaveSingleItem();
        (line.Level, line.Id.Id).ShouldBe((LogLevel.Information, 4401));
        line.Message.ShouldBe("compiler.warm_up elapsed_ms=42");
        line.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task A_warm_up_that_throws_is_a_warning_that_carries_the_exception_and_nothing_escapes()
    {
        var logger = new CapturingLogger();
        var failure = new InvalidOperationException("the warm-up broke");
        var service = Service(new FakeTimeProvider(), logger, () => throw failure);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await logger.Logged.WaitAsync(Limit, TestContext.Current.CancellationToken);

        var line = logger.Lines.ShouldHaveSingleItem();
        (line.Level, line.Id.Id).ShouldBe((LogLevel.Warning, 4401));
        line.Exception.ShouldBeSameAs(failure);
    }

    [Fact]
    public async Task Stop_returns_at_once_while_the_warm_up_is_still_running()
    {
        var logger = new CapturingLogger();
        var gate = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Service(new FakeTimeProvider(), logger, () =>
        {
            started.SetResult();
            gate.Wait(Limit, CancellationToken.None);
        });
        await service.StartAsync(TestContext.Current.CancellationToken);
        await started.Task.WaitAsync(Limit, TestContext.Current.CancellationToken);

        var stop = service.StopAsync(TestContext.Current.CancellationToken);

        stop.IsCompletedSuccessfully.ShouldBeTrue();
        logger.Lines.ShouldBeEmpty();

        gate.Set();
        await logger.Logged.WaitAsync(Limit, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_registration_of_the_api_runs_the_real_compiler_warm_up()
    {
        var services = new ServiceCollection();
        services.AddApi();
        var registration = services.Single(d => d.ServiceType == typeof(IHostedService) && d.ImplementationFactory is not null);
        var logger = new CapturingLogger();
        var provider = new ServiceCollection().AddSingleton<TimeProvider>(TimeProvider.System).AddSingleton<ILogger<StrategyWarmupService>>(logger).BuildServiceProvider();
        var hosted = (IHostedService)registration.ImplementationFactory!(provider);

        await hosted.StartAsync(TestContext.Current.CancellationToken);
        await logger.Logged.WaitAsync(Limit, TestContext.Current.CancellationToken);

        hosted.ShouldBeOfType<StrategyWarmupService>();
        var line = logger.Lines.ShouldHaveSingleItem();
        (line.Level, line.Id.Id).ShouldBe((LogLevel.Information, 4401), "the real warm-up ran without throwing");
    }
}
