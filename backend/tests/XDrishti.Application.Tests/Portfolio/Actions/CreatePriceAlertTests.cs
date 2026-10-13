using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Alerts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.Actions;

/// <summary>Creating a price alert (acceptance criterion 13): the instrument rules (NSE shares and ETFs only), the level, the note, the already-true refusal against the last known price, the limit of 200 active alerts, and the audit event.</summary>
public sealed class CreatePriceAlertTests
{
    private static readonly CancellationToken Ct = TestContext.Current.CancellationToken;

    private static async Task<Result<PriceAlertView>> CreateAsync(ActionsHarness harness, PriceAlertInput input) =>
        await harness.Create().HandleAsync(new CreatePriceAlertCommand(input, ActionsHarness.Owner), Ct);

    private static PriceAlertInput Alert(AlertInstrument instrument, string direction = "at_or_below", decimal level = 1200m, string? note = null) => new(instrument.Id, direction, level, note);

    private static string[] Fields(Result<PriceAlertView> result) => [.. result.Error.Fields!.Select(f => f.Field)];

    [Fact]
    public async Task A_valid_alert_is_created_active_and_audited()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");

        var created = await CreateAsync(harness, Alert(infy, "at_or_below", 1234.5m, "  buy more  "));

        created.IsSuccess.ShouldBeTrue();
        var view = created.Value;
        (view.InstrumentId, view.Symbol, view.Name, view.Direction, view.Level, view.Note, view.Status).ShouldBe((infy.Id, "INFY", "INFY Ltd", "at_or_below", 1234.5m, "buy more", "active"));
        (view.CreatedAt, view.TriggeredAt, view.TriggerPrice).ShouldBe((harness.Now, null, null));
        var audit = harness.Data.Events.ShouldHaveSingleItem();
        (audit.Event, audit.Actor, audit.AccountId).ShouldBe((AccountEventType.PriceAlertCreated, ActionsHarness.Owner, null));
        audit.Detail.ShouldBe("Price alert created: INFY at or below ₹1,234.5");
        (await harness.Data.CountActiveAsync(Ct)).ShouldBe(1);
    }

    [Theory]
    [InlineData("at_or_above")]
    [InlineData("at_or_below")]
    public async Task Both_directions_are_accepted(string direction)
    {
        var harness = new ActionsHarness();
        var etf = harness.Data.AddInstrument("NIFTYBEES", "etf");

        var created = await CreateAsync(harness, Alert(etf, direction));

        created.Value.Direction.ShouldBe(direction);
    }

    [Fact]
    public async Task A_blank_note_is_no_note()
    {
        var harness = new ActionsHarness();

        var created = await CreateAsync(harness, Alert(harness.Data.AddInstrument("INFY"), note: "   "));

        created.Value.Note.ShouldBeNull();
    }

    [Fact]
    public async Task The_instrument_is_required()
    {
        var harness = new ActionsHarness();

        (Fields(await CreateAsync(harness, new PriceAlertInput(null, "at_or_below", 10m, null)))).ShouldBe(["instrumentId"]);
        (Fields(await CreateAsync(harness, new PriceAlertInput(Guid.Empty, "at_or_below", 10m, null)))).ShouldBe(["instrumentId"]);
    }

    [Fact]
    public async Task An_instrument_that_is_not_in_the_master_is_refused()
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, new PriceAlertInput(Guid.NewGuid(), "at_or_below", 10m, null));

        result.Error.Code.ShouldBe("portfolio.invalid");
        result.Error.Fields!.Single().Message.ShouldBe("That instrument is not in the instrument master.");
    }

    [Theory]
    [InlineData("index", "NSE", false, true, "Price alerts are for NSE shares and ETFs; indices and futures are not supported.")]
    [InlineData("future", "NSE", false, true, "Price alerts are for NSE shares and ETFs; indices and futures are not supported.")]
    [InlineData("share", "BSE", false, true, "Price alerts are for NSE shares and ETFs; indices and futures are not supported.")]
    [InlineData("share", "NSE", true, true, "That instrument is no longer listed.")]
    [InlineData("share", "NSE", false, false, "Dhan has no NSE equity id for this instrument, so it cannot be quoted.")]
    public async Task Only_a_listed_quotable_nse_share_or_etf_can_have_an_alert(string type, string exchange, bool retired, bool quotable, string message)
    {
        var harness = new ActionsHarness();
        var instrument = harness.Data.AddInstrument("XYZ", type, exchange, retired, quotable);

        var result = await CreateAsync(harness, Alert(instrument));

        Fields(result).ShouldBe(["instrumentId"]);
        result.Error.Fields!.Single().Message.ShouldBe(message);
        harness.Data.Alerts.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("up")]
    [InlineData("AT_OR_BELOW")]
    [InlineData("")]
    public async Task The_direction_must_be_one_of_the_two(string? direction)
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, new PriceAlertInput(harness.Data.AddInstrument("INFY").Id, direction, 10m, null));

        Fields(result).ShouldBe(["direction"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_000_000.0001)]
    [InlineData(1.00001)]
    public async Task The_level_is_above_zero_at_most_ten_million_with_at_most_four_decimals(double level)
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, new PriceAlertInput(harness.Data.AddInstrument("INFY").Id, "at_or_below", (decimal)level, null));

        Fields(result).ShouldBe(["level"]);
    }

    [Theory]
    [InlineData("0.0001")]
    [InlineData("10000000")]
    [InlineData("1234.5678")]
    public async Task The_edges_of_the_level_are_accepted(string level)
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, new PriceAlertInput(harness.Data.AddInstrument("INFY").Id, "at_or_below", decimal.Parse(level, System.Globalization.CultureInfo.InvariantCulture), null));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_missing_level_is_required()
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, new PriceAlertInput(harness.Data.AddInstrument("INFY").Id, "at_or_below", null, null));

        Fields(result).ShouldBe(["level"]);
    }

    [Fact]
    public async Task The_note_has_at_most_200_characters_of_plain_text()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");

        (await CreateAsync(harness, Alert(infy, note: new string('n', 200)))).IsSuccess.ShouldBeTrue();
        Fields(await CreateAsync(harness, Alert(infy, note: new string('n', 201)))).ShouldBe(["note"]);
        Fields(await CreateAsync(harness, Alert(infy, note: "line one\nline two"))).ShouldBe(["note"]);
    }

    [Fact]
    public async Task A_markup_note_is_kept_as_text()
    {
        var harness = new ActionsHarness();

        var created = await CreateAsync(harness, Alert(harness.Data.AddInstrument("INFY"), note: "<script>alert(1)</script>"));

        created.Value.Note.ShouldBe("<script>alert(1)</script>");
    }

    [Fact]
    public async Task All_the_problems_come_back_at_once()
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, new PriceAlertInput(null, "sideways", 0m, new string('x', 201)));

        Fields(result).ShouldBe(["instrumentId", "direction", "level", "note"], ignoreOrder: true);
    }

    // ---- the condition that already holds ----

    [Theory]
    [InlineData("at_or_below", 1200, 1150, true)] // the price is already below the level
    [InlineData("at_or_below", 1200, 1200, true)] // equal counts
    [InlineData("at_or_below", 1200, 1200.01, false)]
    [InlineData("at_or_above", 1200, 1250, true)]
    [InlineData("at_or_above", 1200, 1200, true)]
    [InlineData("at_or_above", 1200, 1199.99, false)]
    public async Task A_condition_that_already_holds_at_the_last_price_is_refused_with_the_price_in_the_message(string direction, double level, double price, bool refused)
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        harness.Data.KnownPrices[infy.Id] = new KnownPrice((decimal)price, "holding");

        var result = await CreateAsync(harness, Alert(infy, direction, (decimal)level));

        result.IsFailure.ShouldBe(refused);
        if (refused)
        {
            result.Error.Code.ShouldBe("portfolio.invalid");
            Fields(result).ShouldBe(["level"]);
            result.Error.Fields!.Single().Message.ShouldStartWith($"The last price is ₹{price:#,##0.####}, so a price ");
        }
    }

    [Fact]
    public async Task The_last_stored_close_is_used_when_no_holding_has_a_price()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        harness.Data.KnownPrices[infy.Id] = new KnownPrice(1100m, "close");

        var result = await CreateAsync(harness, Alert(infy, "at_or_below", 1200m));

        result.Error.Fields!.Single().Message.ShouldContain("₹1,100");
    }

    [Theory]
    [InlineData("2026-10-09", true)] // the previous session
    [InlineData("2026-10-05", true)] // 5 trading days before Monday 12 October: the oldest close that still counts
    [InlineData("2026-10-02", false)] // 6 trading days
    [InlineData("2026-08-03", false)]
    public async Task A_stored_close_older_than_five_trading_days_is_not_the_last_price(string closedOn, bool refused)
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        harness.Data.KnownPrices[infy.Id] = new KnownPrice(1100m, "close", DateOnly.Parse(closedOn, System.Globalization.CultureInfo.InvariantCulture));

        var result = await CreateAsync(harness, Alert(infy, "at_or_below", 1200m)); // 1,100 is below the level: already true if the close counts

        result.IsFailure.ShouldBe(refused);
    }

    [Fact]
    public async Task A_holdings_price_is_current_whatever_the_age_of_the_stored_closes()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        harness.Data.KnownPrices[infy.Id] = new KnownPrice(1100m, "holding");

        (await CreateAsync(harness, Alert(infy, "at_or_below", 1200m))).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_known_price_the_alert_is_allowed_and_the_next_tick_decides()
    {
        var harness = new ActionsHarness();

        var result = await CreateAsync(harness, Alert(harness.Data.AddInstrument("INFY"), "at_or_below", 1200m));

        result.IsSuccess.ShouldBeTrue();
    }

    // ---- the limit ----

    [Fact]
    public async Task At_most_200_alerts_can_be_active()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        for (var i = 0; i < 199; i++)
        {
            harness.Data.SeedAlert(infy.Id, AlertDirection.AtOrBelow, 1000m + i, harness.Now);
        }

        var two00 = await CreateAsync(harness, Alert(infy, level: 5m));
        var two01 = await CreateAsync(harness, Alert(infy, level: 6m));

        two00.IsSuccess.ShouldBeTrue();
        (two01.Error.Code, two01.Error.Kind).ShouldBe(("portfolio.alert_limit", ErrorKind.Conflict));
        (await harness.Data.CountActiveAsync(Ct)).ShouldBe(200);
        harness.Data.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Triggered_and_cancelled_alerts_do_not_count_against_the_limit()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        for (var i = 0; i < 200; i++)
        {
            harness.Data.SeedAlert(infy.Id, AlertDirection.AtOrBelow, 1000m + i, harness.Now);
        }

        var first = harness.Data.Alerts[0].Alert.Id;
        await harness.Data.TryCancelAsync(first, Ct);
        await harness.Data.TryTriggerAsync(harness.Data.Alerts[1].Alert.Id, 900m, harness.Now, Ct);

        var created = await CreateAsync(harness, Alert(infy, level: 5m));

        created.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_failed_save_rolls_the_alert_and_its_audit_event_back_together()
    {
        var harness = new ActionsHarness();
        var infy = harness.Data.AddInstrument("INFY");
        harness.Data.RefuseNextSave = true;

        await Should.ThrowAsync<InvalidOperationException>(async () => await CreateAsync(harness, Alert(infy)));

        harness.Data.Alerts.ShouldBeEmpty();
        harness.Data.Events.ShouldBeEmpty();
    }
}
