using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Application.Market.Context;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Context;

/// <summary>The post-step of the data job never fails the job: a crash is one generic warning, an ended job is left alone.</summary>
public sealed class ContextUpkeepTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class ThrowingRefresh(Exception failure) : ICommandHandler<RefreshContextCommand, ContextRefreshed>
    {
        public ValueTask<Result<ContextRefreshed>> HandleAsync(RefreshContextCommand command, CancellationToken cancellationToken) => throw failure;
    }

    private sealed class FailingRefresh : ICommandHandler<RefreshContextCommand, ContextRefreshed>
    {
        public ValueTask<Result<ContextRefreshed>> HandleAsync(RefreshContextCommand command, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result.Failure<ContextRefreshed>(Error.Validation("context.broken", "The context is broken.")));
    }

    private static (JobReporter Reporter, DataJob Job) Reporter()
    {
        var jobs = new FakeDataJobStore();
        var job = DataJob.Start(["aggregate"], false, "test", new DateTimeOffset(2026, 3, 9, 16, 0, 0, TimeSpan.Zero));
        jobs.Jobs[job.Id] = job;
        return (new JobReporter(job, jobs, new FakeTimeProvider(DateTimeOffset.UnixEpoch)), job);
    }

    [Fact]
    public async Task A_crash_is_a_generic_warning_that_leaks_nothing_and_the_job_goes_on()
    {
        var (reporter, job) = Reporter();

        await new ContextUpkeep(new ThrowingRefresh(new InvalidOperationException("password=hunter2 leaked")), NullLogger<ContextUpkeep>.Instance).RunAsync(reporter, Ct);

        job.Warnings.ShouldBe(["The market context could not be updated. See the API log."]);
    }

    [Fact]
    public async Task A_failed_result_is_a_warning_with_the_message()
    {
        var (reporter, job) = Reporter();

        await new ContextUpkeep(new FailingRefresh(), NullLogger<ContextUpkeep>.Instance).RunAsync(reporter, Ct);

        job.Warnings.ShouldBe(["Market context: The context is broken."]);
    }

    [Fact]
    public async Task A_cancellation_is_not_swallowed()
    {
        var (reporter, _) = Reporter();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await new ContextUpkeep(new ThrowingRefresh(new OperationCanceledException(cancelled.Token)), NullLogger<ContextUpkeep>.Instance).RunAsync(reporter, cancelled.Token));
    }
}
