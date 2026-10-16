using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Accounts;
using XDrishti.Application.Tests.Portfolio.BrokerHistory;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.BrokerHistory.BrokerScenario;
using static XDrishti.Application.Tests.Portfolio.PortfolioFixture;

namespace XDrishti.Application.Tests.Portfolio.Heal;

/// <summary>
/// The nightly deepening of the trade history (ADR 0027): when the trades do not explain a holding in a way earlier history could fix (<c>negative_opening</c>, <c>sell_exceeds_open</c>), the scheduled run reads one earlier year - the year before the earliest date read - and works the
/// lots out again, one year a night, and stops at the first year Dhan refuses or has no trade for, and at the furthest year back. The account's first import date is 2025-01-01, so the first year read is 2024; the broker holds 10 shares and the trades since September buy 15, which no
/// opening quantity can explain. Saturday 2026-10-10 14:30 India time.
/// </summary>
public sealed class DeepenHistoryTests
{
    private static readonly DateOnly Today = new(2026, 10, 10);

    private static async Task<BrokerScenario> MismatchAsync(bool earlierTrade = true)
    {
        var s = await BrokerScenario.StartAsync(qty: 10);
        s.Dhan.Trades.Add(Trade("T1", new DateOnly(2026, 9, 2), TradeSide.Buy, 15, 90m)); // more than the broker holds
        if (earlierTrade)
        {
            s.Dhan.Trades.Add(Trade("E1", new DateOnly(2024, 6, 3), TradeSide.Buy, 1, 50m)); // Dhan has something for 2024
        }

        return s;
    }

    private static async Task<Application.Portfolio.BrokerHistory.BrokerHistoryRunView> NightAsync(BrokerScenario s)
    {
        s.Fx.Clock.Advance(TimeSpan.FromDays(1));
        await s.Fx.Connections.ConnectByLoginAsync(s.Account);
        return await s.SyncOkAsync(scheduled: true, defaultFrom: true);
    }

    private static SyncHealth Trades(BrokerScenario s) => s.Fx.Data.Health.Of(s.Account.Id, SyncHealthKind.Trades);

    private static List<(DateOnly From, DateOnly To)> Windows(BrokerScenario s) => [.. s.Dhan.TradeCalls.Where(c => c.Page == 0).Select(c => (c.From, c.To))];

