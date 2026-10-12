using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class JobReporterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeDataJobStore _store = new();
    private readonly DataJob _job = DataJob.Start([DataSteps.Csv], false, "owner", Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Progress_is_written_at_most_every_half_second_but_the_job_always_has_the_latest_numbers()
    {
        var reporter = new JobReporter(_job, _store, _clock);

        await reporter.ReportAsync(_job.Csv with { Status = PhaseStatus.Running, Done = 1 }, Ct);
        await reporter.ReportAsync(_job.Csv with { Done = 2 }, Ct);
        await reporter.ReportAsync(_job.Csv with { Done = 3 }, Ct);
        _clock.Advance(TimeSpan.FromMilliseconds(499));
        await reporter.ReportAsync(_job.Csv with { Done = 4 }, Ct);
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        await reporter.ReportAsync(_job.Csv with { Done = 5 }, Ct);

        _store.Published.Select(p => p.CsvDone).ShouldBe([1, 5]);
        _job.Csv.Done.ShouldBe(5);
    }

    [Fact]
    public async Task Insisting_writes_at_once_and_a_warning_is_always_written()
    {
        var reporter = new JobReporter(_job, _store, _clock);
        await reporter.ReportAsync(_job.Csv with { Status = PhaseStatus.Running }, Ct);

        await reporter.ReportAsync(_job.Csv with { Done = 7 }, Ct, publishNow: true);
        await reporter.WarnAsync("careful", Ct);

        _store.Published.Count.ShouldBe(3);
        _job.Warnings.ShouldBe(["careful"]);
        _store.Published[0].Phase.ShouldBe("csv");
    }

    [Fact]
    public async Task When_the_store_says_the_job_is_no_longer_running_nothing_is_written_again()
    {
        var reporter = new JobReporter(_job, _store, _clock);
        _store.EndedElsewhere.Add(_job.Id);

        await reporter.ReportAsync(_job.Csv with { Status = PhaseStatus.Running }, Ct, publishNow: true);
        await reporter.ReportAsync(_job.Csv with { Done = 3 }, Ct, publishNow: true);
        await reporter.WarnAsync("careful", Ct);
        await reporter.PublishAsync(Ct);

        reporter.Ended.ShouldBeTrue();
        _store.Published.ShouldBeEmpty();
        _job.Warnings.ShouldBeEmpty();
        _job.Csv.Done.ShouldBe(0);
    }
}
