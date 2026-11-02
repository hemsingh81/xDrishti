using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Filings;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Filings;
using XDrishti.Infrastructure.Ai.Assistant;
using XDrishti.Infrastructure.FilingArchive;
using XDrishti.Infrastructure.Filings;

namespace XDrishti.Api.IntegrationTests.Filings;

/// <summary>
/// P15-07 end to end on a real TimescaleDB and a real drop folder (criteria 1 to 5, 7 and the load-time limit): a synthetic announcements csv goes through the real source, the loader, the files store and the tables; a rerun inserts nothing; the point-in-time read shows an
/// announcement only from its broadcast time plus the lag; the sampler draws from what was stored, the labels import and set D's file is read back by the eval reader.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AnnouncementFolderTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Header = "symbol,broadcast_ts,category,subject,text\n";

    private static readonly string[] Categories = ["Results", "Credit Rating", "Trading Window", "Newspaper Publication"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "xd-announcement-it-" + Guid.NewGuid().ToString("N"));

    private string Inbox => Path.Combine(_root, "inbox");

    private string FilesPath => Path.Combine(_root, "files");

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Directory.CreateDirectory(Inbox);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Inbox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public override async ValueTask DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }

        await base.DisposeAsync();
    }

    private void Drop(string name, string content)
    {
        File.WriteAllText(Path.Combine(Inbox, name), content, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Inbox, name + ".meta.json"), """{"schema":1,"symbol":"*","kind":"announcement"}""", new UTF8Encoding(false));
    }

    private (LoadAnnouncementsHandler Loader, FilingFiles Files, IServiceScope Scope) Load()
    {
        var scope = Factory.Services.CreateScope();
        var p = scope.ServiceProvider;
        var source = new FolderDropFilingsSource(Options.Create(new FilingsOptions { InboxPath = Inbox, FilesPath = FilesPath }), TimeProvider.System, NullLogger<FolderDropFilingsSource>.Instance);
        var files = new FilingFiles(Options.Create(new FilingsOptions { InboxPath = Inbox, FilesPath = FilesPath }));
        return (new LoadAnnouncementsHandler(source, p.GetRequiredService<IFilingInstruments>(), p.GetRequiredService<IAnnouncementStore>(), files, p.GetRequiredService<IFilingSettings>(), p.GetRequiredService<TimeProvider>(), NullLogger<LoadAnnouncementsHandler>.Instance), files, scope);
    }

    private async Task<RowLoadSummary> LoadAsync()
    {
        var (loader, _, scope) = Load();
        using (scope)
        {
            return (await loader.HandleAsync(new LoadAnnouncementsCommand(), Ct)).Value;
        }
    }

    private static string Row(string symbol, DateTimeOffset at, string category, int n) =>
        $"{symbol},{at:yyyy-MM-ddTHH:mm:sszzz},{category},\"Subject {n}, with a comma\",\"Text of announcement {n}.\nSecond line, \"\"quoted\"\".\"";

    [Fact]
    public async Task A_synthetic_drop_loads_once_stores_each_text_and_a_rerun_inserts_nothing()
    {
        await SeedAsync("RELIANCE");
        await SeedAsync("TCS");
        Drop("announcements.csv", Header + string.Join("\n",
            Row("RELIANCE", new DateTimeOffset(2026, 8, 3, 11, 0, 0, TimeSpan.FromMinutes(330)), "Results", 1),
            Row("TCS", new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.FromMinutes(330)), "Trading Window", 2),
            "TCS,2026-08-04,Results,Undated subject,Undated text",
            "NOSUCH,2026-08-03T11:00:00+05:30,Results,S,T",
            "TCS,2026-08-03T11:00:00+05:30,Results,S," + new string('x', 4001)));
        var before = Directory.GetFiles(Inbox).Select(f => (Path.GetFileName(f), File.ReadAllText(f))).Order().ToList();

        var first = await LoadAsync();
        var second = await LoadAsync();

        (first.Files, first.Loaded, first.Unavailable, first.Duplicate).ShouldBe((1, 3, 1, 0));
        first.Rejected.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}={r.Value}").ShouldBe(["too_long=1", "unknown_instrument=1"]);
        (second.Loaded, second.Duplicate).ShouldBe((0, 3));
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings WHERE kind = 'announcement'")).ShouldBe(3);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.announcements")).ShouldBe(3);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings WHERE kind = 'announcement' AND broadcast_state = 'unavailable' AND broadcast_ts IS NULL AND unavailable_reason = 'date_only'")).ShouldBe(1);
        (await ScalarAsync<long>("SELECT count(*) FROM filings.filings WHERE kind = 'announcement' AND period_end IS NULL AND supersedes IS NULL")).ShouldBe(3);
        (await ScalarAsync<string>("SELECT category FROM filings.announcements a JOIN filings.filings f ON f.id = a.filing_id WHERE a.headline = 'Subject 1, with a comma'")).ShouldBe("Results");
        var sha = await ScalarAsync<string>("SELECT f.content_sha256 FROM filings.filings f JOIN filings.announcements a ON a.filing_id = f.id WHERE a.headline = 'Subject 1, with a comma'");
        var stored = await File.ReadAllTextAsync(Path.Combine(FilesPath, sha[..2], sha), Ct);
        stored.ShouldContain("Text of announcement 1.");
        RowCanonical.Sha256(stored).ShouldBe(sha);
        Directory.GetFiles(Inbox).Select(f => (Path.GetFileName(f), File.ReadAllText(f))).Order().ToList().ShouldBe(before);
    }

    [Fact]
    public async Task An_announcement_is_visible_only_from_its_broadcast_time_plus_the_lag()
    {
        var reliance = await SeedAsync("RELIANCE");
        var at = new DateTimeOffset(2026, 8, 3, 11, 0, 0, TimeSpan.FromMinutes(330));
        Drop("announcements.csv", Header + Row("RELIANCE", at, "Results", 1) + "\n" + Row("RELIANCE", at.AddHours(2), "Credit Rating", 2) + "\nRELIANCE,,Results,No time,No time text");
        await LoadAsync();
        using var scope = Factory.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IPointInTimeFilings>();
        var lag = TimeSpan.FromMinutes(scope.ServiceProvider.GetRequiredService<IFilingSettings>().LagMinutes);

        var before = await port.AsOf(reliance, FilingKind.Announcement, at + lag - TimeSpan.FromSeconds(1), Ct);
        var exactly = await port.AsOf(reliance, FilingKind.Announcement, at + lag, Ct);
        var later = await port.AsOf(reliance, FilingKind.Announcement, at.AddHours(2) + lag, Ct);

        before.Filings.ShouldBeEmpty();
        exactly.Filings.Count.ShouldBe(1);
        later.Filings.Count.ShouldBe(2);
        later.UnavailableCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_sample_is_drawn_from_what_is_stored_never_from_undated_or_text_files_and_the_labelled_set_reads_back()
    {
        foreach (var symbol in new[] { "RELIANCE", "TCS", "INFY" })
        {
            await SeedAsync(symbol);
        }

        var lines = new List<string>();
        var symbols = new[] { "RELIANCE", "TCS", "INFY" };
        for (var i = 0; i < 700; i++)
        {
            lines.Add(Row(symbols[i % 3], new DateTimeOffset(2024 + (i % 3), 1 + (i % 9), 1 + (i % 27), 9, 30, i % 60, TimeSpan.FromMinutes(330)), Categories[i % Categories.Length], i));
        }

        lines.Add("TCS,2026-01-01,Results,Undated subject,Undated text");
        Drop("announcements.csv", Header + string.Join("\n", lines));
        (await LoadAsync()).Loaded.ShouldBe(701);
        using (var scope = Factory.Services.CreateScope())
        {
            // an announcement file with one sidecar time (a text or pdf file): an ordinary envelope, no row in filings.announcements
            var instrument = await ScalarAsync<Guid>("SELECT id FROM market.instruments WHERE canonical_symbol = 'NSE:TCS'");
            var envelope = new NewFiling(instrument, FilingKind.Announcement, null, new DateTimeOffset(2025, 5, 5, 5, 0, 0, TimeSpan.Zero), BroadcastState.Ok, null, Now, new string('d', 64), 10, "notice.txt");
            (await scope.ServiceProvider.GetRequiredService<IFilingStore>().InsertAsync(envelope, Ct)).Outcome.ShouldBe(FilingInsertOutcome.Stored);
        }

        var evalRoot = Path.Combine(_root, "evalroot");
        Directory.CreateDirectory(Path.Combine(evalRoot, "eval", "sets"));
        Directory.CreateDirectory(Path.Combine(evalRoot, "eval", "fixture"));
        var repoEval = FindRepoEval();
        foreach (var name in new[] { "thresholds.json", "sets/announcement-baseline.json", "fixture/fixture.json", "sets/data-questions.jsonl", "sets/plans.jsonl", "sets/ideas.jsonl" })
        {
            File.Copy(Path.Combine(repoEval, name), Path.Combine(evalRoot, "eval", name));
        }

        var eval = new AnnouncementEvalFiles(Options.Create(new EvalOptions { Folder = Path.Combine(evalRoot, "eval") }));
        using var read = Factory.Services.CreateScope();
        var files = new FilingFiles(Options.Create(new FilingsOptions { InboxPath = Inbox, FilesPath = FilesPath }));
        var candidates = new AnnouncementCandidateReader(Factory.Services.GetRequiredService<Npgsql.NpgsqlDataSource>(), files);
        var sampler = new SampleAnnouncementsHandler(candidates, eval);
        var command = new SampleAnnouncementsCommand(new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31), 1337);

        var sampled = await sampler.HandleAsync(command, Ct);
        var again = await sampler.HandleAsync(command, Ct);

        sampled.IsSuccess.ShouldBeTrue(sampled.IsFailure ? sampled.Error.Message : string.Empty);
        again.Value.Changed.ShouldBeFalse();
        sampled.Value.Lines.ShouldContain(l => l.Contains("announcement files without row metadata 1", StringComparison.Ordinal));
        var sample = AnnouncementSampleFile.Parse((await eval.ReadAsync(AnnouncementFile.Sample, Ct))!)!;
        (sample.Eligible, sample.Rows.Count).ShouldBe((700, 240));
        var toLabel = (await eval.ReadAsync(AnnouncementFile.ToLabel, Ct))!;
        toLabel.ShouldNotContain("Undated text");
        toLabel.ShouldContain("Text of announcement");

        var labelled = new StringBuilder(string.Join(',', AnnouncementSampleCsv.Header) + "\n");
        foreach (var l in AnnouncementSampleCsv.Read(toLabel)!)
        {
            labelled.Append(string.Join(',', new[] { l.Id, l.FilingId, l.Symbol, l.PublishedAt, l.Category, l.Subject, l.Text, l.Category == "Results" ? "material" : "non_material" }.Select(c => "\"" + c.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""))).Append('\n');
        }

        var imported = await new ImportAnnouncementLabelsHandler(eval).HandleAsync(new ImportAnnouncementLabelsCommand(labelled.ToString()), Ct);

        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Error.Message : string.Empty);
        var set = await new EvalFileReader(Options.Create(new EvalOptions { Folder = Path.Combine(evalRoot, "eval") })).ReadAsync(Ct);
        AnnouncementSet.Parse(set.Value.Announcements!).Value.Count.ShouldBe(200);
        set.Value.AnnouncementManifest!.ShouldNotContain("Text of announcement");
    }

    [Fact]
    public async Task Thirty_two_thousand_rows_load_within_thirty_seconds()
    {
        var symbols = Enumerable.Range(0, 100).Select(i => "L" + i.ToString("000", CultureInfo.InvariantCulture)).ToList();
        foreach (var symbol in symbols)
        {
            await SeedAsync(symbol);
        }

        for (var file = 0; file < 2; file++)
        {
            var lines = Enumerable.Range(0, 16_000).Select(i => Row(symbols[i % symbols.Count], new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.FromMinutes(330)).AddMinutes((file * 16_000) + i), Categories[i % 4], (file * 16_000) + i));
            Drop($"big{file}.csv", Header + string.Join("\n", lines));
        }

        var watch = Stopwatch.StartNew();
        var summary = await LoadAsync();
        watch.Stop();

        summary.Loaded.ShouldBe(32_000);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30), $"loading took {watch.Elapsed}");
    }

    private static string FindRepoEval()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "eval", "thresholds.json")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "eval");
    }
}
