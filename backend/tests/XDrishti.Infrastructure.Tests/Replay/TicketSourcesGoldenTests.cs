#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Replay;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Planning;
using XDrishti.Infrastructure.Replay;
using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Tests.Replay;

/// <summary>SR-01 AC 5, candidate side: the live ticket source and the replay's candidate source, over the same cells, versions and bars, give the same tickets, the same rejected and unavailable counts.</summary>
public sealed class TicketSourcesGoldenTests
{
    private static readonly DateOnly Session = new(2026, 5, 22);
    private static readonly DateOnly ForDate = new(2026, 5, 25);
    private static readonly Guid InstrumentId = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StoredCellStats Cell(string strategy)
    {
        var id = new CellId(strategy, "1D", "BUY", "swing", "FIXED_RR");
        var row = new CellStatsRow(id, CellWindow.All, new DateOnly(2026, 5, 21), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats("k", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "", row);
    }

    private static List<Bar> Quiet(int sessions)
    {
        var days = new List<DateOnly>();
        for (var d = Session; days.Count < sessions; d = d.AddDays(-1))
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
            var open = new DateTimeOffset(days[i].ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).AddMinutes(SessionClock.OpenMinute);
            for (var m = 0; m < SessionClock.RegularBars; m++)
            {
                bars.Add(new Bar(open.AddMinutes(m).ToUnixTimeSeconds(), price, price + 0.05, price - 0.05, price, i == days.Count - 1 ? 3000 : 1000));
            }
        }

        return bars;
    }

    [Fact]
    public async Task The_live_and_the_replay_sources_build_the_same_tickets_from_the_same_stores()
    {
        var text = StrategySeeder.EmbeddedTexts().First(t => t.Name.StartsWith("BASE_BREAKOUT", StringComparison.Ordinal)).Text;
        var hash = CanonicalText.Sha256Hex(CanonicalStrategy.Json(StrategyFile.Read(text).Definition!));
        var good = new StoredVersion("BASE_BREAKOUT", 1, 1, hash, text, "{}", null, null, DateTimeOffset.UnixEpoch);
        var bad = new StoredVersion("BROKEN", 1, 1, new string('0', 64), "not: [a strategy", "{}", null, null, DateTimeOffset.UnixEpoch);
        var cells = new[] { Cell("BASE_BREAKOUT"), Cell("BROKEN") };
        var bars = Quiet(100);

        // live, over the stores
        var stats = Substitute.For<ICellStatsStore>();
        stats.ListLatestBeforeAsync(ForDate, null, "all", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>(cells));
        var changelog = Substitute.For<ILearningChangelog>();
        changelog.HistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([]));
        var strategies = Substitute.For<IStrategyStore>();
        strategies.GetVersionAsync("BASE_BREAKOUT", null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(good));
        strategies.GetVersionAsync("BROKEN", null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(bad));
        var instrument = new InstrumentSummary(InstrumentId, "AAA", "Aaa", "Aaa", "EQUITY", null, null, null, 1, null, null, false, false, false, TrackedInfo.Never);
        var member = new BasketMemberView(Guid.NewGuid(), instrument, 0, DateTimeOffset.UnixEpoch, null, null, "owner", null, null);
        var baskets = Substitute.For<IBasketQueries>();
        baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BasketView>>([new BasketView(Guid.NewGuid(), "b", "#fff", "", null, 0, 1, DateTimeOffset.UnixEpoch, null, "manual", false, null, null)]));
        baskets.MembersAsync(Arg.Any<Guid>(), null, null, Arg.Any<CancellationToken>()).Returns(ci => ValueTask.FromResult<BasketMembers?>(new BasketMembers(ci.Arg<Guid>(), "b", null, null, 1, [member])));
        var store = Substitute.For<IBarStore>();
        store.ReadAsync(InstrumentId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<Bar>>(bars));
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
        var live = (await new StoredTicketSource(stats, changelog, strategies, baskets, store, Substitute.For<XDrishti.Application.Abstractions.Market.History.IBackfillSeams>(), calendar).ReadAsync(Session, ForDate, Ct)).Value;

        // replay, over the facts that read the same
        var parameters = new ReplayParams(Session, Session.AddDays(30), new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen);
        var facts = Substitute.For<IReplayFacts>();
        facts.UniverseAsync(parameters, Session, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ReplayUniverse([new ReplayInstrument(InstrumentId, "AAA")], false)));
        facts.CellsAsync(parameters, Session, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new ReplayCells([new ReplayCell(cells[0], null, good), new ReplayCell(cells[1], null, bad)], [])));
        facts.CalendarAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
        facts.MinuteBarsAsync(InstrumentId, Session, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<Bar>>(bars));
        var replay = await new ReplayCandidateSource(facts).ReadAsync(parameters, Session, ForDate, Ct);

        live.Candidates.Count.ShouldBe(1, "a ticket exists: the comparison is not of empty things");
        live.Unavailable.ShouldBe(1);
        replay.Found.Select(Shape).ShouldBe(live.Candidates.Select(Shape));
        (replay.Rejected, replay.Unavailable).ShouldBe((live.Rejected, live.Unavailable));
    }

    private static (string Cell, string Symbol, string Reason, double? Stop, double? Entry, double? Target, double? Turnover, Guid? Id) Shape(XDrishti.Application.Abstractions.Planning.TicketCandidate c) =>
        (c.Cell.Key, c.Draft.Symbol, c.Draft.Reason, c.Draft.StopLevel, c.Draft.IndicativeEntry, c.Draft.TargetLevel, c.Draft.AverageDailyTurnover, c.InstrumentId);
}
