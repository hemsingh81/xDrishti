using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Portfolio;
using static XDrishti.Application.Tests.Portfolio.Actions.ActionFixtures;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>The Portfolio notices of the bell (acceptance criterion 16): a warning for each alert that triggered in the last 7 days and, once a day at most, an information that holdings need a decision.</summary>
public sealed class PortfolioNoticesTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<PortfolioNotices> NoticesAsync(ActionsHarness harness) =>
        await harness.Notices().HandleAsync(new GetPortfolioNoticesQuery(), Ct);

    private static ActionsHarness Held(params HoldingData[] holdings)
    {
        var harness = new ActionsHarness(holdings);
        foreach (var holding in holdings.Where(h => h.InstrumentId is not null))
        {
            harness.Classifications.Baskets[holding.InstrumentId!.Value] = [new BasketRef(Guid.NewGuid(), "Core")];
        }

        return harness;
    }

    private static async Task<PriceAlert> TriggeredAsync(ActionsHarness harness, string symbol, AlertDirection direction, decimal level, decimal price, TimeSpan ago)
    {
        var instrument = harness.Data.AddInstrument(symbol);
        var alert = harness.Data.SeedAlert(instrument.Id, direction, level, harness.Now.AddDays(-10));
        await harness.Data.TryTriggerAsync(alert.Id, price, harness.Now - ago, Ct);
        return alert;
    }

    [Fact]
    public async Task A_triggered_alert_is_a_warning_with_its_symbol_condition_level_and_trigger_price()
    {
        var harness = Held();
        var alert = await TriggeredAsync(harness, "INFY", AlertDirection.AtOrBelow, 1200m, 1190.5m, TimeSpan.FromHours(2));

        var notice = (await NoticesAsync(harness)).Items.ShouldHaveSingleItem();

        notice.Id.ShouldBe($"portfolio:alert:{alert.Id}");
        (notice.Kind, notice.Severity, notice.AlertId, notice.InstrumentId).ShouldBe(("price_alert_triggered", "warn", alert.Id, alert.InstrumentId));
        notice.Text.ShouldBe("INFY is at or below ₹1,200 (price ₹1,190.5)");
        notice.At.ShouldBe(harness.Now.AddHours(-2));
    }

    [Fact]
    public async Task An_alert_is_listed_for_seven_days_after_it_triggered_then_not()
    {
        var harness = Held();
        await TriggeredAsync(harness, "NEW", AlertDirection.AtOrAbove, 10m, 11m, TimeSpan.FromDays(6.9));
        await TriggeredAsync(harness, "OLD", AlertDirection.AtOrAbove, 10m, 11m, TimeSpan.FromDays(7.1));

        var notices = await NoticesAsync(harness);

        notices.Items.Select(n => n.Text).ShouldBe(["NEW is at or above ₹10 (price ₹11)"]);
    }

    [Fact]
    public async Task Active_and_cancelled_alerts_raise_nothing()
    {
        var harness = Held();
        var instrument = harness.Data.AddInstrument("INFY");
        harness.Data.SeedAlert(instrument.Id, AlertDirection.AtOrBelow, 1m, harness.Now);
        var cancelled = harness.Data.SeedAlert(instrument.Id, AlertDirection.AtOrBelow, 2m, harness.Now);
        await harness.Data.TryCancelAsync(cancelled.Id, Ct);

        (await NoticesAsync(harness)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Open_items_raise_one_notice_whose_id_carries_the_india_date()
    {
        var harness = Held(Pos("BAD", 70m), Pos("WIN", 140m));

        var notice = (await NoticesAsync(harness)).Items.ShouldHaveSingleItem();

        notice.Id.ShouldBe("portfolio:actions:2026-10-12");
        (notice.Kind, notice.Severity, notice.Text, notice.AlertId).ShouldBe(("actions_need_decision", "info", "2 holdings need a decision", null));
    }

    [Fact]
    public async Task One_item_reads_in_the_singular_and_a_new_day_is_a_new_id()
    {
        var harness = Held(Pos("BAD", 70m));

        var today = (await NoticesAsync(harness)).Items.Single();
        harness.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 18, 45, 0, TimeSpan.Zero)); // already 13 October in India
        var tomorrow = (await NoticesAsync(harness)).Items.Single();

        today.Text.ShouldBe("1 holding needs a decision");
        (today.Id, tomorrow.Id).ShouldBe(("portfolio:actions:2026-10-12", "portfolio:actions:2026-10-13"));
    }

    [Fact]
    public async Task Snoozed_and_done_items_do_not_count_and_without_open_items_there_is_no_notice()
    {
        var bad = Pos("BAD", 70m);
        var harness = Held(bad);
        await harness.DoneAsync($"below_cost:{bad.Id}");

        (await NoticesAsync(harness)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_notices_count_the_accounts_that_count_in_totals()
    {
        var harness = Held(Pos("BAD", 70m));

        await NoticesAsync(harness);

        harness.Queries.LastFilter!.TotalsOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task Both_kinds_are_listed_with_the_alerts_first()
    {
        var harness = Held(Pos("BAD", 70m));
        await TriggeredAsync(harness, "INFY", AlertDirection.AtOrBelow, 1200m, 1100m, TimeSpan.FromMinutes(5));

        (await NoticesAsync(harness)).Items.Select(n => n.Kind).ShouldBe(["price_alert_triggered", "actions_need_decision"]);
    }

    [Fact]
    public async Task The_count_of_open_items_is_kept_for_the_minute_and_forgotten_when_something_is_saved()
    {
        var harness = Held(Pos("BAD", 70m));

        await NoticesAsync(harness);
        await NoticesAsync(harness);
        var reads = harness.Queries.Reads;
        await harness.SnoozeAsync($"below_cost:{harness.Queries.Holdings[0].Id}"); // a decision saves, which drops the cache (and evaluates once itself)
        var afterDecision = harness.Queries.Reads;
        var notices = await NoticesAsync(harness);

        reads.ShouldBe(1);
        harness.Queries.Reads.ShouldBe(afterDecision + 1);
        notices.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failure_to_count_the_open_items_still_returns_the_alert_notices()
    {
        var harness = Held(Pos("BAD", 70m));
        await TriggeredAsync(harness, "INFY", AlertDirection.AtOrBelow, 1200m, 1100m, TimeSpan.FromMinutes(5));
        harness.Data.ThrowOnSettingsRead = true;

        var notices = await NoticesAsync(harness);

        notices.Items.Select(n => n.Kind).ShouldBe(["price_alert_triggered"]);
        harness.Data.ThrowOnSettingsRead = false;
        (await NoticesAsync(harness)).Items.Select(n => n.Kind).ShouldBe(["price_alert_triggered", "actions_need_decision"]); // the failure was not remembered
    }

    [Fact]
    public async Task A_cancelled_request_is_not_swallowed()
    {
        var harness = Held(Pos("BAD", 70m));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(async () => await harness.Notices().HandleAsync(new GetPortfolioNoticesQuery(), cancelled.Token));
    }
}
