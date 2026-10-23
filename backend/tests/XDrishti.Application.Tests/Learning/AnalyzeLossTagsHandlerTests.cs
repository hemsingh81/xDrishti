using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The loss-tag use case (spec P6-15): stored run to tagged losses per cell, and the refusals.</summary>
public sealed class AnalyzeLossTagsHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private AnalyzeLossTagsHandler Handler() => new(_runs, new FakeTimeProvider(), NullLogger<AnalyzeLossTagsHandler>.Instance);

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private static AnalyzeLossTagsQuery Query(string runKey = "") => new([new CellSource(runKey.Length > 0 ? runKey : Key, "intraday", "FIXED_RR")], new LossTagOptions());

    private static SimTrade Stop(int day, double rFilled, double exit) =>
        FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, new DateOnly(2026, 3, 2).AddDays(day), ExitReason.Stop, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: exit, rExact: -1, rFilled: rFilled);

    [Fact]
    public async Task A_stored_run_gives_tagged_losses_on_the_cell_result_not_the_exact_one()
    {
        // Filled R is -0.05 on the second trade, so it is a loss on the exact R only: the cell unit decides.
        var trades = new[] { Stop(0, -1, 99), Stop(1, -1, 98.5), Stop(2, 0.5, 99) };
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));

        var result = await Handler().HandleAsync(Query(), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Trades.ShouldBe(3);
        result.Value.Losses.ShouldBe(2);
        var cell = result.Value.Cells.ShouldHaveSingleItem();
        cell.CellKey.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*");
        cell.Counts[(int)LossTagKind.GapThroughStop].ShouldBe(1);
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var result = await Handler().HandleAsync(Query("nope"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        _runs.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task A_run_without_trades_is_refused_with_the_analyser_code()
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream([]));

        var result = await Handler().HandleAsync(Query(), Ct);

        result.Error.Code.ShouldBe(LossTagAnalyzer.InvalidCode);
    }
}
