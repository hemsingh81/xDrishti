using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Market.Baskets;
using XDrishti.Application.Planning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// Spec P7-13, the whole path with the real simulator, the real stores and fabricated bars (no fake of the engine): a plan is stored, its tickets are graded by <see cref="GradePlansCommand"/>. The window starts on the session the ticket was built
/// on (D) because the engine cuts by the day of the decision; only trades the ticket could know are graded.
/// <code>
/// swing "DAILY_UP" (buy when close &gt; sma(3), stop = lowest low of 3 days, target 2R):
///   Mar 2-9 flat 100 | Mar 10 flat 110 (signal at its close) | Mar 11 opens 110, spikes to 135 at 10:00 (target 130) | Mar 12 flat 135
/// (a) plan for Mar 11 (built on Mar 10): decided at the close of D, filled at the open of F, target hit on F: graded, ~2R
/// (c) plan for Mar 10 (built on Mar 9): the daily decision at the close of Mar 10 is the ticket of Mar 11, not of Mar 10: not triggered
/// (b) intraday ORB: the F1 day of P3-03 on Mar 3 after a flat Mar 2: decided and filled on F, target: graded +2R (the F-only window of the first version also passes this one)
/// (e) ORB trades on D (Mar 2), F (Mar 3) is flat: the D trade is not the outcome of the ticket for F: not triggered
/// (d) no spike on Mar 11 and the spike on Mar 25, after the pinned "today" (Mar 20): the position is open at the end of what may be read, so the ticket is pending and nothing is written
/// </code>
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class GradeEndToEndTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string DailyUp = """
        strategy:
          schema: 1
          id: DAILY_UP
          style: swing
          timeframes:
            - 1D
          sides:
            - BUY
          params:
            rr: 2
          trigger:
            all:
              - "close > sma(3)"
          entry:
            type: at_close
          stop:
            type: swing_low
            lookback: 3
          exits:
            - policy: FIXED_RR
              rr: [rr]
          time_rules:
            max_hold_sessions: 5
        """;

    private static readonly DateTimeOffset Today = new(2026, 3, 20, 6, 30, 0, TimeSpan.Zero);

    private static DateOnly Mar(int day) => new(2026, 3, day);

    /// <summary>A whole session of 375 one-minute bars at one price, with an optional spike bar at a minute of the session.</summary>
    private static Bar[] Session(DateOnly date, double price, int? spikeAt = null, double spikeHigh = 0)
    {
        var bars = new List<Bar>();
        var level = price;
        for (var m = 0; m < SessionClock.RegularBars; m++)
        {
            var epoch = Day.Epoch(date, Day.Minute("09:15") + m);
            if (m == spikeAt)
            {
                bars.Add(new Bar(epoch, level, spikeHigh, level, spikeHigh, Day.MinuteVolume));
                level = spikeHigh;
            }
            else
            {
                bars.Add(new Bar(epoch, level, level, level, level, Day.MinuteVolume));
            }
        }

        return [.. bars];
    }

    private static Bar[] DailySeries(bool spikeOnF, bool spikeLater)
    {
        var days = new List<Bar[]>();
        foreach (var d in new[] { 2, 3, 4, 5, 6, 9 })
        {
            days.Add(Session(Mar(d), 100));
        }

        days.Add(Session(Mar(10), 110));
        days.Add(spikeOnF ? Session(Mar(11), 110, 45, 135) : Session(Mar(11), 110));
        days.Add(Session(Mar(12), spikeOnF ? 135 : 110));
        if (spikeLater)
        {
            days.Add(Session(Mar(25), 110, 45, 135));
        }

        return ReplayDays.Join([.. days]);
    }

    private async Task<(Guid Instrument, string Symbol)> InstrumentAsync(string ticker, IEnumerable<Bar> bars)
    {
        var id = await SeedAsync(ticker);
        await using (var connection = await OpenAsync())
        {
            await ExecuteAsync(connection, "UPDATE market.instruments SET tick_size = 5 WHERE id = @id", ("id", id));
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IBarStore>();
            var list = bars.ToArray();
            await using var session = await store.BeginMergeAsync(id, BarSource.Dhan, Ct);
            await session.AddAsync(list, Ct);
            await session.CompleteAsync(null, Ct);
        }

        using var created = await Client.PostJsonAsync("/api/baskets", new { name = "grading " + ticker }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var basket = (await created.Content.ReadFromJsonAsync<BasketView>(Ct))!.Id;
        using var added = await Client.PostJsonAsync($"/api/baskets/{basket}/members", new { instrumentIds = new[] { id } }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK, await added.Content.ReadAsStringAsync(Ct));
        return (id, "NSE:" + ticker);
    }

    private async Task<string> StoreStrategyAsync(string id, string style, string text)
    {
        var definition = StrategyFile.Read(text).Definition!;
        var canonical = CanonicalStrategy.Json(definition);
        var hash = CanonicalText.Sha256Hex(canonical);
        using var scope = Factory.Services.CreateScope();
        var appended = await scope.ServiceProvider.GetRequiredService<IStrategyStore>().AppendAsync(new NewVersion(id, style, text, canonical, hash, Seed: true), Ct);
        appended.IsSuccess.ShouldBeTrue(appended.IsFailure ? appended.Error.Message : string.Empty);
        return hash;
    }

    private static string Orb()
    {
        var assembly = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".Seed.Strategies.ORB.yaml", StringComparison.Ordinal));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
        return reader.ReadToEnd();
    }

    private static NewPlan Plan(string key, DateOnly forDate, string symbol, string hash, string strategy, string cell) =>
        new(key, forDate, new DateTimeOffset(forDate.Year, forDate.Month, forDate.Day, 3, 35, 0, TimeSpan.Zero), 1, new string('c', 64), "cell statistics only", true, "test", DateTimeOffset.UnixEpoch,
            [new NewPlanItem(1, TicketGroup.Primary, symbol, "BUY", strategy, hash, "t", "s", "g", "v", [], new PlanEvidence(cell, 40, "r", "b", null, null, [], null), [])]);

    private async Task<(GradeOutcome Outcome, StoredGrade? Grade)> GradeAsync(NewPlan plan, IServiceProvider? services = null)
    {
        Factory.Clock.Pin(Today);
        using var scope = (services ?? Factory.Services).CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var published = await plans.PublishAsync(plan, Ct);
        var outcome = await scope.ServiceProvider.GetRequiredService<ICommandHandler<GradePlansCommand, GradeOutcome>>().HandleAsync(new GradePlansCommand(plan.ForDate), Ct);
        outcome.IsSuccess.ShouldBeTrue(outcome.IsFailure ? outcome.Error.Message : string.Empty);
        var grades = await scope.ServiceProvider.GetRequiredService<IGradeStore>().ForPlansAsync([published.Id], Ct);
        return (outcome.Value, grades.Values.SingleOrDefault());
    }

    private const string SwingCell = "DAILY_UP|1D|BUY|swing|FIXED_RR{rr=2}|*";

    private sealed class FixedGradeSettings(string? sheet, decimal capital) : IGradeSettings
    {
        public string? CostSheetId => sheet;

        public decimal Capital => capital;
    }

    private static Bar[] Series(params (int Day, double Price, int? Spike, double High)[] days) =>
        ReplayDays.Join([.. days.Select(d => Session(Mar(d.Day), d.Price, d.Spike, d.High))]);

    private static (int, double, int?, double) Flat(int day, double price) => (day, price, null, 0);

    private async Task<string> DailyTicketAsync(string ticker, Bar[] bars)
    {
        var (_, symbol) = await InstrumentAsync(ticker, bars);
        _hash = await StoreStrategyAsync("DAILY_UP", "swing", DailyUp);
        return symbol;
    }

    private string _hash = string.Empty;

    // The replay starts at the first stored session and only some of its counters are cut to the window; the flagged days and the no-trade reasons cover the whole history. These cases pin that nothing from another day decides a ticket.

    [Fact]
    public async Task A_price_gap_long_before_the_decision_day_does_not_leave_a_ticket_with_no_trade_pending()
    {
        var symbol = await DailyTicketAsync("DUF", Series(Flat(2, 100), Flat(3, 100), Flat(4, 150), Flat(5, 150), Flat(6, 150), Flat(9, 150), Flat(10, 150), Flat(11, 150)));

        var (outcome, grade) = await GradeAsync(Plan("e2e-f", Mar(11), symbol, _hash, "DAILY_UP", SwingCell));

        outcome.NotTriggered.ShouldBe(1, outcome.Pending + " pending");
        grade!.Grade.Grade.Detail.ShouldContain("close of 2026-03-10");
    }

    [Fact]
    public async Task A_price_gap_long_before_the_decision_day_does_not_stop_a_later_signal_from_being_graded()
    {
        var symbol = await DailyTicketAsync("DUG", Series(Flat(2, 100), Flat(3, 100), Flat(4, 150), Flat(5, 150), Flat(6, 150), Flat(9, 150), Flat(10, 165), (11, 165, 45, 200)));

        var (outcome, grade) = await GradeAsync(Plan("e2e-g", Mar(11), symbol, _hash, "DAILY_UP", SwingCell));

        outcome.Graded.ShouldBe(1, grade?.Grade.Grade.Detail);
        grade!.Grade.Grade.ExitReason.ShouldBe("Target");
    }

    [Fact]
    public async Task A_price_gap_on_the_decision_day_leaves_the_ticket_pending_not_final()
    {
        var symbol = await DailyTicketAsync("DUH", Series(Flat(2, 100), Flat(3, 100), Flat(4, 100), Flat(5, 100), Flat(6, 100), Flat(9, 100), Flat(10, 150), Flat(11, 150)));

        var (outcome, grade) = await GradeAsync(Plan("e2e-h", Mar(11), symbol, _hash, "DAILY_UP", SwingCell));

        outcome.Pending.ShouldBe(1);
        grade.ShouldBeNull();
    }

    [Fact]
    public async Task An_order_left_resting_at_the_end_of_a_segment_long_ago_does_not_leave_a_later_ticket_pending()
    {
        // The signal of Mar 9's close rests when Mar 10 opens on a price gap (the replay counts it as an order pending at the end). A ticket for Mar 13 has nothing to do with it.
        var symbol = await DailyTicketAsync("DUI", Series(Flat(2, 100), Flat(3, 100), Flat(4, 100), Flat(5, 100), Flat(6, 100), Flat(9, 110), Flat(10, 160), Flat(11, 160), Flat(12, 160), Flat(13, 160)));

        var (outcome, grade) = await GradeAsync(Plan("e2e-i", Mar(13), symbol, _hash, "DAILY_UP", SwingCell));

        outcome.NotTriggered.ShouldBe(1, outcome.Pending + " pending");
        grade.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_ticket_of_the_session_after_a_gap_is_pending_because_its_fill_day_is_flagged()
    {
        var symbol = await DailyTicketAsync("DUJ", Series(Flat(2, 100), Flat(3, 100), Flat(4, 100), Flat(5, 100), Flat(6, 100), Flat(9, 110), Flat(10, 160), Flat(11, 160)));

        var (outcome, _) = await GradeAsync(Plan("e2e-j", Mar(10), symbol, _hash, "DAILY_UP", SwingCell));

        outcome.Pending.ShouldBe(1);
    }

    [Fact]
    public async Task A_limit_miss_on_a_later_day_does_not_change_the_reason_of_a_ticket_that_did_not_trigger()
    {
        // Mar 10's close gives a signal and Mar 11 opens 4.5 percent higher, past the entry limit: that is the ticket of Mar 11. The ticket for Mar 10 (built on Mar 9) had no signal.
        var symbol = await DailyTicketAsync("DUK", Series(Flat(2, 100), Flat(3, 100), Flat(4, 100), Flat(5, 100), Flat(6, 100), Flat(9, 100), Flat(10, 110), Flat(11, 115), Flat(12, 115)));

        var (outcome, grade) = await GradeAsync(Plan("e2e-k", Mar(10), symbol, _hash, "DAILY_UP", SwingCell));

        outcome.NotTriggered.ShouldBe(1);
        grade!.Grade.Grade.Detail.ShouldNotContain("limit", Case.Insensitive);
        grade.Grade.Grade.Detail.ShouldContain("did not hold or the order did not fill");
    }

    [Fact]
    public async Task An_intraday_ticket_with_a_price_gap_in_its_history_is_not_left_pending()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(Mar(2), 99.00), ReplayDays.Flat(Mar(3), 150.00), ReplayDays.Flat(Mar(4), 150.00));
        var (_, symbol) = await InstrumentAsync("DUM", bars);
        var hash = await StoreStrategyAsync("ORB", "intraday", Orb());

        var (outcome, grade) = await GradeAsync(Plan("e2e-m", Mar(4), symbol, hash, "ORB", "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*"));

        outcome.NotTriggered.ShouldBe(1, outcome.Pending + " pending");
        grade.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_trade_dropped_by_the_sizing_rule_is_final_for_the_decision_day_and_not_for_a_later_day()
    {
        var symbol = await DailyTicketAsync("DUL", DailySeries(spikeOnF: true, spikeLater: false));
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ICostSheetStore>().SaveAsync(FixtureSheet.Sheet, "test", DateTimeOffset.UnixEpoch, Ct);
            (await scope.ServiceProvider.GetRequiredService<IPlatformUnitOfWork>().TrySaveAsync(Ct)).ShouldBeTrue();
        }

        // 500 rupees of capital risk 5 rupees per trade; the risk per share is 10, so sizing gives no whole share and the replay drops the trade
        using var host = Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IGradeSettings>(new FixedGradeSettings(FixtureSheet.Id, 500m))));

        var onD = await GradeAsync(Plan("e2e-l1", Mar(11), symbol, _hash, "DAILY_UP", SwingCell), host.Services);
        var later = await GradeAsync(Plan("e2e-l2", Mar(10), symbol, _hash, "DAILY_UP", SwingCell), host.Services);

        onD.Outcome.NotGradable.ShouldBe(1, onD.Grade?.Grade.Grade.Detail);
        onD.Grade!.Grade.Grade.Detail.ShouldContain("sizing or cost rule");
        later.Outcome.NotTriggered.ShouldBe(1, later.Grade?.Grade.Grade.Detail);
    }

    [Fact]
    public async Task A_daily_signal_at_the_close_of_D_filled_at_the_open_of_F_is_graded_not_left_as_not_triggered()
    {
        var (_, symbol) = await InstrumentAsync("DUA", DailySeries(spikeOnF: true, spikeLater: false));
        var hash = await StoreStrategyAsync("DAILY_UP", "swing", DailyUp);

        var (outcome, grade) = await GradeAsync(Plan("e2e-a", Mar(11), symbol, hash, "DAILY_UP", SwingCell));

        outcome.Graded.ShouldBe(1, grade?.Grade.Grade.Detail);
        grade!.Grade.Grade.Status.ShouldBe(GradeStatus.Graded);
        grade.Grade.Grade.ExitReason.ShouldBe("Target");
        grade.Grade.Grade.R!.Value.ShouldBeInRange(1.5, 2.0);
        grade.Grade.Grade.Detail.ShouldContain("entered 2026-03-11");
        grade.Grade.RunKey.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_daily_decision_at_the_close_of_F_is_not_graded_as_the_outcome_of_the_ticket_for_F()
    {
        // The signal of Mar 10's close fills on Mar 11 and belongs to the ticket for Mar 11 (case a). The ticket for Mar 10 was built on Mar 9, whose close gave no signal: it did not trigger.
        var (_, symbol) = await InstrumentAsync("DUC", DailySeries(spikeOnF: true, spikeLater: false));
        var hash = await StoreStrategyAsync("DAILY_UP", "swing", DailyUp);

        var (outcome, grade) = await GradeAsync(Plan("e2e-c", Mar(10), symbol, hash, "DAILY_UP", SwingCell));

        outcome.NotTriggered.ShouldBe(1, grade?.Grade.Grade.Detail);
        outcome.Graded.ShouldBe(0);
        grade!.Grade.Grade.Detail.ShouldContain("close of 2026-03-09");
    }

    [Fact]
    public async Task An_intraday_trade_decided_on_D_is_not_the_outcome_of_the_ticket_for_F()
    {
        // ORB trades on Mar 2 (D) and the target is hit that day; Mar 3 (F) is flat. The ticket for Mar 3 did not trigger. (The F-only window of the first version gave the same answer here; the case guards the D-to-R window.)
        var bars = ReplayDays.Join(ReplayDays.F1(Mar(2)), ReplayDays.Flat(Mar(3), 105.00));
        var (_, symbol) = await InstrumentAsync("DUE", bars);
        var hash = await StoreStrategyAsync("ORB", "intraday", Orb());

        var (outcome, grade) = await GradeAsync(Plan("e2e-e", Mar(3), symbol, hash, "ORB", "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*"));

        outcome.NotTriggered.ShouldBe(1, grade?.Grade.Grade.Detail);
        outcome.Graded.ShouldBe(0);
    }

    [Fact]
    public async Task An_intraday_ticket_decided_and_filled_on_F_is_graded_at_the_target()
    {
        var bars = ReplayDays.Join(ReplayDays.Flat(Mar(2), 99.00), ReplayDays.F1(Mar(3)), ReplayDays.Flat(Mar(4), 105.00));
        var (_, symbol) = await InstrumentAsync("DUB", bars);
        var hash = await StoreStrategyAsync("ORB", "intraday", Orb());

        var (outcome, grade) = await GradeAsync(Plan("e2e-b", Mar(3), symbol, hash, "ORB", "ORB|15m|BUY|intraday|FIXED_RR{rr=2}|*"));

        outcome.Graded.ShouldBe(1, grade?.Grade.Grade.Detail);
        grade!.Grade.Grade.ExitReason.ShouldBe("Target");
        grade.Grade.Grade.R!.Value.ShouldBeGreaterThan(1.0);
        grade.Grade.Grade.Detail.ShouldContain("entered 2026-03-03");
    }

    [Fact]
    public async Task Bars_after_the_last_closed_session_are_never_read_so_a_position_open_at_that_point_stays_pending()
    {
        var (_, symbol) = await InstrumentAsync("DUD", DailySeries(spikeOnF: false, spikeLater: true));
        var hash = await StoreStrategyAsync("DAILY_UP", "swing", DailyUp);

        var (outcome, grade) = await GradeAsync(Plan("e2e-d", Mar(11), symbol, hash, "DAILY_UP", SwingCell));

        outcome.Pending.ShouldBe(1);
        outcome.Graded.ShouldBe(0);
        grade.ShouldBeNull();
    }
}
