using System.Buffers;
using System.Text;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Text;

namespace XDrishti.SimExport.Tests.Csv;

/// <summary>
/// Criteria 5 and 6 of P4-05 for the files and the console text: the report of the hand table of the Domain tests (A takes six entries, B takes five, five are common) printed to four decimals, row by row, a figure beyond a censor level printed as
/// censored with no sentence, a cell below 30 trades printed as such, and no negative zero.
/// </summary>
public sealed class ComparisonCsvTests
{
    private static readonly ExitShare[] Shares = [new(ExitReason.Stop, 2, 2.0 / 6), new(ExitReason.Target, 2, 2.0 / 6), new(ExitReason.SquareOff, 2, 2.0 / 6)];

    private static SampleFigures A_own() => new(6, 3, 0.5, 2.0 / 6, 0.0, Math.Sqrt(2.25 / 6), 2.0, 1.8, 700.0 / 6, null, new ValueList<ExitShare>(Shares));

    private static SampleFigures A_common() => new(5, 3, 0.6, 0.5, 0.5, Math.Sqrt(0.4), 2.5, 2.25, 100, null, new ValueList<ExitShare>([new ExitShare(ExitReason.Stop, 2, 0.4), new ExitShare(ExitReason.Target, 2, 0.4), new ExitShare(ExitReason.SquareOff, 1, 0.2)]));

    private static SampleFigures B_own() => new(5, 2, 0.4, 0.4, -0.0, Math.Sqrt(0.4), 2.0, 2.0, 154, null, new ValueList<ExitShare>([new ExitShare(ExitReason.Stop, 2, 0.4), new ExitShare(ExitReason.Target, 1, 0.2), new ExitShare(ExitReason.SquareOff, 2, 0.4)]));

    private static readonly KeyValuePair<string, long>[] Counters = [new("legs", 0), new("stop_loosen_ignored", 0), new("adds_taken", 0), new("adds_missed", 0), new("adds_skipped_risk", 0), new("adds_skipped_quantity", 0), new("exits_time_stop", 0), new("exits_ema_exit", 0)];

    private static ComparisonReport Report() => new(
        "RFilled",
        5,
        true,
        [
            new ComparisonRow("FIXED_RR{rr=2}", A_own(), A_common(), null, 1, 1, Counters),
            new ComparisonRow("FIXED_RR{rr=3}", B_own(), B_own(), new PairedFigure(5, -0.1), 0, 0, Counters),
        ],
        [
            new CandidateDiagnostics(
                "FIXED_RR{rr=2}",
                6,
                true,
                [
                    new DiagnosticFigure("winners with adverse excursion above 0.8R", 1.0 / 3, true, 1, 3, "1R stop", false, null),
                    new DiagnosticFigure("winners with adverse excursion above 1.2R", 0, true, 0, 3, "1R stop", true, null),
                    new DiagnosticFigure("median MFE (R)", 1.0, false, 6, 6, "2R target", false, null),
                ]),
        ]);

    private static string Text(Action<IBufferWriter<byte>> write)
    {
        var sink = new ArrayBufferWriter<byte>();
        write(sink);
        return new UTF8Encoding(false, true).GetString(sink.WrittenSpan);
    }

