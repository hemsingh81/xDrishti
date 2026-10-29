using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P12-01, ADR 0155: the one place that says where an order action may live. <c>XDrishti.Execution</c> is the only order-capable project by architecture. In this slice it holds the skeleton and the pure order state machine and
/// no network code; these rules prove nothing else can reach it and that it has no way to reach a broker yet. The old tripwire (<see cref="NoOrderPlacementArchitectureTests"/>) is not edited; the project is exempt from it only by
/// being absent from its list. A tripwire, not a guarantee: the barriers are the allow-list of <c>xd-api</c> (it refuses every order write), the account gate, the role and the network.
/// </summary>
public sealed class ExecutionBoundaryArchitectureTests
{
    private static readonly Assembly Execution = typeof(XDrishti.Execution.Domain.ExecState).Assembly;

    private static readonly string[] Folders = ["Domain", "Application"];

    private static readonly string[] OrderWriteLiterals = ["/v2/orders", "/v2/super/orders", "/v2/killswitch", "/v2/pnlExit"];

    // Two files named in the spec, plus the three files of P11-01 that read the day's order book with a GET on /v2/orders (the allow-list entry is a GET and BrokerAllowListTests pins it).
    // Matched by path relative to backend/src (forward slashes), so the same file name in another folder is still scanned.
    private static readonly string[] ExcludedFromLiteralScan =
    [
        "XDrishti.Domain/Planning/PlanPhaseCheck.cs", "XDrishti.Infrastructure/Brokers/BrokerEndpoints.cs", "XDrishti.Infrastructure/Brokers/DhanOrderBookClient.cs",
        "XDrishti.Infrastructure/Brokers/DhanOrderBookMapper.cs", "XDrishti.Infrastructure/DependencyInjection.cs",
    ];

    private static string SrcRelative(string file) => Path.GetRelativePath(Path.Combine(Backend, "src"), file).Replace('\\', '/');

    private static bool IsExcluded(string relative) => ExcludedFromLiteralScan.Contains(relative) || relative == "XDrishti.Execution/Dhan/ExecutionEndpoints.cs";

    private static string Backend => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string ExecutionSource => Path.Combine(Backend, "src", "XDrishti.Execution");

    private static bool Exempt(string violation) =>
        violation.StartsWith("XDrishti.Execution.Dhan.", StringComparison.Ordinal)
        || violation == "XDrishti.Execution.Application.Ports.IBrokerOrders"
        || violation.StartsWith("XDrishti.Execution.Application.Ports.IBrokerOrders.", StringComparison.Ordinal);

