using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XDrishti.Api.CostSheets;
using XDrishti.Api.IntegrationTests.Simulate;
using XDrishti.Api.Simulate;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Application.Research.CostSheets;
using XDrishti.Domain.Common;

namespace XDrishti.Api.IntegrationTests.CostSheets;

/// <summary>
/// The parts of P3-03f that need no database: the dispatcher (criterion 6), the host (6), the command lines of the three actions (7, 8, 10), the cost flags of <c>simulate</c> (F1) and the script (11). The host is built, never started.
/// </summary>
public sealed class CostSheetVerbTests
{
    private static readonly Dictionary<string, string?> NoDatabase = new()
    {
        ["Database:ConnectionString"] = "Host=127.0.0.1;Port=1;Database=none;Username=nobody;Timeout=1",
        ["BrokerVault:Key"] = string.Empty,
    };

    public static TheoryData<string[], string> Dispatch => new()
    {
        { ["cost-sheet", "load", "x.json"], "CostSheet" },
        { ["cost-sheet", "load", "migrate"], "CostSheet" },
        { ["cost-sheet", "load", "owner-reset"], "CostSheet" },
        { ["cost-sheet", "--healthcheck"], "CostSheet" },
        { ["cost-sheet"], "CostSheet" },
        { ["x", "cost-sheet"], "Serve" },
        { ["Cost-Sheet"], "Serve" },
        { ["simulate", "--basket", "cost-sheet"], "Simulate" },
        { ["cost-sheet", "simulate"], "CostSheet" },
        { ["migrate"], "Migrate" },
        { ["owner-reset"], "OwnerReset" },
        { ["--healthcheck"], "HealthCheck" },
    };

    [Theory]
    [MemberData(nameof(Dispatch))]
    public void Only_the_first_argument_selects_the_cost_sheet_verb_and_it_never_selects_another_step(string[] args, string expected)
    {
        VerbDispatch.Of(args).ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_host_resolves_the_three_handlers_and_the_store_needs_no_vault_key_and_starts_nothing()
    {
        using var host = SimulateHost.Create(NoDatabase, serviceName: "xd-cost-sheet");
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<ICommandHandler<LoadCostSheetCommand, LoadedCostSheet>>().ShouldNotBeNull();
        services.GetRequiredService<IQueryHandler<ShowCostSheetQuery, Result<ShownCostSheet>>>().ShouldNotBeNull();
        services.GetRequiredService<IQueryHandler<CheckCostSheetQuery, Result<CostCheckReport>>>().ShouldNotBeNull();
        services.GetRequiredService<ICostSheetStore>().ShouldNotBeNull();
        Should.Throw<InvalidOperationException>(() => host.Services.GetRequiredService<ICostSheetStore>(), "the provider validates scopes");
        host.Services.GetRequiredService<IConfiguration>()["BrokerVault:Key"].ShouldBeNullOrEmpty();
        host.Services.GetServices<IHostedService>().Select(h => h.GetType().FullName!).ShouldNotContain(n => n.StartsWith("XDrishti.", StringComparison.Ordinal));
        host.Services.GetRequiredService<IConfiguration>()["Serilog:WriteTo:0:Args:standardErrorFromLevel"].ShouldBe("Verbose");
    }

    [Fact]
    public void A_service_that_cannot_be_built_fails_at_build_and_the_arguments_of_the_process_never_become_configuration()
    {
        Should.Throw<Exception>(() => SimulateHost.Create(NoDatabase, services => services.AddScoped<SimulateHostTests.MissingDependencyConsumer>(), "xd-cost-sheet"));
        using var host = SimulateHost.Create(NoDatabase, serviceName: "xd-cost-sheet");

        host.Services.GetRequiredService<IConfiguration>()["replace"].ShouldBeNull();
        host.Services.GetRequiredService<IConfiguration>()["urls"].ShouldBeNull();
    }

    public static TheoryData<string> ValidLines => new()
    {
        "load sheet.json",
        "load sheet.json --replace",
        "load --replace sheet.json",
        "show dhan-2026-10",
        "check dhan-2026-10 a.csv",
        "check dhan-2026-10 a.csv b.csv c.csv",
    };

    [Theory]
    [MemberData(nameof(ValidLines))]
    public void A_valid_command_line_gives_the_options(string line)
    {
        var parsed = CostSheetOptions.Parse(line.Split(' '));

        parsed.Errors.ShouldBeEmpty();
        parsed.Options.ShouldNotBeNull();
    }

    public static TheoryData<string> InvalidLines => new()
    {
        string.Empty,
        "nope",
        "load",
        "load a.json b.json",
        "load a.json --replace --replace",
        "load a.json --bogus",
        "load a.json --replace=yes",
        "show",
        "show a b",
        "show a --replace",
        "check a",
        "check",
        "check a b --replace",
    };

    [Theory]
    [MemberData(nameof(InvalidLines))]
    public void An_invalid_command_line_is_a_usage_error(string line)
    {
        var parsed = CostSheetOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_options_carry_the_action_the_id_the_files_and_the_replace_switch()
    {
        var load = CostSheetOptions.Parse(["load", "--replace", "a.json"]).Options.ShouldNotBeNull();
        var check = CostSheetOptions.Parse(["check", "dhan-x", "a.csv", "b.csv"]).Options.ShouldNotBeNull();

        (load.Action, load.Replace, load.SheetId).ShouldBe((CostSheetAction.Load, true, null));
        load.Files.ShouldBe(["a.json"]);
        (check.Action, check.SheetId, check.Replace).ShouldBe((CostSheetAction.Check, "dhan-x", false));
        check.Files.ShouldBe(["a.csv", "b.csv"]);
    }

    // the cost flags of simulate (F1)

    private const string Base = "--strategy ORB --symbols RELIANCE --from 2026-03-02 --to 2026-03-04";

    private static SimulateParse Parse(string line) => SimulateOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), ["ORB"]);