    [Fact]
    public void The_table_is_four_decimals_a_row_for_the_own_trades_and_a_row_for_the_common_set_with_the_paired_difference()
    {
        var lines = Text(sink => ComparisonCsv.Write(sink, Report())).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldBe(string.Join(',', ComparisonCsv.Columns));
        lines[1].ShouldBe("FIXED_RR{rr=2},own,RFilled,6,3,0.5000,0.3333,0.0000,0.6124,2.0000,1.8000,116.6667,,0.3333,0.3333,0.3333,0.0000,0.0000,0.0000,0.0000,,,1,1,0,0,0,0,0,0,0,0");
        lines[2].ShouldBe("FIXED_RR{rr=2},common,RFilled,5,3,0.6000,0.5000,0.5000,0.6325,2.5000,2.2500,100.0000,,0.4000,0.4000,0.2000,0.0000,0.0000,0.0000,0.0000,,,,,,,,,,,,");
        lines[3].ShouldBe("FIXED_RR{rr=3},own,RFilled,5,2,0.4000,0.4000,0.0000,0.6325,2.0000,2.0000,154.0000,,0.4000,0.2000,0.4000,0.0000,0.0000,0.0000,0.0000,,,0,0,0,0,0,0,0,0,0,0");
        lines[4].ShouldContain(",common,RFilled,5,2,0.4000,0.4000,0.0000,");
        lines[4].ShouldContain(",5,-0.1000,", Case.Sensitive, "the paired difference of B from A on the five common entries");
        lines.ShouldAllBe(l => !l.Contains("-0.0000", StringComparison.Ordinal), "a zero has no sign");
    }

    [Fact]
    public void The_diagnostics_print_the_value_the_sample_the_censor_level_and_whether_it_is_censored()
    {
        var lines = Text(sink => ComparisonCsv.WriteDiagnostics(sink, Report())).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        lines[0].ShouldBe(string.Join(',', ComparisonCsv.DiagnosticColumns));
        lines[1].ShouldBe("FIXED_RR{rr=2},winners with adverse excursion above 0.8R,0.3333,share,1,3,1R stop,false,,6,true");
        lines[2].ShouldBe("FIXED_RR{rr=2},winners with adverse excursion above 1.2R,0.0000,share,0,3,1R stop,true,,6,true");
        lines[3].ShouldBe("FIXED_RR{rr=2},median MFE (R),1.0000,r,6,6,2R target,false,,6,true");
    }

    [Fact]
    public void The_console_table_has_both_blocks_and_the_diagnostics_say_censored_and_below_the_minimum()
    {
        var table = ComparisonText.Table(Report());
        var diagnostics = ComparisonText.Diagnostics(Report());

        table.ShouldContain("comparison (unit: RFilled; open-at-end trades are not counted; entries taken under every candidate: 5)");
        table.ShouldContain("each candidate's own trades");
        table.ShouldContain("the common set (the paired comparison)");
        table.ShouldContain("-0.1000");
        table.ShouldContain("FIXED_RR{rr=3}");
        diagnostics.ShouldContain("FIXED_RR{rr=2}: 6 trades (n < 30, no reading)");
        diagnostics.ShouldContain("winners with adverse excursion above 1.2R: 0.0% (0 of 3); censor level: 1R stop; CENSORED, beyond the level");
        diagnostics.ShouldContain("median MFE (R): 1.0000 (n 6); censor level: 2R target");
    }

    [Fact]
    public void The_banner_lines_say_in_sample_and_the_gate_only_for_a_swing_strategy()
    {
        ComparisonText.BannerLines(false).ShouldContain(l => l.StartsWith("candidates compared on the same entries; the best row is in-sample on the data shown and is not evidence of an edge", StringComparison.Ordinal));
        ComparisonText.BannerLines(false).ShouldNotContain(l => l.Contains("gate not applied", StringComparison.Ordinal));
        ComparisonText.BannerLines(true).ShouldContain(l => l.StartsWith("gate not applied in the comparison (swing)", StringComparison.Ordinal));
        ComparisonText.BannerLines(false).ShouldContain(l => l.StartsWith("censor levels:", StringComparison.Ordinal));
    }

    [Fact]
    public void A_nan_cannot_be_printed()
    {
        Should.Throw<ArgumentException>(() => ComparisonFormat.Fixed(double.NaN));
        ComparisonFormat.Fixed(-0.00001).ShouldBe("0.0000");
        ComparisonFormat.Fixed(1.23455).ShouldBe("1.2346");
        ComparisonFormat.Percent(1.0 / 3).ShouldBe("33.3%");
    }
}
