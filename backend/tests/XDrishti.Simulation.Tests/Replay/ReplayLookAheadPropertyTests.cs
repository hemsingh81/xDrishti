using XDrishti.Domain.Research;
using XDrishti.Simulation.Replay;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Simulation.Tests.Replay;

/// <summary>
/// Criterion 14 of P3-04: no look-ahead through the replay's own reading and filtering of the bars. For a cut at the end of a session c, the trades dated up to c, the flagged days up to c and the notes of the sessions up to c are the
/// same when the series ends at c, when it goes on, and when the sessions after c are replaced by extreme bars. The session filters use the bars of their own session and the previous close only, so the cut is at a session boundary; this carries
/// the properties of P3-03 (the walker) and P3-03a (through <c>Simulator.Run</c>) to the replay. The counters are not compared: they cover the whole replay to <c>To</c>.
/// </summary>
public sealed class ReplayLookAheadPropertyTests
{
    private const int Seeds = 100;

    private static readonly PreparedStrategy Bare = Rep.Prepare(OrbV1.Create());

    /// <summary>What the property compares: the trades of the prefix, its flagged days and its notes, as text.</summary>
    private static string Prefix(InstrumentReplay replay, DateOnly cut) =>
        string.Join("\n", replay.Trades.Where(t => t.TradeDate <= cut).Select(RunText.Trade))
        + "\n--\n" + string.Join("\n", replay.Record.Discontinuities.Where(d => d.Date <= cut).Select(d => d.Date.ToString("yyyy-MM-dd") + " " + CanonicalText.Number(d.Ratio)))
        + "\n--\n" + string.Join("\n", replay.Record.Notes.Where(n => n.Date <= cut).Select(n => n.Date.ToString("yyyy-MM-dd") + " " + n.Reason.ToCode() + " " + n.BarsInWindow + " " + n.ExpectedMinutes));

    private static int CutOf(ulong seed) => 2 + new SeededSeries(seed * 31 + 7).Int(7);

    [Fact]
    public void The_prefix_of_the_replay_is_the_same_when_the_series_ends_goes_on_or_is_replaced_by_extreme_bars_and_the_property_is_not_vacuous()
    {
        long trades = 0, flagged = 0, excluded = 0, cutsAfterFlagged = 0;
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var series = SeededSessions.Of(seed);
            var c = CutOf(seed);
            var cut = series.Dates[c];
            var first = series.Dates[0];

            var ending = Rep.Replay(Bare, series.Through(c), first, cut);
            var going = Rep.Replay(Bare, series.Bars, first, series.Dates[^1]);
            var extreme = Rep.Replay(Bare, series.WithExtremes(c + 1, SeededSessions.Sessions - 1, seed), first, series.Dates[^1]);

            var expected = Prefix(ending, cut);
            Prefix(going, cut).ShouldBe(expected, "seed " + seed + ": the series goes on");
            Prefix(extreme, cut).ShouldBe(expected, "seed " + seed + ": the sessions after the cut are replaced by extreme bars");
            ReplayIdentities.ShouldHold(going, "seed " + seed);
            ReplayIdentities.ShouldHold(extreme, "seed " + seed);

            trades += ending.Trades.Count;
            flagged += ending.Record.Discontinuities.Count;
            excluded += ending.Record.Sessions.Excluded;
            cutsAfterFlagged += ending.Record.Discontinuities.Count > 0 ? 1 : 0;
        }

        trades.ShouldBeGreaterThanOrEqualTo(250, "trades in the compared prefixes");
        flagged.ShouldBeGreaterThanOrEqualTo(40, "flagged days in the compared prefixes");
        excluded.ShouldBeGreaterThanOrEqualTo(40, "excluded sessions in the compared prefixes");
        cutsAfterFlagged.ShouldBeGreaterThanOrEqualTo(40, "cuts that lie after a flagged day");
    }

    [Fact]
    public void Control_extreme_bars_placed_in_the_first_session_show_a_difference_in_every_seed()
    {
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var series = SeededSessions.Of(seed);
            var cut = series.Dates[CutOf(seed)];
            var first = series.Dates[0];

            var honest = Rep.Replay(Bare, series.Bars, first, series.Dates[^1]);
            var doctored = Rep.Replay(Bare, series.WithExtremes(0, 0, seed), first, series.Dates[^1]);

            Prefix(doctored, cut).ShouldNotBe(Prefix(honest, cut), "seed " + seed + ": the comparison can fail");
        }
    }

    [Fact]
    public void Every_series_has_the_shape_the_generator_promises_ten_weekdays_and_a_first_plain_day()
    {
        for (ulong seed = 1; seed <= Seeds; seed++)
        {
            var series = SeededSessions.Of(seed);

            series.Dates.Length.ShouldBe(10);
            series.Dates.ShouldAllBe(d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday);
            series.Dates.ShouldBe(series.Dates.Order());
            (series.Ends[0] - series.Starts[0]).ShouldBe(375);
            series.Bars.Select(b => b.EpochSeconds).ShouldBe(series.Bars.Select(b => b.EpochSeconds).Order());
        }
    }
}
