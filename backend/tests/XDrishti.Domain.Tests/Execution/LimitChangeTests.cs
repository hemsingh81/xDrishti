using XDrishti.Domain.Execution.Limits;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Tests.Execution;

/// <summary>Spec P12-09 criterion 9 and the sheet rules: each field up is a raise and down a lowering, a mixed change is a raise, setting an unset value is a raise; a raise takes effect at 09:00 India time after one full closed session.</summary>
public sealed class LimitChangeTests
{
    private static readonly ExecutionLimits Sheet = new(6, 60_000m, 150_000m, 1_500m, 3, ["TATAPOWER", "INFY", "SBIN"], new TimeOnly(9, 20), new TimeOnly(14, 45), [LimitsCeilings.EquityIntraday], false);

    public static TheoryData<string, string> Raises() => new()
    {
        { "orders", ExecutionLimits.FieldOrders },
        { "notional order", ExecutionLimits.FieldNotionalOrder },
        { "notional day", ExecutionLimits.FieldNotionalDay },
        { "loss", ExecutionLimits.FieldLoss },
        { "positions", ExecutionLimits.FieldPositions },
        { "symbol added", ExecutionLimits.FieldAllowList },
        { "earlier start", ExecutionLimits.FieldWindow },
        { "later end", ExecutionLimits.FieldWindow },
        { "exit all off", "exitAllOnLossHalt" },
    };

    private static (ExecutionLimits From, ExecutionLimits To) Up(string what) => what switch
    {
        "orders" => (Sheet, Sheet with { MaxOrdersPerDay = 7 }),
        "notional order" => (Sheet, Sheet with { MaxNotionalPerOrder = 60_000.01m }),
        "notional day" => (Sheet, Sheet with { MaxNotionalPerDay = 150_000.01m }),
        "loss" => (Sheet, Sheet with { MaxLossPerDay = 1_501m }),
        "positions" => (Sheet, Sheet with { MaxOpenPositions = 4 }),
        "symbol added" => (Sheet, Sheet with { AllowList = ["TATAPOWER", "INFY", "SBIN", "ITC"] }),
        "earlier start" => (Sheet, Sheet with { WindowStart = new TimeOnly(9, 19) }),
        "later end" => (Sheet, Sheet with { WindowEnd = new TimeOnly(14, 46) }),
        _ => (Sheet with { ExitAllOnLossHalt = true }, Sheet),
    };

    [Theory]
    [MemberData(nameof(Raises))]
    public void A_field_that_lets_more_through_is_a_raise(string what, string field)
    {
        var (from, to) = Up(what);

        var raise = LimitChange.Classify(from, to);
        var back = LimitChange.Classify(to, from);

        raise.IsRaise.ShouldBeTrue();
        raise.Loosened.ShouldBe([field]);
        back.IsRaise.ShouldBeFalse(what + " back down is a lowering");
    }

    [Fact]
    public void A_mixed_change_counts_as_a_raise_and_an_unchanged_sheet_is_not()
    {
        var mixed = LimitChange.Classify(Sheet, Sheet with { MaxOrdersPerDay = 3, AllowList = ["TATAPOWER", "ITC"] });

        mixed.IsRaise.ShouldBeTrue();
        mixed.Loosened.ShouldBe([ExecutionLimits.FieldAllowList]);
        LimitChange.Classify(Sheet, Sheet with { AllowList = ["SBIN", "INFY", "TATAPOWER"] }).IsRaise.ShouldBeFalse();
    }

    [Fact]
    public void Unsetting_a_value_lowers_and_setting_one_raises_and_no_sheet_is_the_empty_sheet()
    {
        LimitChange.Classify(Sheet, Sheet with { MaxLossPerDay = null, WindowStart = null, WindowEnd = null, Products = [] }).IsRaise.ShouldBeFalse();
        LimitChange.Classify(null, ExecutionLimits.Empty).IsRaise.ShouldBeFalse();
        LimitChange.Classify(null, Sheet).Loosened.Count.ShouldBe(8);
        LimitChange.Classify(Sheet with { MaxOrdersPerDay = null }, Sheet).Loosened.ShouldBe([ExecutionLimits.FieldOrders]);
        LimitChange.Classify(Sheet, Sheet with { ExitAllOnLossHalt = true }).IsRaise.ShouldBeFalse();
    }

    [Fact]
    public void A_narrower_window_inside_the_old_one_is_a_lowering_even_when_both_ends_move()
    {
        LimitChange.Classify(Sheet, Sheet with { WindowStart = new TimeOnly(10, 0), WindowEnd = new TimeOnly(14, 0) }).IsRaise.ShouldBeFalse();
        LimitChange.Classify(Sheet, Sheet with { WindowStart = new TimeOnly(10, 0), WindowEnd = new TimeOnly(14, 50) }).IsRaise.ShouldBeTrue();
    }

    private static DateTimeOffset Ist(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromMinutes(330));

