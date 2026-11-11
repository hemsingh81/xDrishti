#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Replay;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Replay;
using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Tests.Replay;

/// <summary>SR-01: the candidate source builds the tickets of a replayed session from the as-of facts only, with the pure ticket builder; the bars after the close of the session do not reach a ticket, and the gaps are counted, not guessed.</summary>
public sealed class ReplayCandidateSourceTests
{
    private static readonly DateOnly Session = new(2026, 5, 22);
    private static readonly DateOnly ForDate = new(2026, 5, 25);
    private static readonly Guid InstrumentId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ReplayParams Parameters = new(Session, Session.AddDays(30), new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen);

    private static ReplayCell Cell(string strategy, StoredVersion? version)
    {
        var id = new CellId(strategy, "1D", "BUY", "swing", "FIXED_RR");
        var row = new CellStatsRow(id, CellWindow.All, new DateOnly(2026, 5, 21), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new ReplayCell(new StoredCellStats("k", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "", row), CellState.Candidate, version);
    }

    private static IReplayFacts Facts(IReadOnlyList<ReplayCell> cells, Func<IReadOnlyList<Bar>> bars, bool survivorship = false)
    {
        var facts = Substitute.For<IReplayFacts>();
        facts.UniverseAsync(Parameters, Session, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ReplayUniverse([new ReplayInstrument(InstrumentId, "AAA")], survivorship)));
        facts.CellsAsync(Parameters, Session, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ReplayCells(cells, [])));
        facts.CalendarAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
        facts.MinuteBarsAsync(InstrumentId, Session, Arg.Any<CancellationToken>()).Returns(_ => ValueTask.FromResult(bars()));
        return facts;
    }

    private static (StoredVersion Version, string Text) Seed(string id)
    {
        var text = StrategySeeder.EmbeddedTexts().First(t => t.Name.StartsWith(id, StringComparison.Ordinal)).Text;
        var hash = CanonicalText.Sha256Hex(CanonicalStrategy.Json(StrategyFile.Read(text).Definition!));
        return (new StoredVersion(id, 1, 1, hash, text, "{}", null, null, DateTimeOffset.UnixEpoch), text);
    }

    private static List<Bar> Quiet(int sessions, DateOnly last)
    {
        var days = new List<DateOnly>();
        for (var d = last; days.Count < sessions; d = d.AddDays(-1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Insert(0, d);
            }
        }

        var bars = new List<Bar>();
        for (var i = 0; i < days.Count; i++)
        {
            var price = 100 * (1 + (0.001 * i));
            var volume = i == days.Count - 1 ? 3000 : 1000;
            var open = new DateTimeOffset(days[i].ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).AddMinutes(SessionClock.OpenMinute);
            for (var m = 0; m < SessionClock.RegularBars; m++)
            {
                bars.Add(new Bar(open.AddMinutes(m).ToUnixTimeSeconds(), price, price + 0.05, price - 0.05, price, volume));
            }
        }

        return bars;
    }

    [Fact]
    public async Task A_cell_with_no_version_is_listed_not_guessed_and_a_complete_session_makes_a_ticket_with_the_version_of_the_rule()
    {
        var (version, _) = Seed("BASE_BREAKOUT");
        var facts = Facts([Cell("BASE_BREAKOUT", version), Cell("NO_SUCH", null)], () => Quiet(100, Session));

        var set = await new ReplayCandidateSource(facts).ReadAsync(Parameters, Session, ForDate, Ct);

        set.NoVersionCells.ShouldBe([Cell("NO_SUCH", null).Stored.Row.Cell.Key]);
        var ticket = set.Found.ShouldHaveSingleItem();
        ticket.Draft.Verdict.ShouldBe(TicketVerdict.Conditional);
        ticket.Draft.StrategyVersionHash.ShouldBe(version.Hash);
        ticket.InstrumentId.ShouldBe(InstrumentId);
        (set.Universe, set.IncompleteBars).ShouldBe((1, 0));
    }

    [Fact]
    public async Task Bars_after_the_close_of_the_session_handed_to_the_source_change_no_ticket()
    {
        var (version, _) = Seed("BASE_BREAKOUT");
        var honest = Quiet(100, Session);
        var withFuture = Quiet(100, Session).Concat(Quiet(3, ForDate.AddDays(3)).Select(b => b with { Close = 5000, High = 6000, Volume = 99_999 })).ToList();

        var a = await new ReplayCandidateSource(Facts([Cell("BASE_BREAKOUT", version)], () => honest)).ReadAsync(Parameters, Session, ForDate, Ct);
        var b = await new ReplayCandidateSource(Facts([Cell("BASE_BREAKOUT", version)], () => withFuture)).ReadAsync(Parameters, Session, ForDate, Ct);

        a.Found.ShouldHaveSingleItem();
        b.Found.Select(Shape).ShouldBe(a.Found.Select(Shape)); // the draft's part list is a reference: compare what a ticket says
        (b.Rejected, b.Unavailable, b.IncompleteBars).ShouldBe((a.Rejected, a.Unavailable, a.IncompleteBars));
    }

    private static (string Reason, double? Stop, double? Entry, double? Target, double? Turnover) Shape(XDrishti.Application.Abstractions.Planning.TicketCandidate c) => (c.Draft.Reason, c.Draft.StopLevel, c.Draft.IndicativeEntry, c.Draft.TargetLevel, c.Draft.AverageDailyTurnover);

    [Fact]
    public async Task An_instrument_whose_session_is_not_complete_is_counted_incomplete_and_gives_no_ticket()
    {
        var (version, _) = Seed("BASE_BREAKOUT");
        var facts = Facts([Cell("BASE_BREAKOUT", version)], () => []);

        var set = await new ReplayCandidateSource(facts).ReadAsync(Parameters, Session, ForDate, Ct);

        set.Found.ShouldBeEmpty();
        (set.Universe, set.IncompleteBars).ShouldBe((1, 1));
    }

    [Fact]
    public async Task A_stored_text_that_does_not_read_is_unavailable_and_the_survivorship_flag_travels_with_the_set()
    {
        var bad = new StoredVersion("BASE_BREAKOUT", 1, 1, new string('0', 64), "not: [a strategy", "{}", null, null, DateTimeOffset.UnixEpoch);
        var facts = Facts([Cell("BASE_BREAKOUT", bad)], () => Quiet(100, Session), survivorship: true);

        var set = await new ReplayCandidateSource(facts).ReadAsync(Parameters, Session, ForDate, Ct);

        set.Unavailable.ShouldBe(1);
        set.Found.ShouldBeEmpty();
        set.Survivorship.ShouldBeTrue();
    }
}
