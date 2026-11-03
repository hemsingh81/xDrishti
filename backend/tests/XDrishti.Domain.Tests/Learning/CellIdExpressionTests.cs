using System.Diagnostics;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>P14-06 criteria 1 and 2 and the lengths (ADR 0193): the cell key has the fewest trailing parts, every six-part key reads back as cash and writes back byte for byte, and no cell has two texts.</summary>
public sealed class CellIdExpressionTests
{
    private const string Six = "ORB|15m|BUY|intraday|FIXED_RR|*";

    /// <summary>Every key shape stored before P14-06 (a fixture of cell_stats, hypotheses and changelog subjects): a golden list that must never change.</summary>
    public static TheoryData<string> StoredKeys =>
    [
        Six,
        "ORB|15m|SELL|swing|FIXED_RR|*",
        "VWAP_PULLBACK|5m|BUY|intraday|ATR_TRAIL_2.0|*",
        "ORB|15m|BUY|intraday|FIXED_RR|trend_up_calm",
        "ORB|15m|BUY|intraday|FIXED_RR|trend_up_calm_index_only",
        "S1|1d|BUY|swing|RR_2.5|*",
    ];

    [Theory]
    [MemberData(nameof(StoredKeys))]
    public void A_stored_six_part_key_reads_as_cash_and_writes_back_byte_identical(string key)
    {
        var id = CellId.TryParse(key).ShouldNotBeNull();

        id.Expression.ShouldBe(Expression.Cash);
        id.Universe.ShouldBe("*");
        id.IsCash.ShouldBeTrue();
        id.Key.ShouldBe(key);
        (id with { }).Key.ShouldBe(key);
    }

    [Fact]
    public void A_cell_made_the_old_way_has_the_old_key()
    {
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR").Key.ShouldBe(Six);
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", "trend_up_calm").Key.ShouldBe("ORB|15m|BUY|intraday|FIXED_RR|trend_up_calm");
    }

    [Fact]
    public void An_eight_part_key_parses_and_writes_back_the_same_text()
    {
        const string key = Six + "|*|option_buy";

        var id = CellId.TryParse(key).ShouldNotBeNull();

        (id.Universe, id.Expression, id.IsCash).ShouldBe(("*", Expression.OptionBuy, false));
        id.Key.ShouldBe(key);
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", Expression: Expression.OptionBuy).Key.ShouldBe(key);
    }

    [Theory]
    [InlineData("future")]
    [InlineData("option_buy")]
    [InlineData("debit_spread")]
    [InlineData("credit_spread")]
    [InlineData("iron_condor")]
    [InlineData("pair")]
    [InlineData("basis")]
    public void Every_non_cash_expression_round_trips(string name)
    {
        var key = Six + "|*|" + name;

        CellId.TryParse(key)!.Key.ShouldBe(key);
        ExpressionNames.Parse(name).ShouldNotBeNull().Code().ShouldBe(name);
    }

    [Theory]
    [InlineData(Six + "|*|*|cash")]                 // not canonical: cash is never written as an eighth part
    [InlineData(Six + "|*|cash")]                   // not canonical: cash with the default universe is the six-part text
    [InlineData(Six + "|*")]                        // not canonical: a seventh part of * is the six-part text
    [InlineData(Six + "|liq_high")]                 // a universe that BK-11 has not added
    [InlineData(Six + "|liq_high|option_buy")]
    [InlineData(Six + "|*|option_sell")]            // not in the closed set
    [InlineData(Six + "|*|Option_Buy")]             // names are exact
    [InlineData(Six + "|*|option_buy|extra")]       // nine parts
    [InlineData(Six + "|*|")]                       // empty expression
    [InlineData("ORB|15m|BUY|intraday|FIXED_RR")]   // five parts
    [InlineData("")]
    [InlineData(null)]
    public void A_key_that_is_not_canonical_is_refused(string? key)
    {
        CellId.TryParse(key).ShouldBeNull();
    }

    [Fact]
    public void The_reserved_universe_accepts_only_the_default_value()
    {
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", Universe: "liq_high").Problems().ShouldHaveSingleItem().ShouldContain("universe");
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", Universe: "*").Problems().ShouldBeEmpty();
        new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR", Expression: (Expression)99).Problems().ShouldHaveSingleItem().ShouldContain("expression");
    }

    [Fact]
    public void An_eight_part_key_of_64_character_parts_round_trips_and_a_65_character_part_is_refused()
    {
        var part = new string('a', 64);
        var id = new CellId(part, part, part, part, part, part, "*", Expression.IronCondor);

        id.Key.Length.ShouldBe(6 * 64 + 5 + 2 + 12);
        CellId.TryParse(id.Key).ShouldBe(id);
        CellId.TryParse(id.Key.Replace(part + "|" + part, part + "b|" + part, StringComparison.Ordinal)).ShouldBeNull();
    }

    [Fact]
    public void Two_cells_of_one_setup_differ_only_by_their_expression()
    {
        var cash = CellId.TryParse(Six)!;
        var option = CellId.TryParse(Six + "|*|option_buy")!;

        cash.ShouldNotBe(option);
        cash.Key.ShouldNotBe(option.Key);
        (option with { Expression = Expression.Cash }).ShouldBe(cash);
    }

    [Fact]
    public void Parsing_and_writing_cost_about_what_they_did_before()
    {
        // Not gated (the spec asks for a benchmark, not an assertion on time): a smoke run that proves the hot path stays linear and allocation-light enough to finish quickly.
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 100_000; i++)
        {
            CellId.TryParse(Six)!.Key.ShouldNotBeNull();
        }

        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }
}
