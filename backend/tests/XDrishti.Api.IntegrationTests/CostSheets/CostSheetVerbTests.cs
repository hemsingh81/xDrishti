using System.Diagnostics;
using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;
using XDrishti.Application.Research;

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
            (await BoundedFile.ReadAsync(empty, 1024, "the file", ct)).Problem.ShouldNotBeNull();
            (await BoundedFile.ReadAsync(folder, 1024, "the file", ct)).Problem.ShouldNotBeNull();
            (await BoundedFile.ReadAsync(Path.Combine(folder, "nowhere"), 1024, "the file", ct)).Problem.ShouldNotBeNull();
            if (File.Exists("/dev/zero"))
            {
                (await BoundedFile.ReadAsync("/dev/zero", 1024, "the file", ct)).Problem.ShouldNotBeNull("a device reports length 0 and is never opened");
            }

            var fifo = Path.Combine(folder, "pipe");
            if (OperatingSystem.IsWindows() || !TryMakeFifo(fifo))
            {
                Assert.Skip("mkfifo is not available here");
            }

            (await BoundedFile.ReadAsync(fifo, 1024, "the file", ct)).Problem.ShouldNotBeNull("a FIFO is refused without being opened, so it cannot block");
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

            (await BoundedFile.ReadAsync(file, 100, "the file", ct)).Bytes.ShouldNotBeNull().Length.ShouldBe(100);
            (await BoundedFile.ReadAsync(link, 100, "the file", ct)).Bytes.ShouldNotBeNull().Length.ShouldBe(100);
            (await BoundedFile.ReadAsync(file, 99, "the file", ct)).Problem.ShouldNotBeNull().ShouldContain("It was not read");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
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
