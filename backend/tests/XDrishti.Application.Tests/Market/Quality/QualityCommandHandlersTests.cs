using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.Quality;
using XDrishti.Application.Tests.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Quality;

/// <summary>"Run checks" and the two fix actions: each registers a data job (so they share the one-at-a-time rule) with the right steps for the right instrument.</summary>
public sealed class QualityCommandHandlersTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FakeDataJobStore _jobs = new();
    private readonly IDataJobChannel _queue = Substitute.For<IDataJobChannel>();
    private readonly IInstrumentCatalog _catalog = Substitute.For<IInstrumentCatalog>();
    private readonly IBarQueries _queries = Substitute.For<IBarQueries>();
    private readonly FakeIntradaySource _source = new();
    private readonly FakeAccountStatus _account = FakeAccountStatus.Connected();
    private readonly FakeFetchTargets _targets = new();
    private readonly Guid _id = Guid.CreateVersion7();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public QualityCommandHandlersTests()
    {
        _queries.FindInstrumentAsync(_id, Arg.Any<CancellationToken>()).Returns(new BarsInstrument(_id, "NSE:ABB", "ABB Ltd"));
        _targets.Targets.Add(new FetchTarget(_id, "NSE:ABB", "share", "1", "NSE_EQ", null));
    }

    private StartDataJobHandler Start => new(_jobs, _queue, _catalog, new FakeCsvFolder(), _clock, NullLogger<StartDataJobHandler>.Instance);

    private async Task<Result<DataJobView>> RunAsync(Guid? instrumentId) =>
        await new StartQualityRunHandler(_queries, Start).HandleAsync(new StartQualityRunCommand(new StartQualityRunRequest(instrumentId), "owner@example.invalid"), Ct);

    private async Task<Result<DataJobView>> RefetchAsync(Guid id) =>
        await new RefetchQualityInstrumentHandler(_queries, _source, _account, _targets, Start).HandleAsync(new RefetchQualityInstrumentCommand(id, "owner@example.invalid"), Ct);

    private async Task<Result<DataJobView>> RefreshAsync(Guid id) =>
        await new RefreshQualityAggregatesHandler(_queries, Start).HandleAsync(new RefreshQualityAggregatesCommand(id, "owner@example.invalid"), Ct);

    [Fact]
    public async Task Run_checks_starts_a_job_with_the_quality_step_over_every_instrument_and_queues_it()
    {
        var result = await RunAsync(null);

        var view = result.Value;
        view.Steps.ShouldBe(["quality"]);
        (view.InstrumentId, view.Trigger, view.Status, view.RequestedBy).ShouldBe((null, "manual", "running", "owner@example.invalid"));
        view.Quality!.Status.ShouldBe(PhaseStatus.Pending);
        _queue.Received(1).Enqueue(new DataJobTicket(view.Id));
        await _queries.DidNotReceiveWithAnyArgs().FindInstrumentAsync(default, Ct);
    }

    [Fact]
    public async Task Run_checks_for_one_instrument_looks_it_up_and_limits_the_job_to_it()
    {
        var result = await RunAsync(_id);

        result.Value.InstrumentId.ShouldBe(_id);
        _jobs.Started.Single().InstrumentId.ShouldBe(_id);
    }

    [Fact]
    public async Task Run_checks_for_an_unknown_instrument_is_not_found_and_starts_nothing()
    {
        var result = await RunAsync(Guid.NewGuid());

        result.Error.Code.ShouldBe("data.instrument_not_found");
        _jobs.Started.ShouldBeEmpty();
        _queue.DidNotReceiveWithAnyArgs().Enqueue(default!);
    }

    [Fact]
    public async Task While_any_job_runs_run_checks_is_refused_as_a_conflict()
    {
        await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct);

        var result = await RunAsync(null);

        (result.Error.Code, result.Error.Kind).ShouldBe(("data.job_running", ErrorKind.Conflict));
    }

    [Fact]
    public async Task Re_fetch_starts_dhan_aggregate_and_quality_for_the_instrument_asking_again_for_unfillable_days()
    {
        var result = await RefetchAsync(_id);

        var view = result.Value;
        view.Steps.ShouldBe(["dhan", "aggregate", "quality"]);
        (view.InstrumentId, view.RefetchGaps).ShouldBe((_id, true));
        _queue.Received(1).Enqueue(new DataJobTicket(view.Id));
    }

    [Fact]
    public async Task Re_fetch_of_an_unknown_instrument_is_not_found()
    {
        (await RefetchAsync(Guid.NewGuid())).Error.Code.ShouldBe("data.instrument_not_found");
    }

    [Fact]
    public async Task Re_fetch_is_refused_when_the_fetch_is_not_available_with_its_reason()
    {
        _source.Availability = new SourceAvailability(false, "The Dhan fetch is switched off");

        var result = await RefetchAsync(_id);

        (result.Error.Code, result.Error.Kind, result.Error.Message).ShouldBe(("data.dhan_not_available", ErrorKind.Conflict, "The Dhan fetch is switched off"));
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Re_fetch_is_refused_when_the_data_account_is_not_connected()
    {
        _account.State = new DataAccountState(null, null, DataAccountStatuses.None, "There is no connected primary Data account.");

        var result = await RefetchAsync(_id);

        (result.Error.Code, result.Error.Message).ShouldBe(("data.dhan_not_connected", "There is no connected primary Data account."));
    }

    [Fact]
    public async Task Re_fetch_is_refused_for_an_instrument_dhan_cannot_be_asked_about()
    {
        _targets.Targets.Clear();

        var result = await RefetchAsync(_id);

        (result.Error.Code, result.Error.Kind).ShouldBe(("data.dhan_not_available", ErrorKind.Conflict));
        _jobs.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task While_a_job_runs_re_fetch_is_refused_as_a_conflict()
    {
        await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct);

        (await RefetchAsync(_id)).Error.Code.ShouldBe("data.job_running");
    }

    [Fact]
    public async Task Refresh_aggregates_starts_aggregate_and_quality_for_the_instrument()
    {
        var result = await RefreshAsync(_id);

        var view = result.Value;
        view.Steps.ShouldBe(["aggregate", "quality"]);
        (view.InstrumentId, view.RebuildAll, view.RefetchGaps).ShouldBe((_id, true, false));
    }

    [Fact]
    public async Task Refresh_aggregates_of_an_unknown_instrument_is_not_found_and_while_a_job_runs_a_conflict()
    {
        (await RefreshAsync(Guid.NewGuid())).Error.Code.ShouldBe("data.instrument_not_found");

        await Start.HandleAsync(new StartDataJobCommand(new StartDataJobRequest(["aggregate"]), "owner"), Ct);

        (await RefreshAsync(_id)).Error.Code.ShouldBe("data.job_running");
    }
}
