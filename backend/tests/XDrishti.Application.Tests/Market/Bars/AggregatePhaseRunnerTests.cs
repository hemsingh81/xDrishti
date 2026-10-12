using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class AggregatePhaseRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeAggregates _aggregates = new();
    private readonly FakeDataJobStore _jobs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<AggregatePhase> RunAsync(params string[] steps)
    {
        var job = DataJob.Start(steps, false, "owner", Now);
        _jobs.Jobs[job.Id] = job;
        return await new AggregatePhaseRunner(_aggregates, _clock, NullLogger<AggregatePhaseRunner>.Instance).RunAsync(job, new JobReporter(job, _jobs, _clock), Ct);
    }

    private DirtyInstrument Dirty(string symbol)
    {
        var dirty = new DirtyInstrument(Guid.NewGuid(), "NSE:" + symbol, Now.AddDays(-2), Now);
        _aggregates.Dirty.Add(dirty);
        return dirty;
    }

    [Fact]
    public async Task Every_instrument_with_changes_is_built_and_the_buckets_are_summed_by_timeframe()
    {
        var a = Dirty("ABB");
        var b = Dirty("TCS");

        var phase = await RunAsync(DataSteps.Csv, DataSteps.Aggregate);

        _aggregates.Built.ShouldBe([a, b]);
        _aggregates.MarkedAll.ShouldBeFalse();
        (phase.Status, phase.Done, phase.Total, phase.Full, phase.Buckets).ShouldBe((PhaseStatus.Succeeded, 2, 2, false, 22));
        phase.BucketsByTimeframe.ShouldBe(new Dictionary<string, long> { ["5m"] = 20, ["1D"] = 2 }, ignoreOrder: true);
    }

    [Fact]
    public async Task A_job_that_writes_no_bars_marks_everything_changed_first_and_is_a_full_rebuild()
    {
        Dirty("ABB");

        var phase = await RunAsync(DataSteps.Aggregate);

        _aggregates.MarkedAll.ShouldBeTrue();
        phase.Full.ShouldBeTrue();
        phase.Done.ShouldBe(1);
    }

    [Fact]
    public async Task With_nothing_changed_there_is_nothing_to_build()
    {
        var phase = await RunAsync(DataSteps.Csv, DataSteps.Aggregate);

        (phase.Status, phase.Total, phase.Message).ShouldBe((PhaseStatus.Succeeded, 0, "Nothing changed since the last build."));
        _aggregates.Built.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_while_building_reaches_the_executor()
    {
        Dirty("ABB");
        _aggregates.FailOn = new InvalidOperationException("sql");

        await Should.ThrowAsync<InvalidOperationException>(async () => await RunAsync(DataSteps.Aggregate));
    }
}
