using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-04 criterion 1 and 10: the canonical text and hash of a row do not depend on the file, the line, the spelling of the time or the order of lines.</summary>
public sealed class RowCanonicalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Reliance = new("aaaaaaaa-0000-0000-0000-000000000001");

    private static MeetingRow Meeting(string text, int line = 2) =>
        MeetingCsv.Parse("symbol,period_end,meeting_date,purpose,status,intimation_ts\n" + new string('\n', line - 2) + text, Now).Rows.ShouldHaveSingleItem();

    [Fact]
    public void The_same_row_on_another_line_or_with_the_same_instant_in_another_offset_has_the_same_hash()
    {
        var a = Meeting("RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30");
        var b = Meeting("RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T05:30:00Z", 9);

        RowCanonical.Sha256(RowCanonical.Meeting(Reliance, a)).ShouldBe(RowCanonical.Sha256(RowCanonical.Meeting(Reliance, b)));
    }

    [Fact]
    public void Any_other_content_or_instrument_changes_the_hash()
    {
        var a = Meeting("RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:00+05:30");
        var hashes = new[]
        {
            RowCanonical.Meeting(Reliance, a),
            RowCanonical.Meeting(Guid.NewGuid(), a),
            RowCanonical.Meeting(Reliance, Meeting("RELIANCE,2026-06-30,2026-08-13,results,scheduled,2026-08-03T11:00:00+05:30")),
            RowCanonical.Meeting(Reliance, Meeting("RELIANCE,2026-06-30,2026-08-12,results,cancelled,2026-08-03T11:00:00+05:30")),
            RowCanonical.Meeting(Reliance, Meeting("RELIANCE,2026-06-30,2026-08-12,results,scheduled,2026-08-03T11:00:01+05:30")),
            RowCanonical.Meeting(Reliance, Meeting("RELIANCE,2026-06-30,2026-08-12,results,scheduled,")),
        }.Select(RowCanonical.Sha256).ToList();

        hashes.Distinct().Count().ShouldBe(hashes.Count);
        hashes[0].Length.ShouldBe(64);
    }

    [Fact]
    public void A_shareholding_group_has_the_same_hash_in_any_line_order_and_a_blank_is_not_a_zero()
    {
        string Csv(params string[] rows) => "symbol,period_end,category,pct,pledged_pct,broadcast_ts\n" + string.Join("\n", rows);
        ShareholdingGroup Group(params string[] rows) => ShareholdingGroups.Build(ShareholdingCsv.Parse(Csv(rows), Now).Rows).Groups.ShouldHaveSingleItem();
        var ts = "2026-07-20T18:00:00+05:30";

        var one = RowCanonical.Shareholding(Reliance, Group($"RELIANCE,2026-06-30,promoter,50,10,{ts}", $"RELIANCE,2026-06-30,fii,6.50,,{ts}"));
        var two = RowCanonical.Shareholding(Reliance, Group($"RELIANCE,2026-06-30,fii,6.5,,{ts}", $"RELIANCE,2026-06-30,promoter,50.0000,10,{ts}"));
        var blank = RowCanonical.Shareholding(Reliance, Group($"RELIANCE,2026-06-30,promoter,0,,{ts}"));
        var zero = RowCanonical.Shareholding(Reliance, Group($"RELIANCE,2026-06-30,promoter,,,{ts}"));

        one.ShouldBe(two);
        blank.ShouldNotBe(zero);
    }
}
