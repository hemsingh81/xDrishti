using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Pipeline;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class DhanPhaseRunnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private static readonly long Open = new DateTimeOffset(2026, 10, 9, 9, 15, 0, TimeSpan.FromMinutes(330)).ToUnixTimeSeconds();

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly FakeFetchTargets _targets = new();
    private readonly FakeBarStore _bars = new();
    private readonly FakeDataJobStore _jobs = new();
    private readonly FakeDataSettings _settings = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FetchTarget Target(string symbol, DateTimeOffset? last = null)
    {
        var target = new FetchTarget(Guid.CreateVersion7(), "NSE:" + symbol, "share", "100", "NSE_EQ", last);
        _targets.Targets.Add(target);
        return target;
    }

    private static Bar Good(int minute) => new(Open + minute * 60L, 100, 101, 99, 100.5, 10);

    private async Task<(DhanPhase Phase, DataJob Job)> RunAsync()
    {
        var job = DataJob.Start([DataSteps.Dhan], false, "owner", Now);
        _jobs.Jobs[job.Id] = job;
        var runner = new DhanPhaseRunner(_source, _account, _targets, _bars, _settings, _clock, NullLogger<DhanPhaseRunner>.Instance);
        return (await runner.RunAsync(new JobReporter(job, _jobs, _clock), Ct), job);
    }

    [Fact]
    public async Task A_source_that_is_not_available_skips_the_phase_with_its_reason_before_anything_else_is_asked()
    {
        _source.Availability = new SourceAvailability(false, "Dhan Data API fetch is not available yet");
        _account.State = new DataAccountState(null, null, DataAccountStatuses.None, "no account");
        Target("ABB");

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Code, phase.Message).ShouldBe((PhaseStatus.Skipped, "data.dhan_not_available", "Dhan Data API fetch is not available yet"));
        _source.Plans.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DataAccountStatuses.None, null)]
    [InlineData(DataAccountStatuses.NotConnected, "Primary")]
    [InlineData(DataAccountStatuses.Expired, "Primary")]
    public async Task An_account_that_cannot_fetch_skips_the_phase_with_the_accounts_reason(string status, string? name)
    {
        _account.State = new DataAccountState(name is null ? null : Guid.NewGuid(), name, status, "Reconnect it under Accounts.");
        Target("ABB");

        var (phase, job) = await RunAsync();

        (phase.Status, phase.Code, phase.Message, phase.AccountName).ShouldBe((PhaseStatus.Skipped, "data.dhan_not_connected", "Reconnect it under Accounts.", name));
        _source.Plans.ShouldBeEmpty();
        job.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_to_fetch_skips_the_phase()
    {
        var (phase, _) = await RunAsync();

        (phase.Status, phase.Code, phase.AccountName).ShouldBe((PhaseStatus.Skipped, null, "Primary"));
        phase.Message.ShouldBe("No tracked share, ETF or index has a Dhan id: nothing to fetch.");
    }

    [Fact]
    public async Task The_plan_uses_the_last_bar_the_initial_days_and_the_clock()
    {
        _settings.DhanInitialDays = 100;
        var last = Now.AddDays(-4);
        Target("ABB", last);
        Target("TCS");

        await RunAsync();

        var plan = _source.Plans.ShouldHaveSingleItem();
        plan.Targets[0].Windows.ShouldBe([new FetchWindow(last.AddDays(-2), Now)]);
        plan.Targets[1].Windows.Count.ShouldBe(2);
        plan.Targets[1].Windows[0].From.ShouldBe(Now.AddDays(-100));
    }

    [Fact]
    public async Task Fetched_bars_are_checked_compared_and_merged_as_dhan_bars()
    {
        var last = new DateTimeOffset(2026, 10, 9, 9, 17, 0, TimeSpan.FromMinutes(330));
        var abb = Target("ABB", last);
        _bars.Stored[abb.InstrumentId] = [Good(0), Good(1), Good(2)];
        var shiftedClose = Good(1) with { Close = 120 };
        _source.Respond = t => new IntradayResult(t.InstrumentId, [Good(0), shiftedClose, Good(2), Good(3), new Bar(Open - 600, 1, 1, 1, 1, 1)], 2);

        var (phase, job) = await RunAsync();

        (phase.Status, phase.InstrumentsFetched, phase.Bars, phase.Inserted, phase.Rejected, phase.Requests, phase.Done, phase.Total).ShouldBe((PhaseStatus.Succeeded, 1, 5, 4, 1, 2, 1, 1));
        (phase.OverlapCompared, phase.OverlapMismatched).ShouldBe((3, 1));
        var session = _bars.Sessions.ShouldHaveSingleItem();
        (session.Source, session.InstrumentId, session.Completed, session.Mark).ShouldBe((BarSource.Dhan, abb.InstrumentId, true, null));
        session.Bars.Select(b => b.EpochSeconds).ShouldBe([Open, Open + 60, Open + 120, Open + 180]);
        job.Warnings.Count.ShouldBe(2);
        job.Warnings[0].ShouldBe("NSE:ABB: 1 of 3 overlapping bars differ from the stored ones by more than 0.05 %.");
        job.Warnings[1].ShouldStartWith("NSE:ABB: 1 bars from Dhan were not stored (for example ");
    }

    [Fact]
    public async Task A_whole_session_is_merged_in_one_merge_session()
    {
        Target("ABB");
        var day = Enumerable.Range(0, 375).Select(Good).ToList();
        _source.Respond = t => new IntradayResult(t.InstrumentId, day, 1);

        var (phase, _) = await RunAsync();

        phase.Inserted.ShouldBe(375);
        _bars.Sessions.Single().Bars.Count.ShouldBe(375);
    }

    [Fact]
    public async Task One_instrument_failing_or_not_being_stored_is_recorded_and_the_next_one_goes_on()
    {
        var abb = Target("ABB");
        var tcs = Target("TCS");
        var infy = Target("INFY");
        _bars.Failures[tcs.InstrumentId] = new InvalidOperationException("db detail");
        _source.Respond = t => t.InstrumentId == abb.InstrumentId
            ? new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "Dhan answered 500"))
            : new IntradayResult(t.InstrumentId, [Good(0)], 1);

        var (phase, _) = await RunAsync();

        (phase.Status, phase.InstrumentsFetched, phase.InstrumentsFailed, phase.Done, phase.Total).ShouldBe((PhaseStatus.Succeeded, 1, 2, 3, 3));
        phase.Errors.ShouldBe(["NSE:ABB: Dhan answered 500", "NSE:TCS: the bars could not be stored"]);
        phase.Message.ShouldBe("2 of 3 instruments could not be fetched: NSE:ABB: Dhan answered 500");
        _bars.Stored[infy.InstrumentId].Count.ShouldBe(1);
    }

    [Fact]
    public async Task Only_ten_error_messages_are_kept()
    {
        for (var i = 0; i < 15; i++)
        {
            Target("S" + i);
        }

        _source.Respond = t => new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Failed, "timeout"));

        var (phase, _) = await RunAsync();

        (phase.Status, phase.InstrumentsFailed, phase.Errors.Count).ShouldBe((PhaseStatus.Failed, 15, DhanPhase.MaxErrors));
        phase.Message.ShouldBe("Dhan returned errors for all 15 instruments: NSE:S0: timeout");
    }

    [Fact]
    public async Task A_refused_token_ends_the_phase_at_once_and_keeps_what_was_stored_before()
    {
        var abb = Target("ABB");
        Target("TCS");
        Target("INFY");
        _source.Respond = t => t.InstrumentId == abb.InstrumentId
            ? new IntradayResult(t.InstrumentId, [Good(0)], 1)
            : new IntradayResult(t.InstrumentId, [], 1, new IntradayFailure(IntradayFailureKind.Unauthorized, "401"));

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Code, phase.Done, phase.InstrumentsFetched, phase.InstrumentsFailed, phase.Requests).ShouldBe((PhaseStatus.Failed, "data.dhan_not_connected", 1, 1, 0, 2));
        phase.Message.ShouldBe("Dhan refused the access token of the Data account. Reconnect it under Accounts, then run again.");
        _bars.Stored.Keys.ShouldBe([abb.InstrumentId]);
    }

    [Fact]
    public async Task Results_for_instruments_that_were_not_asked_for_are_ignored()
    {
        Target("ABB");
        _source.Respond = t => new IntradayResult(Guid.NewGuid(), [Good(0)], 1);

        var (phase, _) = await RunAsync();

        (phase.Status, phase.Done, phase.Bars).ShouldBe((PhaseStatus.Succeeded, 0, 0));
        _bars.Sessions.ShouldBeEmpty();
    }
}
