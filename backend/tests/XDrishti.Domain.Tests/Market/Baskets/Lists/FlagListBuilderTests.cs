using XDrishti.Domain.Market.Baskets.Lists;

namespace XDrishti.Domain.Tests.Market.Baskets.Lists;

public sealed class FlagListBuilderTests
{
    private static FlagRow Row(Guid? id = null, string? series = "EQ", bool fno = false, string? asm = "N", string? category = null, decimal? mtf = null, bool retired = false) =>
        new(id ?? Guid.NewGuid(), series, fno, asm, category, mtf, retired);

    private static IReadOnlyList<ListMember> List(IReadOnlyDictionary<string, IReadOnlyList<ListMember>> sets, ListBasketDefinition definition) => sets[definition.Key];

    [Fact]
    public void Surveillance_counts_every_flag_but_N_and_keeps_the_category_as_the_note()
    {
        var plain = Row(asm: "N");
        var yes = Row(asm: "Y", category: "ASM Stage 2");
        var other = Row(asm: "S", category: null);

        var asm = List(FlagListBuilder.Build([plain, yes, other]), ListBaskets.AsmGsm);

        asm.Select(m => m.InstrumentId).ShouldBe(new[] { yes.Id, other.Id }.Order());
        asm.Single(m => m.InstrumentId == yes.Id).Note.ShouldBe("ASM Stage 2");
        asm.Single(m => m.InstrumentId == other.Id).Note.ShouldBe("S");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("n", false)]
    [InlineData(" N ", false)]
    [InlineData("Y", true)]
    [InlineData("G", true)]
    public void A_surveillance_flag_is_anything_but_blank_or_N(string? flag, bool flagged)
    {
        FlagListBuilder.IsFlagged(flag).ShouldBe(flagged);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0.0, false)]
    [InlineData(2.5, true)]
    public void MTF_eligible_means_a_leverage_above_zero_and_null_or_zero_is_not_eligible(double? leverage, bool eligible)
    {
        var row = Row(mtf: leverage is null ? null : (decimal)leverage.Value);

        List(FlagListBuilder.Build([row]), ListBaskets.Mtf).Select(m => m.InstrumentId).Contains(row.Id).ShouldBe(eligible);
    }

    [Theory]
    [InlineData("BE", true)]
    [InlineData("bz", true)]
    [InlineData("SM", true)]
    [InlineData("ST", true)]
    [InlineData("EQ", false)]
    [InlineData(null, false)]
    public void Trade_for_trade_and_SME_are_the_four_series(string? series, bool listed)
    {
        var row = Row(series: series);

        List(FlagListBuilder.Build([row]), ListBaskets.TradeForTrade).Select(m => m.InstrumentId).Contains(row.Id).ShouldBe(listed);
    }

    [Fact]
    public void F_and_O_stocks_are_the_shares_with_the_flag()
    {
        var yes = Row(fno: true);
        var no = Row(fno: false);

        List(FlagListBuilder.Build([yes, no]), ListBaskets.FnoStocks).Select(m => m.InstrumentId).ShouldBe([yes.Id]);
    }

    [Fact]
    public void A_retired_instrument_is_in_no_list()
    {
        var gone = Row(fno: true, asm: "Y", mtf: 3, series: "BE", retired: true);

        FlagListBuilder.Build([gone]).Values.ShouldAllBe(list => list.Count == 0);
    }

    [Fact]
    public void Every_master_list_is_present_even_when_empty_and_no_intraday_leverage_list_exists()
    {
        var sets = FlagListBuilder.Build([]);

        sets.Keys.Order().ShouldBe(["asm-gsm", "fno-stocks", "mtf-eligible", "series-t2t"]);
        ListBaskets.Baskets.Select(b => b.Name).ShouldNotContain(name => name.Contains("MIS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Building_the_lists_of_the_whole_master_is_fast_set_arithmetic()
    {
        var rows = Enumerable.Range(0, 5000).Select(i => Row(fno: i % 25 == 0, asm: i % 40 == 0 ? "Y" : "N", mtf: i % 3 == 0 ? 2 : null, series: i % 50 == 0 ? "BE" : "EQ")).ToList();

        var sets = FlagListBuilder.Build(rows);

        sets[ListBaskets.FnoStocks.Key].Count.ShouldBe(200);
    }
}