    [Fact]
    public void Gross_is_the_gross_choice_and_costs_is_a_sheet_choice_with_the_defaults_of_the_spec()
    {
        var gross = Parse(Base + " --gross").Options.ShouldNotBeNull();
        var sheet = Parse(Base + " --costs fixture-a").Options.ShouldNotBeNull();
        var named = Parse(Base + " --costs=fixture-a --capital 500000 --profile conservative --max-cost-r 0.25").Options.ShouldNotBeNull();

        gross.Costs.ShouldBeSameAs(CostChoice.Gross);
        sheet.Costs.ShouldBe(CostChoice.Sheet("fixture-a"));
        named.Costs.ShouldBe(CostChoice.Sheet("fixture-a", 500_000m, "conservative", 0.25m));
    }

    public static TheoryData<string, string> BadCostFlags => new()
    {
        { Base, "--gross" },
        { Base + " --gross --costs fixture-a", "not both" },
        { Base + " --gross --capital 5", "--capital" },
        { Base + " --gross --profile standard", "--profile" },
        { Base + " --gross --max-cost-r 0.2", "--max-cost-r" },
        { Base + " --capital 5", "--gross" },
        { Base + " --costs Fixture-A", "--costs" },
        { Base + " --costs a/b", "--costs" },
        { Base + " --costs fixture-a --capital 0", "--capital" },
        { Base + " --costs fixture-a --capital -5", "--capital" },
        { Base + " --costs fixture-a --capital abc", "--capital" },
        { Base + " --costs fixture-a --capital 10000000001", "--capital" },
        { Base + " --costs fixture-a --profile aggressive", "--profile" },
        { Base + " --costs fixture-a --max-cost-r 0", "--max-cost-r" },
        { Base + " --costs fixture-a --max-cost-r 101", "--max-cost-r" },
        { Base + " --costs fixture-a --costs fixture-b", "--costs" },
        { Base + " --gross --gross", "--gross" },
        { Base + " --gross=yes", "--gross" },
    };

    [Theory]
    [MemberData(nameof(BadCostFlags))]
    public void A_missing_conflicting_or_orphaned_cost_flag_is_a_usage_error_that_names_it(string line, string flag)
    {
        var parsed = Parse(line);

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldContain(e => e.Contains(flag, StringComparison.Ordinal), string.Join(" | ", parsed.Errors));
    }

    [Fact]
    public void The_usage_text_names_the_cost_choice()
    {
        SimulateOptions.Usage.ShouldContain("--gross");
        SimulateOptions.Usage.ShouldContain("--costs <sheet-id>");
        CostSheetOptions.Usage.ShouldContain("cost-sheet check <id> <report.csv>");
    }

    // criterion 11: the script

