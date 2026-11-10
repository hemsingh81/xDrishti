#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Application.SimAll;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.Tests.SimAll;

/// <summary>AU-12 and ADR 0243: a chunk is built as a training run through the one request value, its failures and its timeout are recorded on the chunk, and a stop of the host is not a failure of the chunk.</summary>
public sealed class SimAllChunkExecutorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly IStrategyChecker _checker = Substitute.For<IStrategyChecker>();
    private readonly ISimulationEngine _engine = Substitute.For<ISimulationEngine>();
    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();
    private readonly IQueryHandler<RunSimulationQuery, Result<SimulationRun>> _simulation = Substitute.For<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
    private readonly ICommandHandler<StoreRunCommand, StoreOutcome> _store = Substitute.For<ICommandHandler<StoreRunCommand, StoreOutcome>>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 14, 30, 0, TimeSpan.Zero));
    private readonly SimAllJob _job = new(Guid.NewGuid(), SimAllStates.Running, null, new DateOnly(2020, 1, 1), new DateOnly(2025, 6, 1), new DateOnly(2026, 1, 1), 2, "owner:me@example.com", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
    private readonly SimAllChunk _chunk;

    public SimAllChunkExecutorTests()
    {
        _chunk = new SimAllChunk(_job.Id, "ORB", 2, new string('a', 64), Guid.NewGuid());
        _strategies.GetByHashAsync(_chunk.StrategyHash, Arg.Any<CancellationToken>()).Returns(new StoredVersion("ORB", 2, 1, _chunk.StrategyHash, "text", "{}", null, null, DateTimeOffset.UnixEpoch));
        _checker.Check("text").Returns(new StrategyCheck(new StrategyDescription("ORB", "x", _chunk.StrategyHash, "{}", [Timeframe.M15], [Side.Buy], []), 2, []));
        _engine.BuiltIns.Returns([]);
        _sheets.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<CostSheet>(Error.NotFound("costs.sheet_not_found", "none")));
    }

    private SimAllChunkExecutor Executor() =>
        new(_strategies, _checker, _engine, _sheets, new LabSettings(1, 1, 1, "sheet", TimeSpan.Zero, null!, 1, false), _simulation, _store, Substitute.For<IRunRecordWriter>(), new SimAllSettings { ChunkTimeoutMinutes = 30 }, _time, NullLogger<SimAllChunkExecutor>.Instance);

    [Fact]
    public async Task The_request_is_one_instrument_in_the_job_window_with_the_training_purpose_and_a_failed_run_fails_the_chunk_with_its_code()
    {
        RunSimulationQuery? seen = null;
        _simulation.HandleAsync(Arg.Do<RunSimulationQuery>(q => seen = q), Arg.Any<CancellationToken>()).Returns(Result.Failure<SimulationRun>(Error.Validation("simulation.invalid", "no bars")));

        var result = await Executor().ExecuteAsync(_job, _chunk, Ct);

        seen!.Request.Purpose.ShouldBe(RunPurpose.Training);
        seen.Request.InstrumentIds.ShouldBe([_chunk.InstrumentId]);
        seen.Request.From.ShouldBe(_job.WindowFrom);
        seen.Request.To.ShouldBe(_job.WindowTo);
        seen.Request.StrategyHash.ShouldBe(_chunk.StrategyHash);
        result.State.ShouldBe(SimAllStates.Failed);
        result.FailCode.ShouldBe("simulation.invalid");
        await _store.DidNotReceiveWithAnyArgs().HandleAsync(null!, Ct);
    }

    [Fact]
    public async Task A_strategy_that_is_no_longer_stored_fails_the_chunk_without_a_simulation()
    {
        _strategies.GetByHashAsync(_chunk.StrategyHash, Arg.Any<CancellationToken>()).Returns((StoredVersion?)null);

        var result = await Executor().ExecuteAsync(_job, _chunk, Ct);

        result.FailCode.ShouldBe(SimAllErrors.StrategyMissingCode);
        await _simulation.DidNotReceiveWithAnyArgs().HandleAsync(null!, Ct);
    }

    [Fact]
    public async Task A_chunk_past_its_time_limit_is_failed_with_the_timeout_code()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(call => HangAsync(() => _time.Advance(TimeSpan.FromMinutes(31)), call.ArgAt<CancellationToken>(1)));

        var result = await Executor().ExecuteAsync(_job, _chunk, Ct);

        result.State.ShouldBe(SimAllStates.Failed);
        result.FailCode.ShouldBe(SimAllErrors.ChunkTimeoutCode);
    }

    [Fact]
    public async Task A_stop_of_the_host_is_raised_and_not_recorded_as_a_failure_of_the_chunk()
    {
        using var stop = new CancellationTokenSource();
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns(call => HangAsync(() => stop.Cancel(), call.ArgAt<CancellationToken>(1)));

        await Should.ThrowAsync<OperationCanceledException>(async () => await Executor().ExecuteAsync(_job, _chunk, stop.Token));
    }

    [Fact]
    public async Task An_unexpected_exception_fails_the_chunk_and_not_the_job()
    {
        _simulation.HandleAsync(Arg.Any<RunSimulationQuery>(), Arg.Any<CancellationToken>()).Returns<Result<SimulationRun>>(_ => throw new InvalidOperationException("boom"));

        var result = await Executor().ExecuteAsync(_job, _chunk, Ct);

        result.FailCode.ShouldBe(SimAllErrors.ChunkCrashedCode);
        result.FailText!.ShouldNotContain("boom");
    }

    /// <summary>Does something, then waits until the token is cancelled (a simulation that never ends on its own).</summary>
    private static async ValueTask<Result<SimulationRun>> HangAsync(Action before, CancellationToken token)
    {
        before();
        await Task.Delay(Timeout.Infinite, token);
        return Result.Failure<SimulationRun>(Error.Validation("never", "never"));
    }
}
