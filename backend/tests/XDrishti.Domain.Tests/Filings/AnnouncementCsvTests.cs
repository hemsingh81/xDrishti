using System.Text;
using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-07 criterion 1: the announcements csv (header, each reject with line and column, the time rule, hostile cells).</summary>
public sealed class AnnouncementCsvTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private const string Header = "symbol,broadcast_ts,category,subject,text\n";

    private const string Good = "RELIANCE,2026-08-03T11:00:00+05:30,Results,Quarterly results,The board approved the results.";

    [Fact]
    public void A_good_row_carries_its_own_broadcast_time_and_its_text()
    {
        var parsed = AnnouncementCsv.Parse(Header + Good, Now);

        parsed.FileReject.ShouldBeNull();
        parsed.Rejects.ShouldBeEmpty();
        var row = parsed.Rows.ShouldHaveSingleItem();
        (row.Symbol, row.Category, row.Subject, row.Text, row.Line).ShouldBe(("RELIANCE", "Results", "Quarterly results", "The board approved the results.", 2));
        row.Time.ShouldBe(new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("symbol,broadcast_ts,category,subject")]
    [InlineData("broadcast_ts,symbol,category,subject,text")]
    [InlineData("Symbol,broadcast_ts,category,subject,text")]
    [InlineData("symbol,broadcast_ts,category,subject,text,extra")]
    [InlineData("")]
    public void Any_other_header_rejects_the_whole_file(string header)
    {
        var parsed = AnnouncementCsv.Parse(header + "\n" + Good, Now);

        parsed.FileReject.ShouldBe("bad_header");
        parsed.Rows.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("reliance lt,2026-08-03T11:00:00+05:30,Results,S,T", "symbol")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,,S,T", "category")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Results,,T", "subject")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Results,S,", "text")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Results,S,   ", "text")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Res\u0000ults,S,T", "category")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Results,S\u202EXYZ,T", "subject")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Results,S,T\u0007T", "text")]
    [InlineData("RELIANCE,2026-08-03T11:00:00+05:30,Results,S,T\u2028T", "text")]
    public void A_bad_cell_rejects_that_row_with_its_column_and_never_the_text(string row, string column)
    {
        var parsed = AnnouncementCsv.Parse(Header + row, Now);

        parsed.Rows.ShouldBeEmpty();
        var reject = parsed.Rejects.ShouldHaveSingleItem();
        (reject.Line, reject.Reason, reject.Column).ShouldBe((2, "bad_row", column));
    }

    [Fact]
    public void A_row_with_the_wrong_number_of_cells_or_a_broken_quote_is_rejected_on_its_own()
    {
        var parsed = AnnouncementCsv.Parse(Header + "RELIANCE,2026-08-03T11:00:00+05:30,Results,S\n" + Good + "\nTCS,2026-08-03T11:00:00+05:30,Results,\"S,T\n", Now);

        parsed.Rows.Count.ShouldBe(1);
        parsed.Rejects.Select(r => (r.Line, r.Reason)).ShouldBe([(2, "bad_row"), (4, "bad_row")]);
    }

    [Theory]
    [InlineData(60, 300, 4000, true)]
    [InlineData(61, 10, 10, false)]
    [InlineData(10, 301, 10, false)]
    [InlineData(10, 10, 4001, false)]
    public void A_cell_over_its_limit_is_too_long_and_is_never_truncated(int category, int subject, int text, bool accepted)
    {
        var row = $"RELIANCE,2026-08-03T11:00:00+05:30,{new string('c', category)},{new string('s', subject)},{new string('t', text)}";

        var parsed = AnnouncementCsv.Parse(Header + row, Now);

        if (accepted)
        {
            parsed.Rows.ShouldHaveSingleItem().Text.Length.ShouldBe(4000);
        }
        else
        {
            parsed.Rows.ShouldBeEmpty();
            parsed.Rejects.ShouldHaveSingleItem().Reason.ShouldBe("too_long");
        }
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("2026-08-03", "date_only")]
    [InlineData("2026-08-03T11:00:00", "no_offset")]
    [InlineData("yesterday", "unparsable")]
    [InlineData("2099-01-01T00:00:00+05:30", "broadcast_in_future")]
    [InlineData("1999-01-01T00:00:00+05:30", "before_2000")]
    public void A_missing_or_bad_time_is_not_a_reject_the_row_is_unavailable_and_never_dated(string ts, string reason)
    {
        var parsed = AnnouncementCsv.Parse(Header + $"RELIANCE,{ts},Results,S,T", Now);

        parsed.Rejects.ShouldBeEmpty();
        var row = parsed.Rows.ShouldHaveSingleItem();
        (row.Time, row.Broadcast.State, row.Broadcast.Reason).ShouldBe((null, BroadcastState.Unavailable, reason));
    }

    [Fact]
    public void Quoted_cells_may_hold_commas_quotes_and_line_breaks_and_a_formula_is_only_text()
    {
        var text = "\"Line one, with a comma\r\nLine \"\"two\"\"\"";
        var parsed = AnnouncementCsv.Parse(Header + $"TCS,2026-08-03T11:00:00Z,Results,\"=HYPERLINK(\"\"http://x\"\")\",{text}", Now);

        var row = parsed.Rows.ShouldHaveSingleItem();
        row.Subject.ShouldBe("=HYPERLINK(\"http://x\")");
        row.Text.ShouldBe("Line one, with a comma\nLine \"two\"");
    }

    [Fact]
    public void Text_is_stored_as_given_with_lf_line_ends_so_the_same_cell_is_the_same_bytes()
    {
        var a = AnnouncementCsv.Parse(Header + "TCS,2026-08-03T11:00:00Z,Results,S,\"one\r\ntwo\r\n\"", Now).Rows.Single();
        var b = AnnouncementCsv.Parse(Header + "TCS,2026-08-03T11:00:00Z,Results,S,\"one\ntwo\"", Now).Rows.Single();

        a.Text.ShouldBe("one\ntwo");
        RowCanonical.Announcement(Guid.Empty, a).ShouldBe(RowCanonical.Announcement(Guid.Empty, b));
    }

    [Fact]
    public void More_than_twenty_thousand_rows_refuse_the_file()
    {
        var text = new StringBuilder(Header);
        for (var i = 0; i < 20_001; i++)
        {
            text.Append("TCS,2026-08-03T11:00:00Z,Results,S,T\n");
        }

        AnnouncementCsv.Parse(text.ToString(), Now).FileReject.ShouldBe("too_many_rows");
    }

    [Fact]
    public void An_unclosed_quote_swallows_only_the_rest_of_the_text_and_never_throws()
    {
        var parsed = AnnouncementCsv.Parse(Header + Good + "\nTCS,2026-08-03T11:00:00Z,Results,\"never closed,T\nTCS,2026-08-03T11:00:00Z,Results,S,T", Now);

        parsed.Rows.ShouldHaveSingleItem().Symbol.ShouldBe("RELIANCE");
        parsed.Rejects.ShouldHaveSingleItem().Line.ShouldBe(3);
    }

    [Fact]
    public void A_very_large_cell_is_refused_without_a_problem_and_a_byte_order_mark_is_accepted()
    {
        var huge = new string('x', 5_000_000);

        var parsed = AnnouncementCsv.Parse("\uFEFF" + Header + $"TCS,2026-08-03T11:00:00Z,Results,S,{huge}\n" + Good, Now);

        parsed.Rows.ShouldHaveSingleItem().Symbol.ShouldBe("RELIANCE");
        parsed.Rejects.ShouldHaveSingleItem().Reason.ShouldBe("too_long");
    }

    [Fact]
    public void The_canonical_text_is_the_same_for_the_same_row_in_two_files_whatever_the_line_and_differs_by_time_or_instrument()
    {
        var a = AnnouncementCsv.Parse(Header + Good, Now).Rows.Single();
        var b = AnnouncementCsv.Parse(Header + "TCS,2026-01-01T00:00:00Z,X,Y,Z\n\n" + Good, Now).Rows[^1];
        var instrument = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

        RowCanonical.Announcement(instrument, a).ShouldBe(RowCanonical.Announcement(instrument, b));
        RowCanonical.Sha256(RowCanonical.Announcement(instrument, a)).ShouldNotBe(RowCanonical.Sha256(RowCanonical.Announcement(Guid.Empty, a)));
        RowCanonical.Announcement(instrument, a with { Broadcast = new BroadcastTime(a.Time!.Value.AddSeconds(1), BroadcastState.Ok, null) }).ShouldNotBe(RowCanonical.Announcement(instrument, a));
        RowCanonical.Announcement(instrument, a).ShouldContain("2026-08-03T05:30:00.000000Z");
    }

    [Fact]
    public void The_stored_text_reads_back_and_anything_else_reads_as_nothing()
    {
        var row = AnnouncementCsv.Parse(Header + "TCS,2026-08-03T11:00:00Z,Results,Subject,\"Body, \"\"quoted\"\"\"", Now).Rows.Single();

        var body = AnnouncementBody.Read(Encoding.UTF8.GetBytes(RowCanonical.Announcement(Guid.NewGuid(), row)));

        body.ShouldBe(new AnnouncementBody("Results", "Subject", "Body, \"quoted\""));
        AnnouncementBody.Read("not json"u8).ShouldBeNull();
        AnnouncementBody.Read("{\"kind\":\"results\"}"u8).ShouldBeNull();
        AnnouncementBody.Read("[1]"u8).ShouldBeNull();
    }
}
