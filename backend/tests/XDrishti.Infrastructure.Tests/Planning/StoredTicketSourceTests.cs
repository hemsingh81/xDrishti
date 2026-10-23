#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Market.Search;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Infrastructure.Planning;
using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Tests.Planning;

/// <summary>The glue of the ticket source (P7-04) over fake ports: which cells and instruments are read, how a reason is grouped and capped.</summary>
public sealed class StoredTicketSourceTests
{
    private static readonly DateOnly Session = new(2026, 5, 22);
    private static readonly DateOnly ForDate = new(2026, 5, 25);
    private static readonly Guid InstrumentId = Guid.NewGuid();

    private readonly ICellStatsStore _stats = Substitute.For<ICellStatsStore>();
    private readonly ILearningChangelog _changelog = Substitute.For<ILearningChangelog>();
    private readonly IStrategyStore _strategies = Substitute.For<IStrategyStore>();
    private readonly IBasketQueries _baskets = Substitute.For<IBasketQueries>();
    private readonly IBarStore _bars = Substitute.For<IBarStore>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private StoredTicketSource Source() => new(_stats, _changelog, _strategies, _baskets, _bars, _calendar);

    private static StoredCellStats Cell(string strategy, string side = "BUY", string style = "swing", string exit = "FIXED_RR")
    {
        var id = new CellId(strategy, "1D", side, style, exit);
        var row = new CellStatsRow(id, CellWindow.All, new DateOnly(2026, 5, 21), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats("k", DateTimeOffset.UnixEpoch, new CellStatsOptions(), "", row);
    }

    private void Given(params StoredCellStats[] cells)
    {
        _stats.ListLatestBeforeAsync(ForDate, null, "all", Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>(cells));
        _changelog.HistoryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([]));
        _calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new TradingCalendar()));
        var instrument = new InstrumentSummary(InstrumentId, "AAA", "Aaa", "Aaa", "EQUITY", null, null, null, 1, null, null, false, false, false, TrackedInfo.Never);
        var member = new BasketMemberView(Guid.NewGuid(), instrument, 0, DateTimeOffset.UnixEpoch, null, null, "owner", null, null);
        _baskets.ListAsync(false, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<BasketView>>(
            [new BasketView(Guid.NewGuid(), "b", "#fff", "", null, 0, 1, DateTimeOffset.UnixEpoch, null, "manual", false, null, null)]));
        _baskets.MembersAsync(Arg.Any<Guid>(), null, null, Arg.Any<CancellationToken>()).Returns(ci => ValueTask.FromResult<BasketMembers?>(new BasketMembers(ci.Arg<Guid>(), "b", null, null, 1, [member])));
        _bars.ReadAsync(InstrumentId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<Bar>>([]));
    }

    private static StoredVersion Version(string id, string text, string hash) => new(id, 1, 1, hash, text, "{}", null, null, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task No_cell_statistics_gives_an_empty_result()
    {
        Given();

        var result = await Source().ReadAsync(Session, ForDate, Ct);

        result.Value.Candidates.ShouldBeEmpty();
        result.Value.Unavailable.ShouldBe(0);
    }

    [Fact]
    public async Task A_cell_whose_strategy_is_not_stored_is_unavailable_and_grouped_by_strategy()
    {
        Given(Cell("MISSING"), Cell("MISSING", "SELL"));
        _strategies.GetVersionAsync("MISSING", null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(null));

        var result = await Source().ReadAsync(Session, ForDate, Ct);

        result.Value.Unavailable.ShouldBe(2);
        result.Value.UnavailableReasons.ShouldBe(["strategy or cell not usable: MISSING"]);
        await _strategies.Received(1).GetVersionAsync("MISSING", null, Arg.Any<CancellationToken>()); // read once per strategy, not per cell
    }

    [Fact]
    public async Task More_distinct_reasons_than_the_cap_are_counted_but_not_all_listed()
    {
        var cells = Enumerable.Range(0, 30).Select(i => Cell("S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture))).ToArray();
        Given(cells);
        _strategies.GetVersionAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(null));

        var result = await Source().ReadAsync(Session, ForDate, Ct);

        result.Value.Unavailable.ShouldBe(30);
        result.Value.UnavailableReasons.Count.ShouldBe(20);
    }

    [Fact]
    public async Task A_stored_text_that_does_not_read_is_unavailable_once_per_cell_without_reading_bars()
    {
        Given(Cell("BASE_BREAKOUT"));
        _strategies.GetVersionAsync("BASE_BREAKOUT", null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(Version("BASE_BREAKOUT", "not: [a strategy", new string('0', 64))));

        var result = await Source().ReadAsync(Session, ForDate, Ct);

        result.Value.Unavailable.ShouldBe(1);
        result.Value.UnavailableReasons.ShouldBe(["the stored strategy text does not read"]);
    }

    [Fact]
    public async Task Missing_history_is_grouped_into_one_reason_and_a_complete_session_makes_a_ticket()
    {
        var (text, hash) = Seed("BASE_BREAKOUT");
        Given(Cell("BASE_BREAKOUT"));
        _strategies.GetVersionAsync("BASE_BREAKOUT", null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredVersion?>(Version("BASE_BREAKOUT", text, hash)));

        var empty = await Source().ReadAsync(Session, ForDate, Ct);
        _bars.ReadAsync(InstrumentId, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<Bar>>(Quiet(100)));
        var full = await Source().ReadAsync(Session, ForDate, Ct);

        empty.Value.UnavailableReasons.ShouldBe(["session not complete in the bars"]);
        var ticket = full.Value.Candidates.ShouldHaveSingleItem();
        ticket.Draft.Symbol.ShouldBe("AAA");
        ticket.Draft.Verdict.ShouldBe(TicketVerdict.Conditional);
    }

    [Fact]
    public async Task A_cancelled_token_stops_the_read()
    {
        Given(Cell("X"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await Source().ReadAsync(Session, ForDate, cts.Token));
    }

    private static (string Text, string Hash) Seed(string id)
    {
        var text = StrategySeeder.EmbeddedTexts().First(t => t.Name.StartsWith(id, StringComparison.Ordinal)).Text;
        return (text, CanonicalText.Sha256Hex(CanonicalStrategy.Json(StrategyFile.Read(text).Definition!)));
    }

    /// <summary>A quiet uptrend of complete sessions ending on <see cref="Session"/>.</summary>
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
            var volume = i == days.Count - 1 ? 3000 : 1000;
            var open = new DateTimeOffset(days[i].ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)).AddMinutes(SessionClock.OpenMinute);
            for (var m = 0; m < SessionClock.RegularBars; m++)
            {
                bars.Add(new Bar(open.AddMinutes(m).ToUnixTimeSeconds(), price, price + 0.05, price - 0.05, price, volume));
            }
        }

        return bars;
    }
}