    [Fact]
    public async Task A_mismatch_earlier_history_could_fix_makes_the_scheduled_run_read_the_year_before_the_first_import_date()
    {
        var s = await MismatchAsync();

        var view = await s.SyncOkAsync(scheduled: true);

        var counts = view.Trades.Counts;
        counts.NotRebuilt.ShouldContain(n => n.Reason == "quantity_mismatch" && n.Detail == "negative_opening");
        counts.DeepenedTo.ShouldBe(new DateOnly(2024, 1, 1));
        var deep = Windows(s).Where(w => w.To < new DateOnly(2025, 1, 1)).ToList();
        deep.Count.ShouldBe(13); // 2024 in windows of at most 30 days
        (deep[0].From, deep[^1].To).ShouldBe((new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
        var health = Trades(s);
        (health.DeepenedTo, health.DeepenExhausted, health.DeepenedOn).ShouldBe((new DateOnly(2024, 1, 1), false, Today));
        s.BrokerTrades().ShouldContain(t => t.ExternalId.EndsWith("E1", StringComparison.Ordinal)); // the earlier trade was stored
        view.Trades.Counts.WindowsTotal.ShouldBe(view.Trades.Counts.WindowsDone);
    }

    [Fact]
    public async Task The_lots_are_worked_out_again_after_the_earlier_year_was_read()
    {
        var s = await MismatchAsync();
        s.Fx.Trades.BrokerSecurityReads.ShouldBe(0);

        await s.SyncOkAsync(scheduled: true);

        s.Fx.Trades.BrokerSecurityReads.ShouldBe(2); // the rebuild after the run's own windows and again after the earlier year
    }

    [Fact]
    public async Task It_reads_one_year_a_night_and_never_two_on_the_same_day()
    {
        var s = await MismatchAsync();
        await s.SyncOkAsync(scheduled: true);
        s.Dhan.TradeCalls.Clear();

        s.Fx.Clock.Advance(TimeSpan.FromHours(1));
        await s.SyncOkAsync(scheduled: true, defaultFrom: true); // the same India day: a retry or a second run

        Windows(s).ShouldAllBe(w => w.From >= Today); // nothing earlier
        Trades(s).DeepenedTo.ShouldBe(new DateOnly(2024, 1, 1));
    }

    [Fact]
    public async Task The_next_night_goes_one_year_further_back_from_the_earliest_date_reached()
    {
        var s = await MismatchAsync();
        await s.SyncOkAsync(scheduled: true);
        s.Dhan.Trades.Add(Trade("E0", new DateOnly(2023, 5, 4), TradeSide.Buy, 1, 40m));
        s.Dhan.TradeCalls.Clear();

        var night = await NightAsync(s);

        var deep = Windows(s).Where(w => w.To < new DateOnly(2024, 1, 1)).ToList();
        (deep[0].From, deep[^1].To).ShouldBe((new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31)));
        night.Trades.Counts.DeepenedTo.ShouldBe(new DateOnly(2023, 1, 1));
        Trades(s).DeepenedTo.ShouldBe(new DateOnly(2023, 1, 1));
    }

    [Fact]
    public async Task A_year_in_which_dhan_has_no_trade_ends_the_deepening_for_good()
    {
        var s = await MismatchAsync(earlierTrade: false); // Dhan has nothing before 2026

        await s.SyncOkAsync(scheduled: true);

        var health = Trades(s);
        (health.DeepenedTo, health.DeepenExhausted).ShouldBe((new DateOnly(2024, 1, 1), true));
        s.Dhan.TradeCalls.Clear();
        await NightAsync(s);
        Windows(s).ShouldAllBe(w => w.From >= Today); // not tried again
    }

    [Fact]
    public async Task A_year_dhan_refuses_ends_the_deepening_and_the_run_still_succeeds()
    {
        var s = await MismatchAsync();
        s.Dhan.TradeFailure = (from, _, _) => from < new DateOnly(2025, 1, 1) ? new BrokerFailure(BrokerFailureKind.Rejected, "DH-910") : null;

        var view = await s.SyncOkAsync(scheduled: true);

        view.Trades.Status.ShouldBe("succeeded");
        var health = Trades(s);
        (health.DeepenedTo, health.DeepenExhausted, health.IsFailing).ShouldBe((null, true, false));
    }

    [Fact]
    public async Task A_year_that_failed_for_a_reason_that_may_pass_is_tried_again_tomorrow()
    {
        var s = await MismatchAsync();
        var failing = true;
        s.Dhan.TradeFailure = (from, _, _) => failing && from < new DateOnly(2025, 1, 1) ? new BrokerFailure(BrokerFailureKind.Unavailable) : null;

        var view = await s.SyncOkAsync(scheduled: true, from: new DateOnly(2025, 1, 1)); // the account's first read, from its first import date

        view.Trades.Status.ShouldBe("succeeded");
        var tried = Trades(s);
        (tried.DeepenedTo, tried.DeepenExhausted, tried.DeepenedOn).ShouldBe((null, false, null));

        failing = false;
        s.Dhan.TradeCalls.Clear();
        await NightAsync(s);
        Trades(s).DeepenedTo.ShouldBe(new DateOnly(2024, 1, 1));
    }

    [Fact]
    public async Task The_deepening_starts_from_the_earliest_date_actually_read_also_when_the_first_import_date_was_moved_earlier_since()
    {
        var s = await MismatchAsync();
        s.Fx.HealSettings.DeepenMaxYears = 0; // the first read does not deepen
        await s.SyncOkAsync(scheduled: true); // read from 2026-09-01
        s.Fx.HealSettings.DeepenMaxYears = 8;
        Ok(await s.Fx.Connections.Accounts.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(s.Account.Id, new PortfolioInput(null, null, FirstImportDate: new DateOnly(2024, 4, 1)), Actor), CancellationToken.None)); // moved earlier than anything read
        s.Dhan.TradeCalls.Clear();

        await NightAsync(s);

        var deep = Windows(s).Where(w => w.To < From).ToList();
        (deep[0].From, deep[^1].To).ShouldBe((new DateOnly(2025, 9, 1), new DateOnly(2026, 8, 31))); // the year before the earliest date read - no gap is left before it
        Trades(s).DeepenedTo.ShouldBe(new DateOnly(2025, 9, 1));
    }

