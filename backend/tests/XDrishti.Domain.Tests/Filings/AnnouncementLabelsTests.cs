using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>P15-07 criterion 5: the import checks of the labelling file, each refusal with the row named.</summary>
public sealed class AnnouncementLabelsTests
{
    private static string Hash(int n) => AnnouncementSampleCsv.RowHash($"a{n:000}", Guid(n).ToString("D"), "TCS", "2026-08-03T05:30:00.000000Z", "Results", "Subject " + n, "Text " + n);

    private static Guid Guid(int n) => new(n, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);

    private static AnnouncementSampleFile Sample() => new("1", 1337, "2026-01-01", "2026-06-30", new string('a', 64), 5000, 2500, 2500, 0, 200, 40, [],
        [.. Enumerable.Range(1, 240).Select(n => new SampleFileRow($"a{n:000}", Guid(n), n % 2 == 0 ? "material" : "other", n > 200, 10, 1, 10, Hash(n)))]);

    private static LabelLine Line(int n, string label, string? subject = null) =>
        new(n + 1, $"a{n:000}", Guid(n).ToString("D"), "TCS", "2026-08-03T05:30:00.000000Z", "Results", subject ?? "Subject " + n, "Text " + n, label);

    private static List<LabelLine> All(Func<int, string> label) => [.. Enumerable.Range(1, 240).Select(n => Line(n, n > 200 ? "non_material" : label(n)))];

    [Fact]
    public void A_complete_file_picks_the_200_main_rows_in_order()
    {
        var picks = AnnouncementLabels.Resolve(Sample(), All(n => n % 3 == 0 ? "material" : "non_material")).Value;

        picks.Count.ShouldBe(200);
        picks.Select(p => p.Row.Id).ShouldBe(Enumerable.Range(1, 200).Select(n => $"a{n:000}"));
        picks.Count(p => p.Label == "material").ShouldBe(66);
    }

    [Fact]
    public void Each_unsure_is_replaced_by_the_next_reserve_row_and_an_unsure_reserve_is_passed_over()
    {
        var lines = All(n => n is 5 or 9 ? "unsure" : "material");
        lines[200] = Line(201, "unsure");
        lines[201] = Line(202, "Material");
        lines[202] = Line(203, "non_material");

        var picks = AnnouncementLabels.Resolve(Sample(), lines).Value;

        picks.Count.ShouldBe(200);
        picks.Select(p => p.Row.Id).Where(id => id.CompareTo("a200") > 0).ShouldBe(["a202", "a203"]);
        picks.Select(p => p.Row.Id).ShouldNotContain("a005");
        picks.First(p => p.Row.Id == "a202").Label.ShouldBe("material");
    }

    [Fact]
    public void Fewer_than_200_definite_labels_is_refused_when_the_reserve_runs_out()
    {
        var lines = All(n => n <= 42 ? "unsure" : "material");
        lines.RemoveAll(l => l.Id.CompareTo("a240") > 0);
        lines.Where(l => l.Id.CompareTo("a200") > 0).ToList().ForEach(l => lines[lines.IndexOf(l)] = l with { Label = "material" });

        var result = AnnouncementLabels.Resolve(Sample(), lines);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("eval.too_few_labels");
        result.Error.Message.ShouldContain("a041");
    }

    [Fact]
    public void A_needed_reserve_row_must_be_labelled()
    {
        var lines = All(n => n == 1 ? "unsure" : "material");
        lines[200] = Line(201, string.Empty);

        var result = AnnouncementLabels.Resolve(Sample(), lines);

        result.Error.Code.ShouldBe("eval.bad_label");
        result.Error.Message.ShouldContain("a201");
    }

    [Fact]
    public void An_unused_reserve_row_may_stay_blank()
    {
        var lines = All(_ => "material");
        lines[220] = Line(221, string.Empty);

        AnnouncementLabels.Resolve(Sample(), lines).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("maybe")]
    [InlineData("materially")]
    [InlineData("")]
    public void A_main_row_with_a_label_outside_the_three_words_or_none_is_refused_with_the_row_named(string label)
    {
        var lines = All(_ => "material");
        lines[6] = Line(7, label);

        var result = AnnouncementLabels.Resolve(Sample(), lines);

        result.Error.Code.ShouldBe("eval.bad_label");
        result.Error.Message.ShouldContain("a007");
        result.Error.Message.ShouldContain("line 8");
    }

    [Fact]
    public void A_changed_text_cell_an_unknown_id_a_duplicate_id_and_a_missing_row_are_each_refused_with_the_row_named()
    {
        var changed = All(_ => "material");
        changed[3] = Line(4, "material", "Subject 4 edited");
        var unknown = All(_ => "material");
        unknown.Add(Line(300, "material"));
        var duplicate = All(_ => "material");
        duplicate.Add(Line(3, "material"));
        var missing = All(_ => "material");
        missing.RemoveAt(9);

        var codes = new[] { changed, unknown, duplicate, missing }.Select(l => AnnouncementLabels.Resolve(Sample(), l).Error).ToList();

        codes.Select(e => e.Code).ShouldBe(["eval.text_changed", "eval.unknown_id", "eval.duplicate_id", "eval.missing_row"]);
        codes[0].Message.ShouldContain("a004");
        codes[2].Message.ShouldContain("a003");
        codes[3].Message.ShouldContain("a010");
        codes.ShouldAllBe(e => !e.Message.Contains("edited", StringComparison.Ordinal));
    }