    [Theory]
    [InlineData(26, 8, 59, 27)] // Monday before the open: Monday's session is the full one, so Tuesday
    [InlineData(26, 9, 14, 27)]
    [InlineData(26, 9, 15, 28)] // at the open Monday is no longer whole: Tuesday's session, then Wednesday
    [InlineData(26, 12, 0, 28)]
    [InlineData(26, 20, 0, 28)]
    [InlineData(23, 20, 0, 27)] // Friday evening: Monday's session, then Tuesday
    [InlineData(24, 11, 0, 27)] // Saturday: Monday's session, then Tuesday
    public void A_raise_takes_effect_at_09_00_india_time_of_the_next_trading_day_after_one_full_closed_session(int day, int hour, int minute, int effectiveDay)
    {
        LimitChange.EffectiveFrom(Ist(day, hour, minute), new TradingCalendar()).ShouldBe(Ist(effectiveDay, 9, 0));
    }

    [Theory]
    [InlineData(ExecutionLimits.FieldOrders, 0)]
    [InlineData(ExecutionLimits.FieldOrders, 21)]
    [InlineData(ExecutionLimits.FieldPositions, 11)]
    [InlineData(ExecutionLimits.FieldNotionalOrder, -1)]
    [InlineData(ExecutionLimits.FieldNotionalDay, 59_999.99)]
    [InlineData(ExecutionLimits.FieldLoss, 0.001)]
    public void A_value_outside_its_rule_or_ceiling_names_its_field(string field, double value)
    {
        var sheet = field switch
        {
            ExecutionLimits.FieldOrders => Sheet with { MaxOrdersPerDay = (int)value },
            ExecutionLimits.FieldPositions => Sheet with { MaxOpenPositions = (int)value },
            ExecutionLimits.FieldNotionalOrder => Sheet with { MaxNotionalPerOrder = (decimal)value },
            ExecutionLimits.FieldNotionalDay => Sheet with { MaxNotionalPerDay = (decimal)value },
            _ => Sheet with { MaxLossPerDay = (decimal)value },
        };

        sheet.Invalid().ShouldBe(field);
    }

    [Fact]
    public void The_ceilings_of_the_allow_list_the_window_and_the_products_are_enforced()
    {
        Sheet.Invalid().ShouldBeNull();
        (Sheet with { MaxOrdersPerDay = 20, MaxOpenPositions = 10 }).Invalid().ShouldBeNull();
        (Sheet with { AllowList = [.. Enumerable.Range(0, 51).Select(i => "S" + i)] }).Invalid().ShouldBe(ExecutionLimits.FieldAllowList);
        (Sheet with { AllowList = ["tatapower"] }).Invalid().ShouldBe(ExecutionLimits.FieldAllowList);
        (Sheet with { AllowList = ["INFY", "INFY"] }).Invalid().ShouldBe(ExecutionLimits.FieldAllowList);
        (Sheet with { AllowList = ["M&M", "BAJAJ-AUTO", "TATAPOWER-EQ"] }).Invalid().ShouldBeNull();
        (Sheet with { WindowStart = new TimeOnly(9, 15) }).Invalid().ShouldBe(ExecutionLimits.FieldWindow);
        (Sheet with { WindowEnd = new TimeOnly(15, 1) }).Invalid().ShouldBe(ExecutionLimits.FieldWindow);
        (Sheet with { WindowStart = new TimeOnly(14, 45) }).Invalid().ShouldBe(ExecutionLimits.FieldWindow);
        (Sheet with { WindowEnd = null }).Invalid().ShouldBe(ExecutionLimits.FieldWindow);
        (Sheet with { WindowStart = new TimeOnly(9, 16), WindowEnd = new TimeOnly(15, 0) }).Invalid().ShouldBeNull();
        (Sheet with { Products = ["EQ_CNC"] }).Invalid().ShouldBe(ExecutionLimits.FieldProducts);
    }

    [Fact]
    public void An_unset_value_is_legal_missing_and_allows_nothing()
    {
        ExecutionLimits.Empty.Invalid().ShouldBeNull();
        ExecutionLimits.Empty.IsComplete.ShouldBeFalse();
        ExecutionLimits.Empty.Missing().Count.ShouldBe(8);
        (Sheet with { AllowList = [] }).Missing().ShouldBe([ExecutionLimits.FieldAllowList]);
        Sheet.IsComplete.ShouldBeTrue();
    }

    [Fact]
    public void The_symbol_match_is_exact_on_the_trading_symbol()
    {
        Sheet.Allows("NSE:TATAPOWER").ShouldBeTrue();
        Sheet.Allows("TATAPOWER").ShouldBeTrue();
        Sheet.Allows("NSE:TATAPOWER-EQ").ShouldBeFalse();
        Sheet.Allows("NSE:Tatapower").ShouldBeFalse();
    }

    [Fact]
    public void Equal_sheets_have_an_equal_hash_whatever_the_order_of_their_lists()
    {
        Sheet.Hash().ShouldBe((Sheet with { AllowList = ["SBIN", "TATAPOWER", "INFY"] }).Hash());
        Sheet.Hash().ShouldNotBe((Sheet with { MaxLossPerDay = 1_499m }).Hash());
        Sheet.Hash().Length.ShouldBe(64);
    }
}
