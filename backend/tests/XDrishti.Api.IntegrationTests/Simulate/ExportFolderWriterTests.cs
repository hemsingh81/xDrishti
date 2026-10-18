using System.Runtime.Versioning;
using System.Text;
using XDrishti.Api.Simulate;
using XDrishti.SimExport.Manifest;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>Criteria 9 and 10 of P3-04b1: the folder writer puts the files where they belong, atomically and owner-only, and its overwrite policy never loses an export. Plain temp-directory tests: no database, no container.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class ExportFolderWriterTests
{
    private const UnixFileMode Folder700 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private const UnixFileMode File600 = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly string[] FourFiles = ["README.txt", "open_at_end.csv", "run.json", "trades.csv"];

    private static string Run(string root) => Path.Combine(root, "sim", ExportFixture.RunId);

    private static string Previous(string root) => Run(root) + ".previous";

    private static string[] Names(string folder) => [.. Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    // 9. the folder

    [Fact]
    public async Task A_run_into_an_empty_root_holds_exactly_the_manifest_and_the_four_files()
    {
        using var temp = new TempFolder();

        var result = await ExportFixture.WriteAsync(temp.Root);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Status.ShouldBe(ExportStatus.Written);
        result.Value.Folder.ShouldBe(Run(temp.Root));
        Names(Run(temp.Root)).ShouldBe(["README.txt", "manifest.json", "open_at_end.csv", "run.json", "trades.csv"]);
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId], "no staging folder is left");
    }

    [Fact]
    public async Task The_root_the_sim_folder_and_the_run_folder_are_0700_and_the_files_0600()
    {
        using var temp = new TempFolder();

        await ExportFixture.WriteAsync(temp.Root);

        File.GetUnixFileMode(temp.Root).ShouldBe(Folder700);
        File.GetUnixFileMode(Path.Combine(temp.Root, "sim")).ShouldBe(Folder700);
        File.GetUnixFileMode(Run(temp.Root)).ShouldBe(Folder700, "the run folder is the staging folder renamed, made 0700 at creation");
        foreach (var file in Directory.EnumerateFiles(Run(temp.Root)))
        {
            File.GetUnixFileMode(file).ShouldBe(File600, file);
        }
    }

    [Fact]
    public async Task An_existing_root_is_not_changed()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
        var before = File.GetUnixFileMode(temp.Root);

        await ExportFixture.WriteAsync(temp.Root);

        File.GetUnixFileMode(temp.Root).ShouldBe(before);
        File.GetUnixFileMode(Path.Combine(temp.Root, "sim")).ShouldBe(Folder700);
    }

    [Fact]
    public async Task The_manifest_lists_the_four_files_with_the_length_and_hash_of_what_is_on_disk_and_the_export_key()
    {
        using var temp = new TempFolder();

        var result = await ExportFixture.WriteAsync(temp.Root);

        var parsed = ExportManifest.Parse(File.ReadAllBytes(Path.Combine(Run(temp.Root), "manifest.json")));
        parsed.IsSuccess.ShouldBeTrue();
        parsed.Value.Entries.Select(e => e.Name).ShouldBe(FourFiles);
        foreach (var entry in parsed.Value.Entries)
        {
            var path = Path.Combine(Run(temp.Root), entry.Name);
            entry.Bytes.ShouldBe(new FileInfo(path).Length, entry.Name);
            entry.Sha256.ShouldBe(ExportFixture.Sha256(path), entry.Name);
        }

        parsed.Value.ExportKey.ShouldBe(ExportKey.Of(ExportFixture.Format, ExportFixture.RunKey, null, parsed.Value.Entries, []));
        result.Value.ExportKey.ShouldBe(parsed.Value.ExportKey);
        result.Value.Entries.ShouldBe(parsed.Value.Entries);
    }

    [Fact]
    public async Task Two_runs_into_two_roots_give_identical_bytes()
    {
        using var first = new TempFolder();
        using var second = new TempFolder("other");

        await ExportFixture.WriteAsync(first.Root);
        await ExportFixture.WriteAsync(second.Root);

        string[] names = ["manifest.json", .. FourFiles];
        foreach (var name in names)
        {
            File.ReadAllBytes(Path.Combine(Run(first.Root), name)).ShouldBe(File.ReadAllBytes(Path.Combine(Run(second.Root), name)), name);
        }
    }

    [Fact]
    public async Task A_root_with_spaces_works()
    {
        using var temp = new TempFolder("a root with spaces");

        var result = await ExportFixture.WriteAsync(Path.Combine(temp.Path, "my exports"));

        result.IsSuccess.ShouldBeTrue();
        File.Exists(Path.Combine(temp.Path, "my exports", "sim", ExportFixture.RunId, "manifest.json")).ShouldBeTrue();
    }

    [Fact]
    public async Task A_file_in_a_subfolder_and_an_empty_file_are_written_and_listed()
    {
        using var temp = new TempFolder();
        ExportFile[] files = [new("cards/0001-x.html", _ => { }), new("aaa.txt", _ => { }), .. ExportFixture.Files()];

        var result = await ExportFixture.WriteAsync(temp.Root, files);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Entries.Select(e => e.Name).ShouldContain("cards/0001-x.html");
        File.GetUnixFileMode(Path.Combine(Run(temp.Root), "cards")).ShouldBe(Folder700);
        new FileInfo(Path.Combine(Run(temp.Root), "cards", "0001-x.html")).Length.ShouldBe(0);
        result.Value.Entries.Single(e => e.Name == "aaa.txt").Sha256.ShouldBe("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
        ExportManifest.Parse(File.ReadAllBytes(Path.Combine(Run(temp.Root), "manifest.json"))).Value.Entries.Count.ShouldBe(6);
    }

    [Fact]
    public async Task A_file_name_that_sorts_before_the_manifest_does_not_matter()
    {
        using var temp = new TempFolder();

        var result = await ExportFixture.WriteAsync(temp.Root, [ExportFixture.Text("0-first.txt", "x"), ExportFixture.Text("a.txt", "y")]);

        result.IsSuccess.ShouldBeTrue();
        Names(Run(temp.Root)).ShouldBe(["0-first.txt", "a.txt", "manifest.json"]);
    }

    // 10. the overwrite policy

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_same_input_again_is_unchanged_after_hashing_every_file_and_nothing_is_renamed(bool force)
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root);
        var before = ExportFixture.Tree(temp.Root);
        var moves = new List<(string From, string To)>();
        var writer = new ExportFolderWriter((from, to) =>
        {
            moves.Add((from, to));
            Directory.Move(from, to);
        });

        var result = await ExportFixture.WriteAsync(temp.Root, force: force, writer: writer);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ExportStatus.Unchanged);
        moves.ShouldBeEmpty();
        ExportFixture.Tree(temp.Root).ShouldBe(before, "no staging folder, no previous folder, no new file");
    }

    [Fact]
    public async Task Another_content_is_a_refusal_without_force_and_never_unchanged()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root);
        var kept = File.ReadAllBytes(Path.Combine(Run(temp.Root), "run.json"));

        var result = await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("another fills mode"));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExportErrors.ExistsDifferentCode);
        result.Error.Message.ShouldContain(ExportFixture.RunId);
        File.ReadAllBytes(Path.Combine(Run(temp.Root), "run.json")).ShouldBe(kept);
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId], "the staging folder is gone");
    }

    [Fact]
    public async Task Another_basket_with_the_same_run_key_is_a_refusal()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root, basket: null);

        var result = await ExportFixture.WriteAsync(temp.Root, basket: ExportFixture.Basket);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExportErrors.ExistsDifferentCode);
    }

    [Theory]
    [InlineData("hand-edited")]
    [InlineData("hand-edited, same length")]
    [InlineData("missing file")]
    [InlineData("extra file")]
    [InlineData("extra folder")]
    [InlineData("extra file in a folder")]
    [InlineData("a file replaced by a link")]
    public async Task A_folder_that_was_touched_is_not_unchanged(string touch)
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root);
        var run = Run(temp.Root);
        switch (touch)
        {
            case "hand-edited":
                File.AppendAllText(Path.Combine(run, "trades.csv"), "extra\n");
                break;
            case "hand-edited, same length":
                var bytes = File.ReadAllBytes(Path.Combine(run, "trades.csv"));
                bytes[^3] = (byte)(bytes[^3] == 'X' ? 'Y' : 'X');
                File.WriteAllBytes(Path.Combine(run, "trades.csv"), bytes);
                break;
            case "missing file":
                File.Delete(Path.Combine(run, "run.json"));
                break;
            case "extra file":
                File.WriteAllText(Path.Combine(run, "notes.txt"), "mine");
                break;
            case "extra folder":
                Directory.CreateDirectory(Path.Combine(run, "scratch"));
                break;
            case "extra file in a folder":
                Directory.CreateDirectory(Path.Combine(run, "scratch"));
                File.WriteAllText(Path.Combine(run, "scratch", "x"), "mine");
                break;
            default:
                File.Delete(Path.Combine(run, "README.txt"));
                File.CreateSymbolicLink(Path.Combine(run, "README.txt"), Path.Combine(run, "run.json"));
                break;
        }

        var result = await ExportFixture.WriteAsync(temp.Root);

        result.IsFailure.ShouldBeTrue(touch);
        result.Error.Code.ShouldBe(ExportErrors.ExistsDifferentCode, touch);
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId], "the staging folder is gone");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("")]
    public async Task An_unreadable_manifest_is_a_refusal_and_is_not_replaced_even_with_force(string manifest)
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root);
        File.WriteAllText(Path.Combine(Run(temp.Root), "manifest.json"), manifest);

        foreach (var force in new[] { false, true })
        {
            var result = await ExportFixture.WriteAsync(temp.Root, force: force);

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(ExportErrors.NoValidManifestCode);
        }

        File.Exists(Previous(temp.Root)).ShouldBeFalse();
        Directory.Exists(Previous(temp.Root)).ShouldBeFalse();
        File.ReadAllText(Path.Combine(Run(temp.Root), "manifest.json")).ShouldBe(manifest);
    }

    [Fact]
    public async Task A_manifest_that_cannot_be_read_is_a_refusal()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root);
        var manifest = Path.Combine(Run(temp.Root), "manifest.json");
        File.SetUnixFileMode(manifest, UnixFileMode.None);
        try
        {
            var result = await ExportFixture.WriteAsync(temp.Root, force: true);

            if (!File.Exists(manifest) || Environment.UserName == "root")
            {
                return;
            }

            result.IsFailure.ShouldBeTrue();
            result.Error.Code.ShouldBe(ExportErrors.NoValidManifestCode);
        }
        finally
        {
            File.SetUnixFileMode(manifest, File600);
        }
    }

    [Fact]
    public async Task A_manifest_that_was_edited_is_not_valid()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root);
        var manifest = Path.Combine(Run(temp.Root), "manifest.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"bytes\":", "\"bytes\": ", StringComparison.Ordinal).Replace("\"run_key\":\"3fa", "\"run_key\":\"4fa", StringComparison.Ordinal));

        var result = await ExportFixture.WriteAsync(temp.Root, force: true);

        result.Error.Code.ShouldBe(ExportErrors.NoValidManifestCode);
    }

    [Fact]
    public async Task A_run_folder_that_is_a_file_is_not_replaced_even_with_force()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Root, "sim"));
        File.WriteAllText(Run(temp.Root), "a file where the folder goes");

        var result = await ExportFixture.WriteAsync(temp.Root, force: true);

        result.Error.Code.ShouldBe(ExportErrors.NoValidManifestCode);
        File.ReadAllText(Run(temp.Root)).ShouldBe("a file where the folder goes");
    }

    [Fact]
    public async Task Force_with_a_valid_manifest_keeps_the_old_folder_as_previous_and_writes_the_new_one_complete()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("old"));
        var oldRun = File.ReadAllBytes(Path.Combine(Run(temp.Root), "run.json"));

        var result = await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("new"), force: true);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(ExportStatus.ReplacedPrevious);
        result.Value.PreviousFolder.ShouldBe(Previous(temp.Root));
        File.ReadAllBytes(Path.Combine(Previous(temp.Root), "run.json")).ShouldBe(oldRun);
        Names(Run(temp.Root)).ShouldBe(["README.txt", "manifest.json", "open_at_end.csv", "run.json", "trades.csv"]);
        Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(Run(temp.Root), "run.json"))).ShouldContain("new");
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId, ExportFixture.RunId + ".previous"]);
        (await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("new"))).Value.Status.ShouldBe(ExportStatus.Unchanged);
    }

    [Fact]
    public async Task A_second_force_while_previous_exists_is_a_refusal_that_names_the_folder_and_nothing_is_deleted()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("one"));
        await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("two"), force: true);

        var result = await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("three"), force: true);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExportErrors.PreviousExistsCode);
        result.Error.Message.ShouldContain(Previous(temp.Root));
        File.ReadAllText(Path.Combine(Previous(temp.Root), "run.json")).ShouldContain("one");
        File.ReadAllText(Path.Combine(Run(temp.Root), "run.json")).ShouldContain("two");
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId, ExportFixture.RunId + ".previous"], "no staging folder left");
    }

    [Fact]
    public async Task When_the_second_rename_fails_the_previous_folder_is_put_back_and_the_failure_is_raised()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("old"));
        var calls = 0;
        var writer = new ExportFolderWriter((from, to) =>
        {
            calls++;
            if (calls == 2)
            {
                throw new IOException("the second rename fails");
            }

            Directory.Move(from, to);
        });

        var failure = await Should.ThrowAsync<IOException>(() => ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("new"), force: true, writer: writer));

        failure.Message.ShouldBe("the second rename fails");
        calls.ShouldBe(3, "the rename of the old folder, the failing rename, the rename back");
        File.ReadAllText(Path.Combine(Run(temp.Root), "run.json")).ShouldContain("old");
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId], "the old export is back, there is no previous folder and the staging folder is gone");
    }

    [Fact]
    public async Task When_the_rename_back_fails_too_the_exception_names_both_folders()
    {
        using var temp = new TempFolder();
        await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("old"));
        var calls = 0;
        var writer = new ExportFolderWriter((from, to) =>
        {
            calls++;
            if (calls >= 2)
            {
                throw new IOException("rename " + calls + " fails");
            }

            Directory.Move(from, to);
        });

        var failure = await Should.ThrowAsync<IOException>(() => ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("new"), force: true, writer: writer));

        failure.Message.ShouldContain(Run(temp.Root));
        failure.Message.ShouldContain(Previous(temp.Root));
        failure.InnerException!.Message.ShouldBe("rename 2 fails");
        Directory.Exists(Previous(temp.Root)).ShouldBeTrue("the old export is complete in the previous folder");
        File.ReadAllText(Path.Combine(Previous(temp.Root), "run.json")).ShouldContain("old");
    }

    [Fact]
    public async Task A_failure_while_rendering_leaves_no_run_folder_and_removes_only_the_staging_folder_it_made()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Root, "sim", ".staging-someone-else-1234"), Folder700);
        File.WriteAllText(Path.Combine(temp.Root, "sim", ".staging-someone-else-1234", "keep"), "a killed process left this");
        ExportFile[] files = [ExportFixture.Text("a.txt", "x"), new("b.txt", _ => throw new InvalidOperationException("render failed"))];

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => ExportFixture.WriteAsync(temp.Root, files));

        failure.Message.ShouldBe("render failed");
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([".staging-someone-else-1234"]);
        File.ReadAllText(Path.Combine(temp.Root, "sim", ".staging-someone-else-1234", "keep")).ShouldBe("a killed process left this");
    }

    [Fact]
    public async Task A_token_cancelled_between_two_files_leaves_no_run_folder_and_no_staging_folder()
    {
        using var temp = new TempFolder();
        using var cancel = new CancellationTokenSource();
        ExportFile[] files = [new("a.txt", _ => cancel.Cancel()), ExportFixture.Text("b.txt", "x")];

        await Should.ThrowAsync<OperationCanceledException>(() => ExportFixture.WriteAsync(temp.Root, files, cancel: cancel));

        Names(Path.Combine(temp.Root, "sim")).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_token_cancelled_before_the_start_writes_nothing()
    {
        using var temp = new TempFolder();
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => ExportFixture.WriteAsync(temp.Root, cancel: cancel));

        Directory.Exists(temp.Root).ShouldBeFalse();
    }

    [Fact]
    public async Task A_staging_folder_left_by_a_killed_process_is_reported_and_never_removed()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(Path.Combine(temp.Root, "sim", ".staging-old-run-0001"), Folder700);
        Directory.CreateDirectory(Path.Combine(temp.Root, "sim", ".staging-old-run-0002"), Folder700);

        var result = await ExportFixture.WriteAsync(temp.Root);

        result.Value.StaleStaging.ShouldBe([".staging-old-run-0001", ".staging-old-run-0002"]);
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([".staging-old-run-0001", ".staging-old-run-0002", ExportFixture.RunId]);
    }

    [Fact]
    public async Task Two_writers_racing_for_one_name_the_loser_compares_and_finds_the_same_export()
    {
        using var temp = new TempFolder();
        var first = true;
        var writer = new ExportFolderWriter((from, to) =>
        {
            if (first)
            {
                first = false;
                CopyFolder(from, to);
            }

            Directory.Move(from, to);
        });

        var result = await ExportFixture.WriteAsync(temp.Root, writer: writer);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Status.ShouldBe(ExportStatus.Unchanged);
        Names(Path.Combine(temp.Root, "sim")).ShouldBe([ExportFixture.RunId], "the loser removed its own staging folder");
    }

    [Fact]
    public async Task Two_writers_racing_for_one_name_with_different_content_the_loser_is_refused()
    {
        using var temp = new TempFolder();
        var other = Path.Combine(temp.Path, "other-root");
        await ExportFixture.WriteAsync(other, ExportFixture.Files("winner"));
        var first = true;
        var writer = new ExportFolderWriter((from, to) =>
        {
            if (first)
            {
                first = false;
                CopyFolder(Path.Combine(other, "sim", ExportFixture.RunId), to);
            }

            Directory.Move(from, to);
        });

        var result = await ExportFixture.WriteAsync(temp.Root, ExportFixture.Files("loser"), writer: writer);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(ExportErrors.ExistsDifferentCode);
        File.ReadAllText(Path.Combine(Run(temp.Root), "run.json")).ShouldContain("winner");
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to, Folder700);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }
    }
}
