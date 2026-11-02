using System.Diagnostics;

namespace XDrishti.Infrastructure.Tests.Filings;

/// <summary>P15-07: <c>scripts/xd-eval.sh</c> runs every verb path under <c>set -u</c> on the system bash (3.2 on the owner's Mac) without an unbound variable, with a stub <c>podman</c> that only records its arguments.</summary>
public sealed class XdEvalScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "xd-eval-script-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static string RealScript
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "scripts", "xd-eval.sh");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException("scripts/xd-eval.sh");
        }
    }

    private async Task<(int Exit, string Err, string Calls)> RunAsync(bool withSets, params string[] args)
    {
        Directory.CreateDirectory(Path.Combine(_root, "scripts"));
        Directory.CreateDirectory(Path.Combine(_root, "home", ".local", "bin"));
        File.Copy(RealScript, Path.Combine(_root, "scripts", "xd-eval.sh"), true);
        var log = Path.Combine(_root, "calls.log");
        var stub = Path.Combine(_root, "home", ".local", "bin", "podman");
        await File.WriteAllTextAsync(stub, "#!/bin/sh\necho \"$@\" >> \"$PODMAN_LOG\"\n[ \"$1\" = inspect ] && echo healthy\nexit 0\n", Ct);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(stub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (withSets)
        {
            Directory.CreateDirectory(Path.Combine(_root, "data", "eval", "sets"));
        }

        var info = new ProcessStartInfo("/bin/bash") { RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add(Path.Combine(_root, "scripts", "xd-eval.sh"));
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["HOME"] = Path.Combine(_root, "home");
        info.Environment["PODMAN_LOG"] = log;
        using var process = Process.Start(info)!;
        var error = process.StandardError.ReadToEndAsync(Ct);
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        await process.WaitForExitAsync(Ct);
        _ = await output;
        return (process.ExitCode, await error, File.Exists(log) ? await File.ReadAllTextAsync(log, Ct) : string.Empty);
    }

    [Theory]
    [InlineData(false, "show", "00000000-0000-0000-0000-000000000001")]
    [InlineData(true, "show", "00000000-0000-0000-0000-000000000001")]
    [InlineData(false, "compare", "00000000-0000-0000-0000-000000000001")]
    [InlineData(false, "accept", "00000000-0000-0000-0000-000000000001")]
    [InlineData(false, "run", "--set", "events")]
    [InlineData(false, "announcements", "show")]
    [InlineData(false, "announcements", "sample", "--from", "2024-01-01", "--to", "2024-12-31")]
    [InlineData(true, "announcements", "show")]
    public async Task Every_verb_path_runs_without_an_unbound_variable(bool withSets, params string[] args)
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            Assert.Skip("needs bash");
            return;
        }

        var (exit, err, calls) = await RunAsync(withSets, args);

        err.ShouldNotContain("unbound variable");
        exit.ShouldBe(0, err);
        calls.ShouldContain("xd-eval");
    }

    [Fact]
    public async Task Import_and_grade_with_a_file_run_without_an_unbound_variable_and_mount_the_file()
    {
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash"))
        {
            Assert.Skip("needs bash");
            return;
        }

        var file = Path.Combine(_root, "labels.csv");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(file, "id\n", Ct);

        var import = await RunAsync(false, "announcements", "import", file);
        var grade = await RunAsync(false, "grade", "--answers", file, "--target", "hermes");

        import.Err.ShouldNotContain("unbound variable");
        grade.Err.ShouldNotContain("unbound variable");
        (import.Exit, grade.Exit).ShouldBe((0, 0));
        import.Calls.ShouldContain(":/label-file.csv:ro");
    }
}
