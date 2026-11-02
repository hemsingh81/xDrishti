using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Infrastructure.Ai.Assistant;
using XDrishti.Infrastructure.FilingArchive;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>P15-07: the announcement eval files sit at their places, the data ones 0600 in 0700, a write goes through a temporary name, the baseline is never written, and nothing is deleted.</summary>
public sealed class AnnouncementEvalFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xd-announcement-files-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public AnnouncementEvalFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "eval", "sets"));
        File.WriteAllText(Path.Combine(_root, "eval", "thresholds.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "eval", "sets", "announcement-baseline.json"), "{\"version\":1,\"categories\":{}}");
    }

    private AnnouncementEvalFiles Files(string? dataFolder = null) => new(Options.Create(new EvalOptions { Folder = Path.Combine(_root, "eval"), DataFolder = dataFolder ?? string.Empty }));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task Files_are_written_to_their_places_read_back_and_a_missing_file_reads_as_null()
    {
        var files = Files();

        (await files.ReadAsync(AnnouncementFile.Sample, Ct)).ShouldBeNull();
        await files.WriteAsync(AnnouncementFile.Sample, "{\"a\":1}\n", Ct);
        await files.WriteAsync(AnnouncementFile.ToLabel, "id\n", Ct);
        await files.WriteAsync(AnnouncementFile.Labelled, "{}\n", Ct);
        await files.WriteAsync(AnnouncementFile.Manifest, "{\"m\":1}\n", Ct);

        (await files.ReadAsync(AnnouncementFile.Sample, Ct)).ShouldBe("{\"a\":1}\n");
        File.Exists(Path.Combine(_root, "data", "eval", "announcements", "sample.json")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "data", "eval", "announcements", "to-label.csv")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "data", "eval", "sets", "announcements.jsonl")).ShouldBeTrue();
        File.Exists(Path.Combine(_root, "eval", "sets", "announcements.manifest.json")).ShouldBeTrue();
        (await files.ReadAsync(AnnouncementFile.Baseline, Ct))!.ShouldContain("categories");
    }

    [Fact]
    public async Task The_data_files_are_0600_in_0700_folders_and_the_committed_manifest_is_0644()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var files = Files();
        await files.WriteAsync(AnnouncementFile.Labelled, "{}\n", Ct);
        await files.WriteAsync(AnnouncementFile.Manifest, "{}\n", Ct);

        File.GetUnixFileMode(Path.Combine(_root, "data", "eval", "sets", "announcements.jsonl")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(Path.Combine(_root, "data", "eval", "sets")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(Path.Combine(_root, "eval", "sets", "announcements.manifest.json")).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
    }

    [Fact]
    public async Task A_rewrite_replaces_the_file_whole_and_leaves_no_temporary_file()
    {
        var files = Files();
        await files.WriteAsync(AnnouncementFile.Labelled, new string('a', 5000), Ct);

        await files.WriteAsync(AnnouncementFile.Labelled, "short\n", Ct);

        (await files.ReadAsync(AnnouncementFile.Labelled, Ct)).ShouldBe("short\n");
        Directory.GetFiles(Path.Combine(_root, "data", "eval", "sets")).Select(Path.GetFileName).ShouldBe(["announcements.jsonl"]);
    }

    [Fact]
    public async Task The_baseline_is_never_written_by_the_app()
    {
        var before = await File.ReadAllTextAsync(Path.Combine(_root, "eval", "sets", "announcement-baseline.json"), Ct);

        await Should.ThrowAsync<InvalidOperationException>(async () => await Files().WriteAsync(AnnouncementFile.Baseline, "{}", Ct));

        (await File.ReadAllTextAsync(Path.Combine(_root, "eval", "sets", "announcement-baseline.json"), Ct)).ShouldBe(before);
    }

    [Fact]
    public async Task The_configured_data_folder_is_used_and_the_eval_reader_finds_the_labelled_set_and_the_manifest_there()
    {
        var data = Path.Combine(_root, "elsewhere");
        var files = Files(data);
        await files.WriteAsync(AnnouncementFile.Labelled, "{\"x\":1}\n", Ct);
        await files.WriteAsync(AnnouncementFile.Manifest, "{\"m\":2}\n", Ct);
        var options = Options.Create(new EvalOptions { Folder = Path.Combine(_root, "eval"), DataFolder = data });
        Directory.CreateDirectory(Path.Combine(_root, "eval", "fixture"));
        foreach (var name in new[] { "fixture/fixture.json", "sets/data-questions.jsonl", "sets/plans.jsonl", "sets/ideas.jsonl" })
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "eval", name), "{}", Ct);
        }

        var read = await new EvalFileReader(options).ReadAsync(Ct);

        read.IsSuccess.ShouldBeTrue();
        (read.Value.Announcements, read.Value.AnnouncementManifest).ShouldBe(("{\"x\":1}\n", "{\"m\":2}\n"));
        read.Value.AnnouncementBaseline!.ShouldContain("categories");
        File.Exists(Path.Combine(data, "sets", "announcements.jsonl")).ShouldBeTrue();
    }

    [Fact]
    public async Task With_no_labelled_file_the_eval_reader_reports_none()
    {
        Directory.CreateDirectory(Path.Combine(_root, "eval", "fixture"));
        foreach (var name in new[] { "fixture/fixture.json", "sets/data-questions.jsonl", "sets/plans.jsonl", "sets/ideas.jsonl" })
        {
            await File.WriteAllTextAsync(Path.Combine(_root, "eval", name), "{}", Ct);
        }

        var read = await new EvalFileReader(Options.Create(new EvalOptions { Folder = Path.Combine(_root, "eval"), DataFolder = Path.Combine(_root, "none") })).ReadAsync(Ct);

        read.Value.Announcements.ShouldBeNull();
    }
}
