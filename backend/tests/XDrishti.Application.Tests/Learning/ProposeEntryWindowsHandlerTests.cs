using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>The learned entry windows use case (spec P6-14): stored run to labelled rows to a proposal, the hold-out, and the refusals.</summary>
public sealed class ProposeEntryWindowsHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2026, 3, 2);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private ProposeEntryWindowsHandler Handler() => new(_runs, new FakeTimeProvider(), NullLogger<ProposeEntryWindowsHandler>.Instance);

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

    private static SimTrade Trade(int dayOffset, int minute, bool win) =>
        FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, Day.AddDays(dayOffset), win ? ExitReason.Target : ExitReason.Stop, entryMinute: minute, rExact: win ? 2 : -1, rFilled: win ? 2 : -1);

    /// <summary>20 morning trades (16 wins) and 20 afternoon trades (4 wins), each on its own day.</summary>
    private void Stored()
    {
        var trades = Enumerable.Range(0, 20).Select(i => Trade(i, 570, i < 16)).Concat(Enumerable.Range(0, 20).Select(i => Trade(20 + i, 800, i < 4))).ToArray();
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
    }

    private static ProposeEntryWindowsQuery Query(int holdOut = 0, string runKey = "") =>
        new([new CellSource(runKey.Length > 0 ? runKey : Key, "intraday", "FIXED_RR")], new EntryWindowOptions(30, 10, 0.03, 0), new ValidationOptions(), holdOut);

    [Fact]
    public async Task A_stored_run_gives_a_proposal_per_cell_with_the_hold_out_stated()
    {
        Stored();

        var result = await Handler().HandleAsync(Query(), Ct);

        result.IsSuccess.ShouldBeTrue();
        var cell = result.Value.Cells.ShouldHaveSingleItem();
        cell.CellKey.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|*");
        cell.WindowStartMinute.ShouldBe((9 * 60) + 15);
        cell.WindowTrades.ShouldBe(20);
        cell.WindowWins.ShouldBe(16);
        cell.Status.ShouldBe(EntryWindowStatus.Inconclusive);
        result.Value.Limits.ShouldContain(l => l.Contains("No hold-out was kept"));
    }

    [Fact]
    public async Task The_hold_out_is_cut_before_the_search()
    {
        Stored();

        var result = await Handler().HandleAsync(Query(holdOut: 1), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBeLessThan(40);
        result.Value.Limits.ShouldContain(l => l.Contains("held out"));
    }

    [Fact]
    public async Task A_bad_request_is_refused_before_any_read()
    {
        var result = await Handler().HandleAsync(Query(runKey: "nope"), Ct);

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
}
