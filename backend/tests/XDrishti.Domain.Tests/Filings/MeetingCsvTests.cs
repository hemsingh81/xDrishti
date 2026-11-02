using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-04 criteria 1, 3, 8: the board-meeting csv (header, each bad row, the time rule) and the loading order.</summary>
public sealed class MeetingCsvTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Header = "symbol,period_end,meeting_date,purpose,status,intimation_ts\n";

    [Fact]
    public void A_good_row_carries_its_own_intimation_time()
    {
        var parsed = MeetingCsv.Parse(Header + "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30", Now);

        parsed.FileReject.ShouldBeNull();
        parsed.Rejects.ShouldBeEmpty();
        var row = parsed.Rows.ShouldHaveSingleItem();
        (row.Symbol, row.PeriodEnd, row.MeetingDate, row.Purpose, row.Status, row.Line).ShouldBe(("RELIANCE", new DateOnly(2026, 6, 30), new DateOnly(2026, 8, 12), "results", "scheduled", 2));
        row.Time.ShouldBe(new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero));
        row.Intimation.State.ShouldBe(BroadcastState.Ok);
    }

    [Theory]
    [InlineData("symbol,period_end,meeting_date,purpose,status")]
    [InlineData("period_end,symbol,meeting_date,purpose,status,intimation_ts")]
    [InlineData("Symbol,period_end,meeting_date,purpose,status,intimation_ts")]
    [InlineData("symbol,period_end,meeting_date,purpose,status,intimation_ts,extra")]
    [InlineData("")]
    public void Any_other_header_rejects_the_whole_file(string header)
    {
        var parsed = MeetingCsv.Parse(header + "\nRELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30", Now);

        parsed.FileReject.ShouldBe("bad_header");
        parsed.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("reliance lt,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30", "symbol")]
    [InlineData("RELIANCE,2026-6-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30", "period_end")]
    [InlineData("RELIANCE,2026-06-30,12/08/2026,results,scheduled,2026-08-03T11:00:00+05:30", "meeting_date")]
    [InlineData("RELIANCE,2026-06-30,1999-12-31,results,scheduled,2026-08-03T11:00:00+05:30", "meeting_date")]
    [InlineData("RELIANCE,2026-06-30,2026-08-12,result,scheduled,2026-08-03T11:00:00+05:30", "purpose")]
    [InlineData("RELIANCE,2026-06-30,2026-08-12,results,Scheduled,2026-08-03T11:00:00+05:30", "status")]
    public void A_bad_cell_rejects_that_row_with_its_line_and_column_and_the_good_rows_still_load(string bad, string column)
    {
        var parsed = MeetingCsv.Parse(Header + bad + "\nTCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", Now);

        parsed.Rejects.ShouldHaveSingleItem().ShouldBe(new RowReject(2, "bad_row", column));
        parsed.Rows.ShouldHaveSingleItem().Symbol.ShouldBe("TCS");
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_cells_is_a_bad_row_without_a_column()
    {
        var parsed = MeetingCsv.Parse(Header + "RELIANCE,2026-06-30,2026-08-12,results\nTCS,\"2026,x", Now);

        parsed.Rejects.Select(r => (r.Line, r.Reason, r.Column)).ShouldBe([(2, "bad_row", null), (3, "bad_row", null)]);
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("2026-08-03", "date_only")]
    [InlineData("2026-08-03T11:00:00", "no_offset")]
    [InlineData("soon", "unparsable")]
    public void A_blank_or_invalid_intimation_time_is_unavailable_and_never_guessed(string cell, string reason)
    {
        var row = MeetingCsv.Parse(Header + $"RELIANCE,2026-06-30,2026-08-12,results,scheduled,{cell}", Now).Rows.ShouldHaveSingleItem();

        (row.Intimation.State, row.Intimation.Reason, row.Time).ShouldBe((BroadcastState.Unavailable, reason, null));
    }

    [Fact]
    public void A_time_in_the_future_rejects_the_row_so_nothing_that_depends_on_the_clock_is_stored()
    {
        var parsed = MeetingCsv.Parse(Header + "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2027-01-01T00:00:00Z", Now);

        parsed.Rows.ShouldBeEmpty();
        parsed.Rejects.ShouldHaveSingleItem().ShouldBe(new RowReject(2, "bad_row", "intimation_ts"));
    }

    [Fact]
    public void A_time_with_seven_fraction_digits_is_cut_to_whole_microseconds()
    {
        var row = MeetingCsv.Parse(Header + "RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00.1234567+05:30", Now).Rows.ShouldHaveSingleItem();

        row.Time!.Value.Ticks.ShouldBe(row.Time.Value.Ticks - (row.Time.Value.Ticks % 10));
        row.Time.Value.UtcTicks.ShouldBe(new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero).UtcTicks + 1234560);
    }

    [Fact]
    public void More_than_20000_rows_reject_the_file()
    {
        var text = Header + string.Join("\n", Enumerable.Repeat("TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", 20_001));

        MeetingCsv.Parse(text, Now).FileReject.ShouldBe("too_many_rows");
        MeetingCsv.Parse(Header + string.Join("\n", Enumerable.Repeat("TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30", 20_000)), Now).FileReject.ShouldBeNull();
    }

    [Fact]
    public void Rows_load_in_chain_order_whatever_the_line_order_and_a_same_time_conflict_refuses_every_row_of_it()
    {
        var lines = new[]
        {
            "TCS,2026-06-30,2026-07-20,results,rescheduled,2026-07-10T09:00:00+05:30",
            "TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30",
            "TCS,2026-06-30,2026-07-10,other,scheduled,2026-07-10T09:00:00+05:30",
            "TCS,2026-06-30,2026-07-11,results,scheduled,2026-07-15T09:00:00+05:30",
            "TCS,2026-06-30,2026-07-12,results,scheduled,2026-07-15T09:00:00+05:30",
        };
        var forward = MeetingCsv.Prepare(MeetingCsv.Parse(Header + string.Join("\n", lines), Now).Rows);
        var backward = MeetingCsv.Prepare(MeetingCsv.Parse(Header + string.Join("\n", lines.Reverse()), Now).Rows);

        forward.Rows.Select(r => (r.Purpose, r.MeetingDate)).ShouldBe([("other", new DateOnly(2026, 7, 10)), ("results", new DateOnly(2026, 7, 10)), ("results", new DateOnly(2026, 7, 20))]);
        backward.Rows.Select(r => (r.Purpose, r.MeetingDate)).ShouldBe(forward.Rows.Select(r => (r.Purpose, r.MeetingDate)));
        forward.Rejects.Select(r => r.Reason).ShouldBe(["ambiguous_order", "ambiguous_order"]);
        backward.Rejects.Count.ShouldBe(2);
    }

    [Fact]
    public void The_same_row_twice_is_not_a_conflict()
    {
        var line = "TCS,2026-06-30,2026-07-10,results,scheduled,2026-07-01T09:00:00+05:30";

        var prepared = MeetingCsv.Prepare(MeetingCsv.Parse(Header + line + "\n" + line, Now).Rows);

        prepared.Rows.Count.ShouldBe(2);
        prepared.Rejects.ShouldBeEmpty();
    }
}
