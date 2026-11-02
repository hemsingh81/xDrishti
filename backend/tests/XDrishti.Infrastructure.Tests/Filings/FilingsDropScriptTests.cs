using System.Diagnostics;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Filings;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>Spec P15-01 criterion 9: <c>scripts/filings-drop.sh</c> files a local file with its sidecar, refuses what the adapter would refuse, and has no network tool in it.</summary>
public sealed class FilingsDropScriptTests
{
    private const string Good = "--symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast 2026-07-24T16:12:00+05:30";

    private static string Script
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "scripts", "filings-drop.sh");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("scripts/filings-drop.sh");
        }
    }

    private static async Task<(int Exit, string Out, string Err)> RunAsync(string inbox, string arguments, CancellationToken cancellationToken, int? maxMb = null)
    {
        var info = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Script);
        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["FILINGS_INBOX"] = inbox;
        if (maxMb is { } mb)
        {
            info.Environment["FILINGS_MAX_FILE_MB"] = mb.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output, await error);
    }

    private static bool NoBash => OperatingSystem.IsWindows();

    [Fact]
    public async Task It_files_a_local_file_with_a_sidecar_the_adapter_accepts_and_sets_the_modes()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("needs bash and POSIX modes");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var source = Path.Combine(fixture.Root, "res.XML");
        await File.WriteAllTextAsync(source, "<r/>", TestContext.Current.CancellationToken);

        var (exit, output, _) = await RunAsync(inbox, Good + " " + source, TestContext.Current.CancellationToken);

        exit.ShouldBe(0, output);
        var paths = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        paths.Length.ShouldBe(2);
        Path.GetFileName(paths[0]).ShouldMatch("^RELIANCE_results_2026-06-30_[0-9a-f]{8}\\.xml$");
        File.GetUnixFileMode(inbox).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.GetUnixFileMode(paths[0]).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.GetUnixFileMode(paths[1]).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var accepted = (await Listing(inbox)).Accepted.ShouldHaveSingleItem();
        accepted.BroadcastTs.ShouldBe(new DateTimeOffset(2026, 7, 24, 10, 42, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task The_same_content_again_is_a_no_op_and_a_different_content_under_the_same_name_is_refused()
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var source = Path.Combine(fixture.Root, "res.xml");
        await File.WriteAllTextAsync(source, "<r/>", TestContext.Current.CancellationToken);
        (await RunAsync(inbox, Good + " " + source, TestContext.Current.CancellationToken)).Exit.ShouldBe(0);

        (await RunAsync(inbox, Good + " " + source, TestContext.Current.CancellationToken)).Exit.ShouldBe(0);
        (await RunAsync(inbox, Good.Replace("16:12", "16:13", StringComparison.Ordinal) + " " + source, TestContext.Current.CancellationToken)).Exit.ShouldBe(1);

        Directory.GetFiles(inbox).Length.ShouldBe(2);
    }

    [Fact]
    public async Task Without_a_broadcast_time_the_sidecar_says_null_and_the_filing_is_unavailable()
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var source = Path.Combine(fixture.Root, "a.pdf");
        await File.WriteAllTextAsync(source, "pdf", TestContext.Current.CancellationToken);

        var (exit, output, error) = await RunAsync(inbox, "--symbol TCS --kind announcement " + source, TestContext.Current.CancellationToken);

        exit.ShouldBe(0);
        error.ShouldContain("unavailable for backtests");
        (await File.ReadAllTextAsync(output.Split('\n')[1], TestContext.Current.CancellationToken)).ShouldContain("\"broadcast_ts\": null");
        (await Listing(inbox)).Accepted.ShouldHaveSingleItem().Broadcast.ShouldBe(XDrishti.Domain.Filings.BroadcastState.Unavailable);
    }

    [Fact]
    public async Task A_csv_of_announcements_may_name_all_companies_while_a_text_file_of_announcements_may_not()
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var csv = Path.Combine(fixture.Root, "ann.csv");
        var txt = Path.Combine(fixture.Root, "ann.txt");
        await File.WriteAllTextAsync(csv, "symbol,broadcast_ts,category,subject,text\n", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(txt, "notice", TestContext.Current.CancellationToken);

        var ok = await RunAsync(inbox, "--symbol * --kind announcement " + csv, TestContext.Current.CancellationToken);
        var refused = await RunAsync(inbox, "--symbol * --kind announcement " + txt, TestContext.Current.CancellationToken);

        ok.Exit.ShouldBe(0, ok.Err);
        Path.GetFileName(ok.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]).ShouldMatch("^ALL_announcement_.*\\.csv$");
        refused.Exit.ShouldNotBe(0);
        refused.Err.ShouldContain("only for a csv");
    }

    [Theory]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast 2026-07-24T16:12:00 FILE.xml", "with +05:30")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast 2026-07-24 FILE.xml", "with +05:30")]
    [InlineData("--symbol RELIANCE --kind news --period-end 2026-06-30 FILE.xml", "unknown kind")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 FILE.exe", "extension")]
    [InlineData("--symbol RELIANCE --kind results FILE.xml", "--period-end is required")]
    [InlineData("--kind results --period-end 2026-06-30 FILE.xml", "missing --symbol")]
    [InlineData("--symbol RELIANCE --period-end 2026-06-30 FILE.xml", "missing --kind")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30", "missing the file")]
    [InlineData("--symbol * --kind results --period-end 2026-06-30 FILE.xml", "only for a csv")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-13-45 FILE.xml", "real date")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-02-30 FILE.xml", "real date")]
    [InlineData("--symbol RELIANCE --kind results --period-end 1999-12-31 FILE.xml", "real date")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast 2026-07-24T25:12:00+05:30 FILE.xml", "not a real date and time")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast 2026-02-31T10:00:00+05:30 FILE.xml", "not a real date and time")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 --broadcast 2026-07-24T10:00:00+25:00 FILE.xml", "offset beyond")]
    [InlineData("--symbol RELIANCE --kind results --period-end 2026-06-30 --bogus FILE.xml", "unknown option")]
    public async Task It_refuses_what_the_adapter_would_refuse(string arguments, string message)
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var extension = arguments.Contains("FILE.exe", StringComparison.Ordinal) ? "exe" : "xml";
        var source = Path.Combine(fixture.Root, "FILE." + extension);
        await File.WriteAllTextAsync(source, "<r/>", TestContext.Current.CancellationToken);

        var (exit, _, error) = await RunAsync(inbox, arguments.Replace("FILE." + extension, source, StringComparison.Ordinal), TestContext.Current.CancellationToken);

        exit.ShouldBe(2);
        error.ShouldContain(message);
        Directory.Exists(inbox).ShouldBeFalse();
    }

    [Fact]
    public async Task A_file_above_the_size_limit_and_an_empty_file_are_refused()
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var big = Path.Combine(fixture.Root, "big.xml");
        await File.WriteAllBytesAsync(big, new byte[2 * 1024 * 1024], TestContext.Current.CancellationToken);
        var empty = Path.Combine(fixture.Root, "empty.xml");
        await File.WriteAllBytesAsync(empty, [], TestContext.Current.CancellationToken);

        (await RunAsync(inbox, Good + " " + big, TestContext.Current.CancellationToken, maxMb: 1)).Err.ShouldContain("larger than 1 MB");
        (await RunAsync(inbox, Good + " " + empty, TestContext.Current.CancellationToken)).Err.ShouldContain("empty");
    }

    [Fact]
    public async Task A_bad_size_setting_is_refused_before_any_arithmetic_and_help_prints_only_the_usage()
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var source = Path.Combine(fixture.Root, "a.xml");
        await File.WriteAllTextAsync(source, "<r/>", TestContext.Current.CancellationToken);
        var marker = Path.Combine(fixture.Root, "injected");

        var info = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { Script, "--symbol", "TCS", "--kind", "announcement", source })
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["FILINGS_INBOX"] = inbox;
        info.Environment["FILINGS_MAX_FILE_MB"] = "a[$(touch " + marker + ")]";
        using var process = Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        process.ExitCode.ShouldBe(2);
        error.ShouldContain("FILINGS_MAX_FILE_MB");
        File.Exists(marker).ShouldBeFalse();
        var help = await RunAsync(inbox, "-h", TestContext.Current.CancellationToken);
        help.Exit.ShouldBe(0);
        help.Out.ShouldStartWith("usage:");
        help.Out.ShouldNotContain("set -e");
    }

    [Fact]
    public async Task A_symlinked_target_in_the_inbox_is_refused_and_a_file_named_with_a_leading_dash_is_refused_as_a_name_not_read_as_an_option()
    {
        if (NoBash)
        {
            Assert.Skip("needs bash");
            return;
        }

        using var fixture = new InboxFixture();
        var inbox = Path.Combine(fixture.Root, "dropped");
        var source = Path.Combine(fixture.Root, "a.xml");
        await File.WriteAllTextAsync(source, "<r/>", TestContext.Current.CancellationToken);
        var first = await RunAsync(inbox, Good + " " + source, TestContext.Current.CancellationToken);
        var target = first.Out.Split('\n')[0];
        File.Delete(target);
        File.CreateSymbolicLink(target, Path.Combine(fixture.Root, "nowhere"));

        (await RunAsync(inbox, Good + " " + source, TestContext.Current.CancellationToken)).Exit.ShouldBe(1);
        File.Exists(Path.Combine(fixture.Root, "nowhere")).ShouldBeFalse();

        var dashed = Path.Combine(fixture.Root, "-dash.xml");
        await File.WriteAllTextAsync(dashed, "<d/>", TestContext.Current.CancellationToken);
        var info = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = fixture.Root };
        foreach (var a in new[] { Script, "--symbol", "TCS", "--kind", "announcement", "--", "-dash.xml" })
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["FILINGS_INBOX"] = Path.Combine(fixture.Root, "second");
        using var process = Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.ShouldBe(2);
        error.ShouldContain("-dash.xml' is refused");
        Directory.Exists(Path.Combine(fixture.Root, "second")).ShouldBeFalse();
    }

    [Fact]
    public void The_script_holds_no_network_or_interpreter_command()
    {
        var text = File.ReadAllText(Script);

        Regex.IsMatch(text, @"\b(curl|wget|nc|ssh|scp|python3?|ncat|telnet)\b").ShouldBeFalse();
    }

    private static async Task<FilingListing> Listing(string inbox)
    {
        var source = new XDrishti.Infrastructure.Filings.FolderDropFilingsSource(
            Microsoft.Extensions.Options.Options.Create(new XDrishti.Infrastructure.Filings.FilingsOptions { InboxPath = inbox }),
            new Microsoft.Extensions.Time.Testing.FakeTimeProvider(InboxFixture.Now),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<XDrishti.Infrastructure.Filings.FolderDropFilingsSource>.Instance);
        return await source.ListAsync(FilingQuery.All, TestContext.Current.CancellationToken);
    }
}
