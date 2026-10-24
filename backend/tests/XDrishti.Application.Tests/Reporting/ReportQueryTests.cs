using System.Reflection;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-00 D4: the one query model, every name against a whitelist; an unknown name is <c>reports.invalid</c>, never a silent empty answer. There is no field with which a caller could say the charge sheet was confirmed.</summary>
public sealed class ReportQueryTests
{
    private static ReportQuery Ok(ReportQueryInput input)
    {
        var parsed = ReportQuery.Parse(input);
        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Message : string.Empty);
        return parsed.Value;
    }

    [Fact]
    public void An_empty_query_is_the_default_run_set_gross_the_whole_population_no_filter_and_a_page_of_100()
    {
        var query = Ok(new ReportQueryInput());

        query.Runs.ShouldBeEmpty();
        query.Basis.ShouldBe(ReportBasis.Gross);
        query.Lens.ShouldBeEmpty();
        query.Filters.ToTradeFilter().IsEmpty.ShouldBeTrue();
        query.Metric.ShouldBe(ReportMetric.ExpectancyR);
        query.Page.Size.ShouldBe(100);
        query.Offset.ShouldBe(0);
    }

    [Fact]
    public void Every_field_is_read_and_lists_keep_their_order_and_drop_repeats()
    {
        var run = new string('a', 64);
        var basket = "11111111-2222-3333-4444-555555555555";

        var query = Ok(new ReportQueryInput(
            Runs: run + "," + run,
            Basis: "net",
            Lens: "basket,strategy",
            Strategy: "ORB,VWAP",
            Timeframe: "15m,1D",
            Side: "buy,SELL",
            Style: "swing",
            Exit: "FIXED_RR{rr=2,x=3};ATR_TRAIL",
            Basket: basket,
            Symbol: "NSE:ABC-1,XYZ",
            From: "2026-01-01",
            To: "2026-03-31",
            Metric: "max_drawdown_r",
            Limit: "250",
            Cursor: "o500"));

        query.Runs.ShouldBe([run]);
        query.Basis.ShouldBe(ReportBasis.Net);
        query.Lens.ShouldBe([ReportDimension.Basket, ReportDimension.Strategy]);
        query.Filters.Strategies.ShouldBe(["ORB", "VWAP"]);
        query.Filters.Timeframes.ShouldBe(["15m", "1D"]);
        query.Filters.Sides.ShouldBe(["BUY", "SELL"]);
        query.Filters.Styles.ShouldBe(["swing"]);
        query.Filters.Exits.ShouldBe(["FIXED_RR{rr=2,x=3}", "ATR_TRAIL"]);
        query.Filters.Baskets.ShouldBe([Guid.Parse(basket)]);
        query.Filters.Symbols.ShouldBe(["NSE:ABC-1", "XYZ"]);
        query.Filters.From.ShouldBe(new DateOnly(2026, 1, 1));
        query.Filters.To.ShouldBe(new DateOnly(2026, 3, 31));
        query.Metric.ShouldBe(ReportMetric.MaxDrawdownR);
        query.Page.Size.ShouldBe(250);
        query.Offset.ShouldBe(500);
    }

    [Theory]
    [InlineData("Basis", "both", "basis")]
    [InlineData("Lens", "strategy,planet", "lens 'planet'")]
    [InlineData("Lens", "strategy,strategy", "repeated")]
    [InlineData("Lens", "basket,instrument,strategy,side", "at most 3")]
    [InlineData("Runs", "not-a-run-key", "run key")]
    [InlineData("Strategy", "1ORB", "strategy")]
    [InlineData("Strategy", "ORB;DROP", "strategy")]
    [InlineData("Timeframe", "15M", "timeframe")]
    [InlineData("Side", "LONG", "side")]
    [InlineData("Style", "scalp", "style")]
    [InlineData("Exit", "bad exit", "exit")]
    [InlineData("Basket", "not-a-guid", "basket")]
    [InlineData("Symbol", "A B", "symbol")]
    [InlineData("From", "01/02/2026", "from")]
    [InlineData("To", "2026-13-40", "to")]
    [InlineData("Metric", "sharpe", "metric")]
    [InlineData("Cursor", "999999999999", "cursor")]
    public void A_name_the_whitelist_does_not_know_is_reports_invalid_and_says_which(string field, string value, string mention)
    {
        var input = new ReportQueryInput();
        input = field switch
        {
            "Basis" => input with { Basis = value },
            "Lens" => input with { Lens = value },
            "Runs" => input with { Runs = value },
            "Strategy" => input with { Strategy = value },
            "Timeframe" => input with { Timeframe = value },
            "Side" => input with { Side = value },
            "Style" => input with { Style = value },
            "Exit" => input with { Exit = value },
            "Basket" => input with { Basket = value },
            "Symbol" => input with { Symbol = value },
            "From" => input with { From = value },
            "To" => input with { To = value },
            "Metric" => input with { Metric = value },
            "Cursor" => input with { Cursor = value },
            _ => throw new ArgumentException(field),
        };

        var parsed = ReportQuery.Parse(input);

        parsed.IsFailure.ShouldBeTrue();
        parsed.Error.Code.ShouldBe("reports.invalid");
        parsed.Error.Message.ShouldContain(mention, Shouldly.Case.Insensitive);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1001")]
    [InlineData("many")]
    [InlineData("1.5")]
    [InlineData("99999999999")]
    public void A_page_size_outside_1_to_1000_or_not_a_whole_number_is_refused(string limit)
    {
        ReportQuery.Parse(new ReportQueryInput(Limit: limit)).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void A_range_that_ends_before_it_starts_is_refused_and_all_problems_come_together()
    {
        var parsed = ReportQuery.Parse(new ReportQueryInput(From: "2026-03-01", To: "2026-01-01", Metric: "nope", Basis: "x"));

        parsed.Error.Message.ShouldContain("from is after to");
        parsed.Error.Message.ShouldContain("metric");
        parsed.Error.Message.ShouldContain("basis");
    }

    [Fact]
    public void More_than_fifty_values_in_a_list_are_refused()
    {
        var symbols = string.Join(',', Enumerable.Range(0, 51).Select(i => "S" + i));

        ReportQuery.Parse(new ReportQueryInput(Symbol: symbols)).Error.Message.ShouldContain("more than 50");
    }

    [Fact]
    public void A_rejected_value_is_echoed_cut_short_and_without_control_characters()
    {
        var parsed = ReportQuery.Parse(new ReportQueryInput(Lens: "x\n\u0001" + new string('y', 200)));

        parsed.Error.Message.ShouldNotContain("\n");
        parsed.Error.Message.Length.ShouldBeLessThan(400);
    }

    [Fact]
    public void Regime_and_account_are_names_the_query_accepts_so_that_the_engine_can_refuse_them_with_the_reason()
    {
        Ok(new ReportQueryInput(Lens: "regime")).Lens.ShouldBe([ReportDimension.Regime]);
        Ok(new ReportQueryInput(Lens: "account")).Lens.ShouldBe([ReportDimension.Account]);
    }

    [Fact]
    public void The_cursor_of_a_page_round_trips()
    {
        var cursor = ReportQuery.CursorFor(1500);

        Ok(new ReportQueryInput(Cursor: cursor)).Offset.ShouldBe(1500);
    }

    [Fact]
    public void No_field_of_the_query_can_say_that_the_charge_sheet_was_confirmed()
    {
        foreach (var type in new[] { typeof(ReportQueryInput), typeof(ReportQuery), typeof(ReportFilters) })
        {
            var names = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(p => p.Name).ToList();

            names.ShouldNotContain(n => n.Contains("confirm", StringComparison.OrdinalIgnoreCase) || n.Contains("sheet", StringComparison.OrdinalIgnoreCase), type.Name);
            type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).ShouldNotContain(p => p.PropertyType == typeof(ChargeSheetConfirmation), type.Name);
        }
    }

    [Fact]
    public void The_wire_names_of_the_figures_are_snake_case_and_all_read_back()
    {
        foreach (var metric in Enum.GetValues<ReportMetric>())
        {
            var name = ReportNames.Name(metric);

            name.ShouldBe(name.ToLowerInvariant());
            ReportNames.TryParseMetric(name, out var back).ShouldBeTrue();
            back.ShouldBe(metric);
        }

        ReportNames.Name(ReportMetric.ExpectancyLowerR).ShouldBe("expectancy_lower_r");
        ReportNames.Name(ReportMetric.AvgHoldingSessions).ShouldBe("avg_holding_sessions");
    }
}
