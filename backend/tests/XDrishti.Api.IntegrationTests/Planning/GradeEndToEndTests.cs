using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
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
/// (c) plan for Mar 10 (built on Mar 9): decided on F, filled on Mar 11: graded
/// (b) intraday ORB: the F1 day of P3-03 on Mar 3 after a flat Mar 2: decided and filled on F, target: graded +2R
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

    private async Task<(GradeOutcome Outcome, StoredGrade? Grade)> GradeAsync(NewPlan plan)
    {
        Factory.Clock.Pin(Today);
        using var scope = Factory.Services.CreateScope();
        var plans = scope.ServiceProvider.GetRequiredService<IPlanStore>();
        var published = await plans.PublishAsync(plan, Ct);
        var outcome = await scope.ServiceProvider.GetRequiredService<ICommandHandler<GradePlansCommand, GradeOutcome>>().HandleAsync(new GradePlansCommand(plan.ForDate), Ct);
        outcome.IsSuccess.ShouldBeTrue(outcome.IsFailure ? outcome.Error.Message : string.Empty);
        var grades = await scope.ServiceProvider.GetRequiredService<IGradeStore>().ForPlansAsync([published.Id], Ct);
        return (outcome.Value, grades.Values.SingleOrDefault());
    }

    private const string SwingCell = "DAILY_UP|1D|BUY|swing|FIXED_RR{rr=2}|*";

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
    public async Task A_swing_ticket_decided_on_F_and_filled_on_the_next_session_is_graded_within_the_read()
    {
        var (_, symbol) = await InstrumentAsync("DUC", DailySeries(spikeOnF: true, spikeLater: false));
        var hash = await StoreStrategyAsync("DAILY_UP", "swing", DailyUp);

        var (outcome, grade) = await GradeAsync(Plan("e2e-c", Mar(10), symbol, hash, "DAILY_UP", SwingCell));

        outcome.Graded.ShouldBe(1, grade?.Grade.Grade.Detail);
        grade!.Grade.Grade.Detail.ShouldContain("entered 2026-03-11");
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
