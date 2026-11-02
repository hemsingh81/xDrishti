using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Tests.Filings;

/// <summary>P15-07 criteria 4, 5, 6 and 9 for the three verbs: sample, import and show, with in-memory files.</summary>
public sealed class AnnouncementEvalHandlersTests
{
    private const string BaselineJson = """{"version":1,"categories":{"Results":"material","Credit Rating":"material","Trading Window":"non_material"}}""";

    private static readonly string[] Categories = ["Results", "Credit Rating", "Trading Window", "Newspaper"];

    private static readonly string[] Replaced = ["a001", "a002", "a201", "a202"];

    private readonly FakeAnnouncementCandidates _candidates = new();

    private readonly FakeAnnouncementEvalFiles _files = new();

    public AnnouncementEvalHandlersTests()
    {
        _files.Files[AnnouncementFile.Baseline] = BaselineJson;
        for (var i = 0; i < 600; i++)
        {
            var id = new Guid(i + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]);
            var category = Categories[i % Categories.Length];
            _candidates.Dated.Add(new SampleCandidate(id, "SYM" + (i % 7), new DateTimeOffset(2024 + (i % 3), 1 + (i % 12), 1 + (i % 27), 5, 30, 0, TimeSpan.Zero), category));
            _candidates.Bodies[id] = new AnnouncementBody(category, $"Subject {i}, with \"comma\"", $"Text of announcement {i}.\nSecond line {i}.");
        }
    }

    private SampleAnnouncementsHandler Sampler() => new(_candidates, _files);

    private ImportAnnouncementLabelsHandler Importer() => new(_files);

    private async Task<AnnouncementEvalReport> SampleAsync(long seed = 1337) =>
        (await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), seed), TestContext.Current.CancellationToken)).Value;

    private static string Label(string csv, Func<LabelLine, string> label)
    {
        var text = new System.Text.StringBuilder(string.Join(',', AnnouncementSampleCsv.Header) + "\n");
        foreach (var l in AnnouncementSampleCsv.Read(csv)!)
        {
            text.Append(string.Join(',', new[] { l.Id, l.FilingId, l.Symbol, l.PublishedAt, l.Category, l.Subject, l.Text, label(l) }.Select(c => "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""))).Append('\n');
        }

        return text.ToString();
    }

    [Fact]
    public async Task Sample_writes_the_labelling_file_and_a_sample_file_with_no_text_and_the_baseline_hash()
    {
        var report = await SampleAsync();

        report.Changed.ShouldBeTrue();
        _files.Writes.ShouldBe([AnnouncementFile.ToLabel, AnnouncementFile.Sample]);
        var sample = AnnouncementSampleFile.Parse(_files.Files[AnnouncementFile.Sample])!;
        sample.BaselineSha256.ShouldBe(AnnouncementCategoryBaseline.HashOf(BaselineJson));
        (sample.Seed, sample.Rows.Count, sample.Rows.Count(r => r.Reserve), sample.Eligible).ShouldBe((1337, 240, 40, 600));
        _files.Files[AnnouncementFile.Sample].ShouldNotContain("Text of announcement");
        _files.Files[AnnouncementFile.Sample].ShouldNotContain("Subject ");
        var csv = AnnouncementSampleCsv.Read(_files.Files[AnnouncementFile.ToLabel])!;
        csv.Count.ShouldBe(240);
        csv.ShouldAllBe(l => l.Label.Length == 0 && l.Text.StartsWith("Text of announcement", StringComparison.Ordinal));
        report.Lines.ShouldContain(l => l.Contains("240", StringComparison.Ordinal) || l.Contains("200 main", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sample_is_the_same_whatever_the_insert_order_and_the_same_sample_again_is_a_no_op_while_another_is_refused()
    {
        await SampleAsync();
        var first = (_files.Files[AnnouncementFile.ToLabel], _files.Files[AnnouncementFile.Sample]);
        _candidates.Dated.Reverse();

        var again = await SampleAsync();
        var other = await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), 7), TestContext.Current.CancellationToken);

        again.Changed.ShouldBeFalse();
        (_files.Files[AnnouncementFile.ToLabel], _files.Files[AnnouncementFile.Sample]).ShouldBe(first);
        _files.Writes.Count.ShouldBe(2);
        other.Error.Code.ShouldBe("eval.sample_exists");
    }

    [Fact]
    public async Task Sample_reads_the_range_as_india_days_and_refuses_a_backward_range_a_missing_baseline_and_too_few_rows()
    {
        await SampleAsync();
        var range = _candidates.Candidates();

        range.From.ShouldBe(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
        range.To.ShouldBe(new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.FromMinutes(330)));
        (await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), 1), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("eval.bad_range");
        _files.Files.Remove(AnnouncementFile.Baseline);
        (await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), 1), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("eval.baseline_missing");
        _files.Files[AnnouncementFile.Baseline] = "{";
        (await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), 1), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("eval.baseline_invalid");
        _files.Files[AnnouncementFile.Baseline] = BaselineJson;
        _candidates.Dated.RemoveRange(100, 400);
        var few = await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), 1), TestContext.Current.CancellationToken);
        few.Error.Code.ShouldBe("eval.too_few_announcements");
        few.Error.Message.ShouldContain("200");
    }

    [Fact]
    public async Task Sample_refuses_and_writes_nothing_when_a_stored_text_cannot_be_read()
    {
        _candidates.Bodies.Clear();

        var result = await Sampler().HandleAsync(new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), 1), TestContext.Current.CancellationToken);

        result.Error.Code.ShouldBe("eval.text_missing");
        _files.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_of_a_valid_file_writes_the_labelled_set_and_a_manifest_with_no_text_and_importing_it_again_is_a_no_op()
    {
        await SampleAsync();
        var labelled = Label(_files.Files[AnnouncementFile.ToLabel], l => l.Id.CompareTo("a200") <= 0 && int.Parse(l.Id[1..], System.Globalization.CultureInfo.InvariantCulture) % 4 == 0 ? "material" : "non_material");

        var first = await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(labelled), TestContext.Current.CancellationToken);
        var writes = _files.Writes.Count;
        var second = await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(labelled), TestContext.Current.CancellationToken);

        first.Value.Changed.ShouldBeTrue();
        second.Value.Changed.ShouldBeFalse();
        _files.Writes.Count.ShouldBe(writes);
        var rows = AnnouncementSet.Parse(_files.Files[AnnouncementFile.Labelled]).Value;
        rows.Count.ShouldBe(200);
        rows.ShouldAllBe(r => r.Source == "nse-announcement" && r.Category != null && r.Text.StartsWith("Text of announcement", StringComparison.Ordinal));
        rows.Count(r => r.Material).ShouldBe(50);
        var manifest = _files.Files[AnnouncementFile.Manifest];
        manifest.ShouldContain(AnnouncementCategoryBaseline.HashOf(BaselineJson));
        manifest.ShouldContain(AnnouncementCategoryBaseline.HashOf(labelled));
        manifest.ShouldContain("\"material\": 50");
        manifest.ShouldNotContain("Text of announcement");
        manifest.ShouldNotContain("Subject ");
    }

    [Fact]
    public async Task Import_refuses_a_baseline_changed_since_the_sample_a_missing_sample_a_changed_text_and_a_broken_file()
    {
        (await Importer().HandleAsync(new ImportAnnouncementLabelsCommand("x"), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("eval.no_sample");
        await SampleAsync();
        var labelled = Label(_files.Files[AnnouncementFile.ToLabel], _ => "material");

        (await Importer().HandleAsync(new ImportAnnouncementLabelsCommand("not,the,header\n1,2,3"), TestContext.Current.CancellationToken)).Error.Code.ShouldBe("eval.bad_file");
        var edited = await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(labelled.Replace("Text of announcement", "Edited announcement", StringComparison.Ordinal)), TestContext.Current.CancellationToken);
        _files.Files[AnnouncementFile.Baseline] = BaselineJson.Replace("Results", "Result", StringComparison.Ordinal);
        var changed = await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(labelled), TestContext.Current.CancellationToken);

        edited.Error.Code.ShouldBe("eval.text_changed");
        changed.Error.Code.ShouldBe("eval.baseline_changed");
        _files.Files.ContainsKey(AnnouncementFile.Labelled).ShouldBeFalse();
        _files.Files.ContainsKey(AnnouncementFile.Manifest).ShouldBeFalse();
    }

    [Fact]
    public async Task Import_replaces_unsure_rows_by_reserve_rows_and_refuses_when_too_few_definite_labels_remain()
    {
        await SampleAsync();
        var csv = _files.Files[AnnouncementFile.ToLabel];
        var withUnsure = Label(csv, l => l.Id is "a003" or "a004" ? "unsure" : "material");
        var allUnsure = Label(csv, l => int.Parse(l.Id[1..], System.Globalization.CultureInfo.InvariantCulture) <= 200 ? "unsure" : "material");

        var ok = await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(withUnsure), TestContext.Current.CancellationToken);
        var refused = await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(allUnsure), TestContext.Current.CancellationToken);

        ok.Value.Lines.ShouldContain(l => l.Contains("2 replaced by reserve rows", StringComparison.Ordinal));
        AnnouncementSet.Parse(_files.Files[AnnouncementFile.Labelled]).Value.Select(r => r.Id).ShouldBe(Replaced.Concat(Enumerable.Range(5, 196).Select(n => $"a{n:000}")));
        refused.Error.Code.ShouldBe("eval.too_few_labels");
    }

    [Fact]
    public async Task Show_says_it_is_waiting_for_a_sample_then_for_labels_then_prints_counts_per_half_the_baseline_and_the_honest_notes()
    {
        var none = await new ShowAnnouncementsHandler(_files).HandleAsync(new ShowAnnouncementsQuery(), TestContext.Current.CancellationToken);
        await SampleAsync();
        var waiting = await new ShowAnnouncementsHandler(_files).HandleAsync(new ShowAnnouncementsQuery(), TestContext.Current.CancellationToken);
        await Importer().HandleAsync(new ImportAnnouncementLabelsCommand(Label(_files.Files[AnnouncementFile.ToLabel], l => l.Category == "Results" ? "material" : "non_material")), TestContext.Current.CancellationToken);
        var shown = await new ShowAnnouncementsHandler(_files).HandleAsync(new ShowAnnouncementsQuery(), TestContext.Current.CancellationToken);

        none.Lines.ShouldHaveSingleItem().ShouldContain("no sample yet");
        waiting.Lines.ShouldContain(l => l.StartsWith("waiting for labels", StringComparison.Ordinal));
        var text = string.Join("\n", shown.Lines);
        text.ShouldContain("labelled: 200 announcements");
        text.ShouldContain("half material:");
        text.ShouldContain("half other:");
        text.ShouldContain("category baseline: predicts material for");
        text.ShouldContain("stratified estimate");
        text.ShouldContain(ShowAnnouncementsHandler.Formula);
        text.ShouldNotContain("Text of announcement");
        text.ShouldNotContain("Subject ");
    }
}