    private static string Script()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "scripts", "cost-sheet.sh")))
        {
            folder = folder.Parent;
        }

        return File.ReadAllText(Path.Combine(folder.ShouldNotBeNull().FullName, "scripts", "cost-sheet.sh"));
    }

    [Fact]
    public void The_script_never_traces_never_echoes_the_password_and_reads_no_vault_key_or_owner_secret()
    {
        var script = Script();

        script.ShouldNotContain("set -x");
        script.ShouldNotContain("xtrace");
        script.Split('\n').Where(l => l.TrimStart().StartsWith("echo", StringComparison.Ordinal)).ShouldAllBe(l => !l.Contains("$password", StringComparison.Ordinal) && !l.Contains("Database__Password", StringComparison.Ordinal));
        script.ShouldNotContain("BrokerVault");
        script.ShouldNotContain("xdrishti/broker");
        script.ShouldNotContain("xdrishti/owner");
        script.ShouldContain("xdrishti/db/app");
        script.ShouldContain("set -euo pipefail");
        script.ShouldContain("DOTNET_ENVIRONMENT=Development");
    }

    [Fact]
    public void The_script_makes_a_file_argument_absolute_before_it_changes_directory_and_returns_the_verbs_exit_code()
    {
        var script = Script();

        var resolve = script.IndexOf("[[ -f \"$arg\" ]]", StringComparison.Ordinal);
        var change = script.IndexOf("cd \"$ROOT/backend/src/XDrishti.Api\"", StringComparison.Ordinal);
        resolve.ShouldBeGreaterThan(0);
        change.ShouldBeGreaterThan(resolve, "the relative path is resolved while the caller's directory is still the current one");
        script.ShouldContain("exec dotnet run -c Release --no-launch-profile -- cost-sheet");
    }

    [Fact]
    public async Task The_script_resolves_a_relative_file_from_another_directory_before_it_changes_directory()
    {
        // run the argument loop of the script on its own, from a directory that is not the repository's: the printed path must be absolute and exist
        var script = Script();
        var start = script.IndexOf("args=()", StringComparison.Ordinal);
        var end = script.IndexOf("cd \"$ROOT", StringComparison.Ordinal);
        var loop = script[start..end];
        var folder = Path.Combine(Path.GetTempPath(), "xd-script-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(folder, "sheet.json"), "{}", TestContext.Current.CancellationToken);
            var info = new ProcessStartInfo("/bin/bash") { WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(loop + "\nprintf '%s\\n' \"${args[@]}\"");
            info.ArgumentList.Add("bash");
            info.ArgumentList.Add("load");
            info.ArgumentList.Add("sheet.json");
            info.ArgumentList.Add("--replace");
            using var process = Process.Start(info)!;
            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);

            process.ExitCode.ShouldBe(0);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.Length.ShouldBe(3);
            lines[0].ShouldBe("load");
            Path.IsPathRooted(lines[1]).ShouldBeTrue();
            File.Exists(lines[1]).ShouldBeTrue();
            Path.GetFileName(lines[1]).ShouldBe("sheet.json");
            lines[2].ShouldBe("--replace");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // the bounded read

    [Fact]
    public async Task A_special_file_a_directory_an_empty_file_and_a_missing_file_are_refused_without_being_read()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "xd-bounded-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var empty = Path.Combine(folder, "empty.json");
            await File.WriteAllBytesAsync(empty, [], ct);
            (await CostSheetCommand.ReadBoundedAsync(empty, 1024, "the file", ct)).Problem.ShouldNotBeNull();
            (await CostSheetCommand.ReadBoundedAsync(folder, 1024, "the file", ct)).Problem.ShouldNotBeNull();
            (await CostSheetCommand.ReadBoundedAsync(Path.Combine(folder, "nowhere"), 1024, "the file", ct)).Problem.ShouldNotBeNull();
            if (File.Exists("/dev/zero"))
            {
                (await CostSheetCommand.ReadBoundedAsync("/dev/zero", 1024, "the file", ct)).Problem.ShouldNotBeNull("a device reports length 0 and is never opened");
            }

            var fifo = Path.Combine(folder, "pipe");
            if (OperatingSystem.IsWindows() || !TryMakeFifo(fifo))
            {
                Assert.Skip("mkfifo is not available here");
            }

            (await CostSheetCommand.ReadBoundedAsync(fifo, 1024, "the file", ct)).Problem.ShouldNotBeNull("a FIFO is refused without being opened, so it cannot block");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_regular_file_and_a_symlink_to_one_are_read_up_to_the_cap_and_a_file_over_it_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), "xd-bounded-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "a.json");
            await File.WriteAllBytesAsync(file, new byte[100], ct);
            var link = Path.Combine(folder, "link.json");
            File.CreateSymbolicLink(link, file);

            (await CostSheetCommand.ReadBoundedAsync(file, 100, "the file", ct)).Bytes.ShouldNotBeNull().Length.ShouldBe(100);
            (await CostSheetCommand.ReadBoundedAsync(link, 100, "the file", ct)).Bytes.ShouldNotBeNull().Length.ShouldBe(100);
            (await CostSheetCommand.ReadBoundedAsync(file, 99, "the file", ct)).Problem.ShouldNotBeNull().ShouldContain("It was not read");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task More_than_ten_report_files_are_refused_before_any_is_read()
    {
        using var host = SimulateHost.Create(NoDatabase, serviceName: "xd-cost-sheet");
        var error = new StringWriter();
        var args = new List<string> { "check", "fixture-a" };
        args.AddRange(Enumerable.Range(0, CostSheetCommand.MaxReportFiles + 1).Select(i => "/nowhere/" + i + ".csv"));

        var exit = await CostSheetCommand.RunAsync(args, host.Services, new SimulateConsole(new StringWriter(), error), TestContext.Current.CancellationToken);

        exit.ShouldBe(1);
        error.ToString().ShouldContain("at most 10 report files");
    }

    private static bool TryMakeFifo(string path)
    {
        try
        {
            var info = new ProcessStartInfo("/usr/bin/mkfifo") { RedirectStandardError = true };
            info.ArgumentList.Add(path);
            using var process = Process.Start(info)!;
            process.WaitForExit();
            return process.ExitCode == 0 && File.Exists(path);
        }
        catch (Exception failure) when (failure is System.ComponentModel.Win32Exception or IOException)
        {
            return false;
        }
    }
}