    [Fact]
    public void The_row_hash_ignores_line_ending_and_edge_spaces_of_the_text_only()
    {
        var a = AnnouncementSampleCsv.RowHash("a001", "f", "S", "t", "c", "s", "one\ntwo");

        AnnouncementSampleCsv.RowHash("a001", "f", "S", "t", "c", "s", "one\r\ntwo ").ShouldBe(a);
        AnnouncementSampleCsv.RowHash("a001", "f", "S", "t", "c", "s", "one two").ShouldNotBe(a);
        AnnouncementSampleCsv.RowHash("a001", "f", "S", "t", "c ", "s", "one\ntwo").ShouldNotBe(a);
    }

    [Fact]
    public void The_labelling_file_round_trips_commas_quotes_and_line_breaks_and_reads_nothing_from_a_broken_file()
    {
        var candidate = new SampleCandidate(Guid(1), "TCS", new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero), "Results");
        var row = new SampledAnnouncement("a001", candidate, "material", false, 4, 1);
        var body = new AnnouncementBody("Results", "Sub, \"ject\"", "Line one\nLine, \"two\"");

        var csv = AnnouncementSampleCsv.Write([(row, body)]);
        var lines = AnnouncementSampleCsv.Read(csv)!;

        var line = lines.ShouldHaveSingleItem();
        (line.Id, line.Category, line.Subject, line.Text, line.Label, line.PublishedAt).ShouldBe(("a001", "Results", body.Subject, body.Text, string.Empty, "2026-08-03T05:30:00.000000Z"));
        AnnouncementSampleCsv.RowHash(line.Id, line.FilingId, line.Symbol, line.PublishedAt, line.Category, line.Subject, line.Text).ShouldBe(AnnouncementSampleCsv.RowHash("a001", Guid(1).ToString("D"), "TCS", "2026-08-03T05:30:00.000000Z", body.Category, body.Subject, body.Text));
        AnnouncementSampleCsv.Read("id,label\n1,2").ShouldBeNull();
        AnnouncementSampleCsv.Read(csv + "\"broken").ShouldBeNull();
    }

    [Fact]
    public void The_sample_file_and_the_manifest_hold_no_text_and_are_deterministic()
    {
        var sample = Sample();
        var picks = AnnouncementLabels.Resolve(sample, All(n => n % 2 == 0 ? "material" : "non_material")).Value;

        var manifest = AnnouncementManifest.Build(sample, "f".PadRight(64, 'f'), picks).ToJson();

        manifest.ShouldBe(AnnouncementManifest.Build(sample, "f".PadRight(64, 'f'), picks).ToJson());
        manifest.ShouldContain("\"class_counts\"");
        manifest.ShouldContain("\"material\": 100");
        manifest.ShouldNotContain("Text ");
        manifest.ShouldNotContain("Subject ");
        AnnouncementSampleFile.Parse(sample.ToJson())!.Rows.Count.ShouldBe(240);
        AnnouncementSampleFile.Parse("{}").ShouldBeNull();
        AnnouncementSampleFile.Parse("nope").ShouldBeNull();
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+1 and more")]
    [InlineData("-5 percent")]
    [InlineData("@SUM(A1)")]
    [InlineData("'=already quoted")]
    [InlineData("''-two")]
    [InlineData("'plain apostrophe")]
    public void A_cell_a_spreadsheet_would_run_is_exported_with_an_apostrophe_and_imported_clean_and_hashes_the_same(string text)
    {
        var candidate = new SampleCandidate(Guid(1), "TCS", new DateTimeOffset(2026, 8, 3, 5, 30, 0, TimeSpan.Zero), "Results");
        var body = new AnnouncementBody("Results", text, text);
        var csv = AnnouncementSampleCsv.Write([(new SampledAnnouncement("a001", candidate, "material", false, 1, 1), body)]);

        var line = AnnouncementSampleCsv.Read(csv)!.Single();

        if (text[0] is '=' or '+' or '-' or '@' || text.StartsWith("'=", StringComparison.Ordinal) || text.StartsWith("''-", StringComparison.Ordinal))
        {
            csv.ShouldContain("\"'" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"", Case.Sensitive, csv);
        }

        (line.Subject, line.Text).ShouldBe((text, text));
        AnnouncementSampleCsv.RowHash(line.Id, line.FilingId, line.Symbol, line.PublishedAt, line.Category, line.Subject, line.Text)
            .ShouldBe(AnnouncementSampleCsv.RowHash("a001", Guid(1).ToString("D"), "TCS", "2026-08-03T05:30:00.000000Z", "Results", text, text));
    }

    [Fact]
    public void A_spreadsheet_that_dropped_the_apostrophe_itself_leaves_a_cell_that_still_reads_clean()
    {
        AnnouncementSampleCsv.Unescape("=SUM(A1)").ShouldBe("=SUM(A1)");
        AnnouncementSampleCsv.Unescape("'=SUM(A1)").ShouldBe("=SUM(A1)");
        AnnouncementSampleCsv.Escape("normal").ShouldBe("normal");
    }
}
