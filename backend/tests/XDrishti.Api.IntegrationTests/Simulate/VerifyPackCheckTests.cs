using System.Buffers;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using XDrishti.Api.Simulate;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Verification;
using XDrishti.Tests.Shared;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// Criterion 10 of P3-09 (Part B), the host side: <c>verify-pack --check</c> on a real pack folder written by the real writer from the standard invented run: the files it derives and refuses, the manifest check that exempts the log alone, the caps, the
/// exit codes. Temp directories only: no database, no container.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class VerifyPackCheckTests
{
    private const string Seed = "p3-check-1";

    private sealed record Pack(string Folder, string Log, PackPlan Plan);

    private static async Task<Pack> WriteAsync(TempFolder temp)
    {
        var run = FabricatedRun.Standard();
        var pack = new PackRun(run, RunLabels.Of(run, FillRules.Default));
        var plan = PackPlanner.Plan([run], Seed, 5);
        var packKey = PackFiles.PackKey(Seed, 5, null, [run]);
        var packId = PackFiles.PackId(Seed, packKey);
        var extra = PackFiles.ExtraLines(Seed, 5, [run], plan);
        List<ExportFile> files =
        [
            new(PackFiles.PackJsonName, sink => PackFiles.WritePackJson(sink, Seed, 5, packId, packKey, null, [pack], plan)),
            new(PackFiles.ReadmeName, sink => sink.Write(Encoding.UTF8.GetBytes(PackFiles.ReadmeText(packId, Seed, [pack])))),
            new("index.html", sink => PackIndexHtml.Write(sink, packId, Seed, [pack], plan)),
            new(PackFiles.EngineValuesName, sink => PackFiles.WriteEngineValues(sink, plan)),
            new(PackFiles.CheckLogName, sink => PackFiles.WriteCheckLog(sink, plan)),
        ];

        var written = await new ExportFolderWriter().WriteAsync(
            temp.Root,
            packId,
            files,
            entries => (ExportManifest.Build(PackFiles.Format, packKey, null, entries, extra), ExportKey.Of(PackFiles.Format, packKey, null, entries, extra)),
            false,
            TestContext.Current.CancellationToken,
            PackFiles.Folder);
        written.IsSuccess.ShouldBeTrue(written.IsFailure ? written.Error.Message : string.Empty);
        return new Pack(written.Value.Folder, Path.Combine(written.Value.Folder, PackFiles.CheckLogName), plan);
    }

    private static async Task<(int Exit, string Out, string Error)> CheckAsync(string path)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await VerifyPackCheck.RunAsync(path, new SimulateConsole(output, error), NullLogger.Instance, TestContext.Current.CancellationToken);

        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task The_untouched_log_says_the_pack_is_incomplete_with_every_row_unsigned_and_exits_1_and_writes_nothing()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        var before = ExportFixture.Tree(pack.Folder);
        var hash = ExportFixture.Sha256(pack.Log);

        var result = await CheckAsync(pack.Log);

        result.Exit.ShouldBe(1, result.Error);
        result.Out.Split('\n').Where(l => l.StartsWith("row ", StringComparison.Ordinal)).ShouldAllBe(l => l.EndsWith(": unsigned", StringComparison.Ordinal));
        result.Out.Split('\n').Count(l => l.StartsWith("row ", StringComparison.Ordinal)).ShouldBe(pack.Plan.Rows.Count + pack.Plan.Spots.Count);
        result.Out.ShouldContain("pack: incomplete (");
        result.Out.ShouldNotContain("pass", Case.Insensitive);
        ExportFixture.Tree(pack.Folder).ShouldBe(before);
        ExportFixture.Sha256(pack.Log).ShouldBe(hash);
    }

    [Fact]
    public async Task A_changed_log_alone_is_not_a_manifest_failure()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        File.AppendAllText(pack.Log, string.Empty);
        File.WriteAllText(pack.Log, File.ReadAllText(pack.Log).Replace(",trade,", ",trade,card", StringComparison.Ordinal));

        var result = await CheckAsync(pack.Log);

        result.Error.ShouldNotContain("manifest");
        result.Exit.ShouldNotBe(2, result.Error + result.Out);
    }

    [Theory]
    [InlineData("engine-values.csv")]
    [InlineData("index.html")]
    [InlineData("pack.json")]
    [InlineData("README.txt")]
    public async Task A_changed_engine_values_index_pack_or_readme_is_exit_2_naming_the_file(string name)
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        File.AppendAllText(Path.Combine(pack.Folder, name), " ");

        var result = await CheckAsync(pack.Log);

        result.Exit.ShouldBe(2);
        result.Error.ShouldContain(name + " differs from manifest.json");
        result.Out.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_missing_listed_file_and_a_missing_manifest_are_exit_2()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        File.Delete(Path.Combine(pack.Folder, "index.html"));
        var missing = await CheckAsync(pack.Log);
        File.Delete(Path.Combine(pack.Folder, "manifest.json"));
        var none = await CheckAsync(pack.Log);

        missing.Exit.ShouldBe(2);
        missing.Error.ShouldContain("index.html differs from manifest.json");
        none.Exit.ShouldBe(2);
        none.Error.ShouldContain("no usable manifest.json");
    }

    [Fact]
    public async Task A_symbolic_link_in_place_of_the_log_or_the_engine_values_or_the_folder_is_refused()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        var real = Path.Combine(temp.Path, "real-log.csv");
        File.Move(pack.Log, real);
        File.CreateSymbolicLink(pack.Log, real);

        var linkedLog = await CheckAsync(pack.Log);
        File.Delete(pack.Log);
        File.Move(real, pack.Log);
        var engine = Path.Combine(pack.Folder, "engine-values.csv");
        var engineReal = Path.Combine(temp.Path, "real-engine.csv");
        File.Move(engine, engineReal);
        File.CreateSymbolicLink(engine, engineReal);
        var linkedEngine = await CheckAsync(pack.Log);
        File.Delete(engine);
        File.Move(engineReal, engine);
        var folderLink = Path.Combine(temp.Path, "linked-folder");
        Directory.CreateSymbolicLink(folderLink, pack.Folder);
        var linkedFolder = await CheckAsync(Path.Combine(folderLink, "check-log.csv"));

        linkedLog.Exit.ShouldBe(2);
        linkedLog.Error.ShouldContain("check-log.csv is a symbolic link");
        linkedEngine.Exit.ShouldBe(2);
        linkedEngine.Error.ShouldContain("engine-values.csv is a symbolic link");
        linkedFolder.Exit.ShouldBe(2);
        linkedFolder.Error.ShouldContain("symbolic link");
    }

    [Fact]
    public async Task A_log_over_5_megabytes_and_a_wrong_file_name_and_a_missing_file_are_exit_2()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        await using (var big = new FileStream(pack.Log, FileMode.Open, FileAccess.Write))
        {
            big.SetLength((5L * 1024 * 1024) + 1);
        }

        var tooBig = await CheckAsync(pack.Log);
        var wrongName = await CheckAsync(Path.Combine(pack.Folder, "notes.csv"));
        var missing = await CheckAsync(Path.Combine(temp.Path, "nowhere", "check-log.csv"));
        var empty = await CheckAsync(" ");

        tooBig.Exit.ShouldBe(2);
        tooBig.Error.ShouldContain("check-log.csv is larger than 5 MB");
        wrongName.Exit.ShouldBe(2);
        missing.Exit.ShouldBe(2);
        empty.Exit.ShouldBe(2);
    }

    [Fact]
    public async Task A_log_that_is_not_utf8_text_or_not_csv_or_names_a_trade_that_is_not_in_the_pack_is_exit_2_with_a_message()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        var original = File.ReadAllBytes(pack.Log);

        File.WriteAllBytes(pack.Log, [0xFF, 0xFE, 0x00, 0xC3, 0x28]);
        var binary = await CheckAsync(pack.Log);
        File.WriteAllText(pack.Log, "seq,trade_key\n1,\"never closed\n");
        var notCsv = await CheckAsync(pack.Log);
        File.WriteAllText(pack.Log, Encoding.UTF8.GetString(original).Replace("ORB|NSE:AAA|Buy|2026-03-02", "ORB|NSE:ZZZ|Buy|2026-03-02", StringComparison.Ordinal));
        var unknown = await CheckAsync(pack.Log);

        binary.Exit.ShouldBe(2);
        binary.Error.ShouldContain("cannot be read as UTF-8 text");
        notCsv.Exit.ShouldBe(2);
        notCsv.Error.ShouldContain("the log is not CSV");
        unknown.Exit.ShouldBe(2);
        unknown.Error.ShouldContain("the log has a trade_key that is not in the pack: ORB|NSE:ZZZ|Buy|2026-03-02");
    }

    [Fact]
    public async Task A_log_with_windows_line_ends_and_a_byte_order_mark_is_read()
    {
        using var temp = new TempFolder();
        var pack = await WriteAsync(temp);
        File.WriteAllText(pack.Log, "﻿" + File.ReadAllText(pack.Log).Replace("\n", "\r\n", StringComparison.Ordinal));

        var result = await CheckAsync(pack.Log);

        result.Exit.ShouldBe(1, result.Error);
        result.Out.ShouldContain("pack: incomplete (");
    }
}
