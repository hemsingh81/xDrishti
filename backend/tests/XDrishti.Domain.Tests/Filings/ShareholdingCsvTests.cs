using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-04 criterion 7 and 8: the shareholding csv, its groups, the time consistency and the sum check.</summary>
public sealed class ShareholdingCsvTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Header = "symbol,period_end,category,pct,pledged_pct,broadcast_ts\n";

    private const string Ts = "2026-07-20T18:00:00+05:30";

    private static string Row(string category, string pct, string pledged = "", string ts = Ts, string symbol = "RELIANCE") => $"{symbol},2026-06-30,{category},{pct},{pledged},{ts}";

    private static string Csv(params string[] rows) => Header + string.Join("\n", rows);

    [Fact]
    public void A_group_of_four_categories_becomes_one_group_with_one_time_and_the_lines_in_a_fixed_order()
    {
        var parsed = ShareholdingCsv.Parse(Csv(Row("public", "40"), Row("promoter", "50.1234", "12.5"), Row("fii", "6"), Row("dii", "3.8766")), Now);
        var (groups, rejects) = ShareholdingGroups.Build(parsed.Rows);

        rejects.ShouldBeEmpty();
        var group = groups.ShouldHaveSingleItem();
        group.Lines.Select(l => l.Category).ShouldBe(["promoter", "fii", "dii", "public"]);
        group.Lines[0].ShouldBe(new ShareholdingLine("promoter", 50.1234m, 12.5m));
        group.Time.ShouldBe(new DateTimeOffset(2026, 7, 20, 12, 30, 0, TimeSpan.Zero));
        group.SumOff.ShouldBeFalse();
    }

    [Fact]
    public void A_blank_pct_is_unavailable_and_never_zero_and_a_blank_pledged_share_stays_unavailable()
    {
        var parsed = ShareholdingCsv.Parse(Csv(Row("promoter", ""), Row("fii", "6")), Now);

        var lines = ShareholdingGroups.Build(parsed.Rows).Groups.ShouldHaveSingleItem().Lines;

        lines[0].ShouldBe(new ShareholdingLine("promoter", null, null));
        lines[0].IsValue.ShouldBeFalse();
        lines[1].IsValue.ShouldBeTrue();
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("100.0001")]
    [InlineData("250")]
    public void A_pct_outside_0_to_100_rejects_that_row_out_of_range_and_the_rest_of_the_file_loads(string pct)
    {
        var parsed = ShareholdingCsv.Parse(Csv(Row("promoter", pct), Row("fii", "6")), Now);

        parsed.Rejects.ShouldHaveSingleItem().ShouldBe(new RowReject(2, "out_of_range", "pct"));
        parsed.Rows.ShouldHaveSingleItem().Category.ShouldBe("fii");
    }

    [Fact]
    public void A_pledged_share_out_of_range_is_out_of_range_too()
    {
        ShareholdingCsv.Parse(Csv(Row("promoter", "50", "101")), Now).Rejects.ShouldHaveSingleItem().ShouldBe(new RowReject(2, "out_of_range", "pledged_pct"));
    }

    [Theory]
    [InlineData("promoter", "abc", "", "pct")]
    [InlineData("promoter", "1.23456", "", "pct")]
    [InlineData("promoter", "50", "x", "pledged_pct")]
    [InlineData("fii", "6", "1", "pledged_pct")]
    [InlineData("retail", "6", "", "category")]
    public void Other_bad_cells_reject_the_row_as_bad_row_with_the_column(string category, string pct, string pledged, string column)
    {
        ShareholdingCsv.Parse(Csv(Row(category, pct, pledged)), Now).Rejects.ShouldHaveSingleItem().ShouldBe(new RowReject(2, "bad_row", column));
    }

    [Fact]
    public void A_bad_header_rejects_the_file()
    {
        ShareholdingCsv.Parse("symbol,period_end,category,pct,broadcast_ts\nA", Now).FileReject.ShouldBe("bad_header");
    }

    [Fact]
    public void Rows_of_one_group_with_different_times_reject_the_whole_group_inconsistent_ts_and_another_group_still_loads()
    {
        var parsed = ShareholdingCsv.Parse(Csv(Row("promoter", "50"), Row("fii", "6", ts: "2026-07-21T18:00:00+05:30"), Row("promoter", "30", symbol: "TCS")), Now);

        var (groups, rejects) = ShareholdingGroups.Build(parsed.Rows);

        rejects.Select(r => (r.Line, r.Reason)).ShouldBe([(2, "inconsistent_ts"), (3, "inconsistent_ts")]);
        groups.ShouldHaveSingleItem().Symbol.ShouldBe("TCS");
    }

    [Fact]
    public void The_same_instant_written_with_another_offset_is_consistent_and_a_blank_time_among_dated_rows_is_not()
    {
        var same = ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("promoter", "50"), Row("fii", "6", ts: "2026-07-20T12:30:00Z")), Now).Rows);
        var mixed = ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("promoter", "50"), Row("fii", "6", ts: "")), Now).Rows);

        same.Groups.ShouldHaveSingleItem();
        mixed.Rejects.Count.ShouldBe(2);
    }

    [Fact]
    public void A_group_without_a_usable_time_is_a_group_with_an_unavailable_time()
    {
        var group = ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("promoter", "50", ts: "2026-07-20"), Row("fii", "6", ts: "2026-07-20")), Now).Rows).Groups.ShouldHaveSingleItem();

        (group.Broadcast.State, group.Broadcast.Reason, group.Time).ShouldBe((BroadcastState.Unavailable, "date_only", null));
    }

    [Fact]
    public void A_category_twice_in_a_group_is_refused_on_every_line_so_the_order_of_lines_never_matters()
    {
        var forward = ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("promoter", "50"), Row("promoter", "51"), Row("fii", "6")), Now).Rows);
        var backward = ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("fii", "6"), Row("promoter", "51"), Row("promoter", "50")), Now).Rows);

        forward.Rejects.Select(r => r.Reason).ShouldBe(["bad_row", "bad_row"]);
        forward.Groups.ShouldHaveSingleItem().Lines.Select(l => l.Category).ShouldBe(["fii"]);
        backward.Groups.ShouldHaveSingleItem().Lines.ShouldBe(forward.Groups[0].Lines);
    }

    [Theory]
    [InlineData("50", "30", "10", "10", false)]
    [InlineData("50", "30", "10", "10.99", false)]
    [InlineData("50", "30", "10", "11.01", true)]
    [InlineData("50", "30", "10", "8.99", true)]
    public void A_sum_more_than_one_point_from_100_is_flagged_and_never_corrected(string promoter, string fii, string dii, string pub, bool off)
    {
        var group = ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("promoter", promoter), Row("fii", fii), Row("dii", dii), Row("public", pub)), Now).Rows).Groups.ShouldHaveSingleItem();

        group.SumOff.ShouldBe(off);
        group.Lines.Select(l => l.Pct).ShouldBe([decimal.Parse(promoter, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(fii, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(dii, System.Globalization.CultureInfo.InvariantCulture), decimal.Parse(pub, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    [Fact]
    public void A_group_missing_a_category_is_not_checked_for_the_sum()
    {
        ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(Row("promoter", "10")), Now).Rows).Groups.ShouldHaveSingleItem().SumOff.ShouldBeFalse();
    }

    [Fact]
    public void A_broadcast_time_in_the_future_rejects_the_row()
    {
        ShareholdingCsv.Parse(Csv(Row("fii", "6", ts: "2027-01-01T00:00:00Z")), Now).Rejects.ShouldHaveSingleItem().ShouldBe(new RowReject(2, "bad_row", "broadcast_ts"));
    }
}