    private static List<string> SourceFiles(string directory) =>
        [.. Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f))];

    [Fact]
    public void The_old_tripwire_file_does_not_name_the_execution_project()
    {
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "tests", "XDrishti.Architecture.Tests", "NoOrderPlacementArchitectureTests.cs"))
            .ShouldNotContain("XDrishti.Execution");
    }

    [Fact]
    public void Order_verbs_are_allowed_only_in_the_dhan_namespace_and_the_one_port()
    {
        var found = NoOrderPlacementArchitectureTests.Violations(Execution);

        found.Where(v => !Exempt(v)).ShouldBeEmpty();
        Execution.GetTypes().Length.ShouldBeGreaterThan(15, "the scan does read the execution assembly");
    }

    [Fact]
    public void A_planted_violation_outside_the_exempt_namespaces_is_caught()
    {
        var found = NoOrderPlacementArchitectureTests.Violations(typeof(ExecutionBoundaryArchitectureTests).Assembly);

        found.Where(v => !Exempt(v)).ShouldContain(f => f.EndsWith("Planted.PlaceOrder", StringComparison.Ordinal));
        Exempt("XDrishti.Execution.Dhan.Anything.PlaceOrder").ShouldBeTrue();
        Exempt("XDrishti.Execution.Domain.PlaceOrder").ShouldBeFalse();
    }

    [Fact]
    public void Only_the_two_test_projects_reference_the_execution_project()
    {
        var referrers = Directory.GetFiles(Backend, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f))
            .Where(f => RepositoryFiles.ProjectReferences(XDocument.Load(f)).Contains("XDrishti.Execution"))
            .Select(Path.GetFileNameWithoutExtension)
            .Order()
            .ToList();

        referrers.ShouldBe(["XDrishti.Architecture.Tests", "XDrishti.Execution.Tests"]);
    }

    // Every source project except Execution, read from the project files (so a new one such as Cli is covered).
    private static IEnumerable<string> OtherProjects() =>
        Directory.GetFiles(Path.Combine(Backend, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f))
            .Select(f => Path.GetFileNameWithoutExtension(f)!)
            .Where(n => n != "XDrishti.Execution");

    [Fact]
    public void No_shipped_assembly_loads_the_execution_assembly()
    {
        var loaded = 0;
        foreach (var name in OtherProjects())
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.Load(new AssemblyName(name));
            }
            catch (FileNotFoundException)
            {
                continue; // a project this test assembly does not load; the project-file rule above covers it
            }

            assembly.GetReferencedAssemblies().ShouldNotContain(r => r.Name == "XDrishti.Execution", name);
            loaded++;
        }

        loaded.ShouldBeGreaterThan(8);
    }

    [Fact]
    public void The_order_write_paths_appear_nowhere_in_source_except_the_planned_adapter_file()
    {
        var offenders = SourceFiles(Path.Combine(Backend, "src"))
            .Where(f => !IsExcluded(SrcRelative(f)))
            .Where(f => OrderWriteLiterals.Any(l => File.ReadAllText(f).Contains(l, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(Backend, f))
            .ToList();

        offenders.ShouldBeEmpty();
        Directory.Exists(Path.Combine(ExecutionSource, "Dhan")).ShouldBeFalse("the Dhan adapter is P12-02 and is not part of P12-01");
    }

    [Fact]
    public void An_excluded_file_name_in_the_wrong_folder_is_still_scanned()
    {
        IsExcluded("XDrishti.Infrastructure/DependencyInjection.cs").ShouldBeTrue();
        IsExcluded("XDrishti.Execution/DependencyInjection.cs").ShouldBeFalse();
        IsExcluded("XDrishti.Execution/Domain/BrokerEndpoints.cs").ShouldBeFalse();
        IsExcluded("XDrishti.Execution/Domain/ExecutionEndpoints.cs").ShouldBeFalse();
        File.Exists(Path.Combine(Backend, "src", "XDrishti.Domain", "Planning", "PlanPhaseCheck.cs")).ShouldBeTrue("the excluded paths exist");
    }

    [Fact]
    public void The_execution_project_has_no_http_surface_no_http_client_and_no_broker_library()
    {
        var references = Execution.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();

        references.ShouldNotContain(r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        references.ShouldNotContain(r => r.StartsWith("System.Net.Http", StringComparison.Ordinal));
        // P12-01b (ADR 0156): plain Npgsql is allowed for the order record (Persistence/), as the role xd_exec; EF Core, the shared Infrastructure and any HTTP library are not.
        references.ShouldNotContain(r => r == "Microsoft.Extensions.Http" || r.Contains("EntityFramework", StringComparison.Ordinal));
        references.ShouldNotContain(r => Regex.IsMatch(r, "dhan|kite|zerodha|upstox|fyers|smartapi|angelone|alpaca|ibapi|ccxt|5paisa|groww|quickfix", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
        references.ShouldNotContain("XDrishti.Infrastructure");
        references.ShouldNotContain("XDrishti.Api");

        var sources = SourceFiles(ExecutionSource).Select(File.ReadAllText).ToList();
        sources.ShouldNotContain(s => s.Contains("HttpClient", StringComparison.Ordinal) || s.Contains("AddHttpClient", StringComparison.Ordinal) || s.Contains("WebSocket", StringComparison.Ordinal));
        File.ReadAllText(Path.Combine(ExecutionSource, "XDrishti.Execution.csproj")).ShouldNotContain("Microsoft.NET.Sdk.Web");
    }

    [Fact]
    public void The_domain_and_application_folders_use_no_clock_random_file_network_or_database()
    {
        var banned = new Regex(@"DateTime(Offset)?\.(Now|UtcNow|Today)|\bnew Random\b|Random\.Shared|System\.IO|File\.|Directory\.|System\.Net|Npgsql|EntityFrameworkCore|Environment\.GetEnvironmentVariable|Thread\.Sleep",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

        var offenders = Folders
            .SelectMany(d => SourceFiles(Path.Combine(ExecutionSource, d)))
            .Where(f => banned.IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();
        banned.IsMatch("var t = DateTime.UtcNow;").ShouldBeTrue("the scan does see a clock use");
    }

    [Fact]
    public void The_execution_container_role_secret_and_network_appear_only_in_their_own_blocks()
    {
        var compose = string.Join('\n', File.ReadAllLines(RepositoryFiles.RepositoryFile("deploy", "compose.yaml")).Where(l => !l.TrimStart().StartsWith('#')));
        var blocks = Regex.Split(compose, @"\n(?=  [a-z][a-z0-9-]*:\n)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

        string[] Holders(string needle) => [.. blocks.Where(b => b.Contains(needle, StringComparison.Ordinal)).Select(b => b.Trim().Split(':')[0])];

        // the password file: set as the login by xd-migrate, read by xd-exec only; the role name only in xd-exec's own connection string; the network only between xd-db and xd-exec.
        Holders("source: db_exec_password").ShouldBe(["xd-migrate", "xd-exec"], ignoreOrder: true);
        Holders("Username=xd_exec").ShouldBe(["xd-exec"]);
        blocks.Where(b => Regex.IsMatch(b, @"networks: \[[^\]]*\bexec\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))).Select(b => b.Trim().Split(':')[0]).ShouldBe(["xd-db", "xd-exec"], ignoreOrder: true);
        Holders("XDrishti.Execution.dll").ShouldBe(["xd-exec"]);
    }

    [Fact]
    public void The_broker_allow_list_still_refuses_order_writes_and_the_slice_did_not_edit_it()
    {
        // The pinned behaviour is BrokerAllowListTests (unchanged). Here: the allow-list source names no order write and no execution type.
        var text = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "BrokerEndpoints.cs"));

        text.ShouldNotContain("XDrishti.Execution");
        text.ShouldNotContain("IBrokerOrders");
    }
}