    [Fact]
    public async Task The_furthest_year_back_is_the_setting_and_it_stops_there()
    {
        var s = await MismatchAsync();
        s.Fx.HealSettings.DeepenMaxYears = 2; // back to 2024-10-10 from 2026-10-10

        await s.SyncOkAsync(scheduled: true);

        var deep = Windows(s).Where(w => w.To < new DateOnly(2025, 1, 1)).ToList();
        (deep[0].From, deep[^1].To).ShouldBe((new DateOnly(2024, 10, 10), new DateOnly(2024, 12, 31)));
        var health = Trades(s);
        (health.DeepenedTo, health.DeepenExhausted).ShouldBe((new DateOnly(2024, 10, 10), true));
    }

    [Fact]
    public async Task When_the_first_import_date_is_already_beyond_the_limit_nothing_is_read()
    {
        var s = await MismatchAsync();
        s.Fx.HealSettings.DeepenMaxYears = 1; // back to 2025-10-10: the first import date 2025-01-01 is beyond it

        await s.SyncOkAsync(scheduled: true);

        Windows(s).ShouldAllBe(w => w.From >= Today.AddDays(-40));
        Trades(s).DeepenExhausted.ShouldBeTrue();
    }

    [Fact]
    public async Task With_the_deepening_switched_off_nothing_earlier_is_read()
    {
        var s = await MismatchAsync();
        s.Fx.HealSettings.DeepenMaxYears = 0;

        await s.SyncOkAsync(scheduled: true);

        Windows(s).ShouldAllBe(w => w.From >= Today.AddDays(-40));
        Trades(s).DeepenedOn.ShouldBeNull();
    }

    [Fact]
    public async Task A_run_the_owner_started_does_not_deepen()
    {
        var s = await MismatchAsync();

        await s.SyncOkAsync(scheduled: false);

        Windows(s).ShouldAllBe(w => w.From >= Today.AddDays(-40));
        Trades(s).DeepenedOn.ShouldBeNull();
    }

    [Fact]
    public async Task Holdings_the_trades_explain_or_that_only_need_review_do_not_deepen()
    {
        var s = await BrokerScenario.StartAsync();
        s.Dhan.Trades.AddRange(BaseTrades()); // explains the 30 shares

        await s.SyncOkAsync(scheduled: true);

        Windows(s).ShouldAllBe(w => w.From >= Today.AddDays(-40));
        Trades(s).DeepenedOn.ShouldBeNull();
    }

    [Fact]
    public async Task The_status_tells_how_far_the_history_was_read()
    {
        var s = await MismatchAsync(earlierTrade: false);
        await s.SyncOkAsync(scheduled: true);

        var status = Ok(await s.Status().HandleAsync(new Application.Portfolio.BrokerHistory.GetBrokerHistoryStatusQuery(s.Account.Id), CancellationToken.None));

        (status.DeepenedTo, status.DeepenExhausted).ShouldBe((new DateOnly(2024, 1, 1), true));
    }

    private static T Ok<T>(XDrishti.Domain.Common.Result<T> result)
    {
        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }
}
