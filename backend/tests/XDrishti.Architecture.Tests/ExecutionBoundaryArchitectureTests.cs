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
    public void The_order_write_paths_appear_nowhere_in_source_except_the_one_adapter_file()
    {
        var offenders = SourceFiles(Path.Combine(Backend, "src"))
            .Where(f => !IsExcluded(SrcRelative(f)))
            .Where(f => OrderWriteLiterals.Any(l => File.ReadAllText(f).Contains(l, StringComparison.Ordinal)))
            .Select(f => Path.GetRelativePath(Backend, f))
            .ToList();

        offenders.ShouldBeEmpty();
        File.ReadAllText(Path.Combine(ExecutionSource, "Dhan", "ExecutionEndpoints.cs")).ShouldContain("/v2/orders", Case.Sensitive, "the one guard table holds the order paths");
    }

    // P12-02 (ADR 0157): the Dhan folder is the only place with HTTP, a host name and the access-token header. ADR 0172: the one live host (api.dhan.co) joins the sandbox host in the one guard file, with exactly two GET rows.
    [Fact]
    public void Http_a_host_name_and_the_token_header_exist_only_in_the_dhan_folder_and_only_the_sandbox_and_the_one_live_host_are_pinned()
    {
        var all = SourceFiles(ExecutionSource);
        var outside = all.Where(f => !SrcRelative(f).StartsWith("XDrishti.Execution/Dhan/", StringComparison.Ordinal)).ToList();

        outside.Where(f => Regex.IsMatch(File.ReadAllText(f), @"HttpClient|HttpMessageHandler|System\.Net\.Http|(https?|wss?)://|access-token|\.dhan\.co", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Select(Path.GetFileName).ShouldBeEmpty();

        var hostFiles = all.Where(f => File.ReadAllText(f).Contains("dhan.co", StringComparison.OrdinalIgnoreCase)).Select(f => SrcRelative(f)).ToList();
        hostFiles.ShouldBe(["XDrishti.Execution/Dhan/ExecutionEndpoints.cs"]);

        var text = File.ReadAllText(Path.Combine(ExecutionSource, "Dhan", "ExecutionEndpoints.cs"));
        text.ShouldContain("\"sandbox.dhan.co\"");
        Regex.Matches(text, @"[a-z0-9.-]+\.dhan\.co", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)).Select(m => m.Value).Distinct().ShouldBe(["sandbox.dhan.co", "api.dhan.co"]);
        Regex.Count(text, "api\\.dhan\\.co", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)).ShouldBe(1, "the live host is one constant");
        text.ShouldContain("public const string LiveHost = \"api.dhan.co\";");
        text.ShouldNotContain("api-order-update");
        text.ShouldNotContain("wss://");
        all.Where(f => File.ReadAllText(f).Contains("api.dhan.co", StringComparison.OrdinalIgnoreCase)).Select(f => SrcRelative(f)).ShouldBe(["XDrishti.Execution/Dhan/ExecutionEndpoints.cs"], "the live host string exists in the guard file only");

        // The live rows: exactly the two readiness GETs, no id (ADR 0172). Widening this table needs its own ADR.
        var liveBlock = Regex.Match(text, @"LiveTable =\s*\[(?<rows>[^\]]*)\];", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        liveBlock.Success.ShouldBeTrue();
        Regex.Matches(liveBlock.Groups["rows"].Value, @"\(DhanCall\.(?<c>\w+), HttpMethod\.(?<m>\w+), ""(?<p>[^""]+)""\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))
            .Select(m => $"{m.Groups["c"].Value} {m.Groups["m"].Value} {m.Groups["p"].Value}").ShouldBe(["ProfileGet Get /v2/profile", "StaticIpGet Get /v2/ip/getIP"]);
        Regex.Count(liveBlock.Groups["rows"].Value, @"\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBe(2, "two rows and nothing else");
        all.ShouldNotContain(f => Regex.IsMatch(File.ReadAllText(f), "AddHttpClient|WebSocket|ClientWebSocket|AllowAutoRedirect\\s*=\\s*true", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void The_dhan_folder_holds_exactly_the_reviewed_files_and_only_the_registration_composes_the_adapter_behind_sandbox()
    {
        Directory.GetFiles(Path.Combine(ExecutionSource, "Dhan"), "*.cs").Select(Path.GetFileName).Order().ToList().ShouldBe(
            ["DhanBrokerFacts.cs", "DhanCredentials.cs", "DhanErrorMap.cs", "DhanOrderClient.cs", "DhanReader.cs", "DhanReads.cs", "DhanSandboxRegistration.cs", "DhanWire.cs", "ExecutionEndpoints.cs", "GuardHandler.cs"]);

        // the adapter is composed in exactly one place, behind an explicit sandbox test, and the send protocol in none of the host files
        var callers = SourceFiles(ExecutionSource).Where(f => File.ReadAllText(f).Contains("AddDhanSandbox(", StringComparison.Ordinal)).Select(Path.GetFileName).Order().ToList();
        callers.ShouldBe(["DhanSandboxRegistration.cs", "ExecServices.cs"]);
        var services = File.ReadAllText(Path.Combine(ExecutionSource, "Host", "ExecServices.cs"));
        Regex.IsMatch(services, @"environment == ExecEnvironment\.Sandbox\)\s*\{\s*services\.AddDhanSandbox", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        services.ShouldNotContain("AddLegSender");
        // the readiness reads (ADR 0170) are composed only inside the sandbox registration, never by the host files
        SourceFiles(ExecutionSource).Where(f => File.ReadAllText(f).Contains("new DhanBrokerFacts(", StringComparison.Ordinal)).Select(Path.GetFileName).ShouldBe(["DhanSandboxRegistration.cs"]);
        SourceFiles(ExecutionSource).Where(f => File.ReadAllText(f).Contains("AddLegSender(", StringComparison.Ordinal)).Select(Path.GetFileName).ShouldBe(["DhanSandboxRegistration.cs"]);

        // the environment live is never a send target: the adapter and the sender name only the sandbox environment as acceptable
        File.ReadAllText(Path.Combine(ExecutionSource, "Dhan", "DhanOrderClient.cs")).ShouldContain("environment != ExecEnvironment.Sandbox");
        File.ReadAllText(Path.Combine(ExecutionSource, "Application", "LegSender.cs")).ShouldContain("environment != ExecEnvironment.Sandbox");
        // ADR 0172: the guard file names live in exactly three places (the Build branch to the two live rows, the host switch and the row check of IsAllowed); the reader carries no write.
        Regex.Count(File.ReadAllText(Path.Combine(ExecutionSource, "Dhan", "ExecutionEndpoints.cs")), @"ExecEnvironment\.Live\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBe(3);
        var reader = File.ReadAllText(Path.Combine(ExecutionSource, "Dhan", "DhanReader.cs"));
        reader.ShouldNotContain("HttpMethod.Post");
        reader.ShouldNotContain("Content =");
        SourceFiles(ExecutionSource).Where(f => File.ReadAllText(f).Contains("new DhanReader(", StringComparison.Ordinal)).Select(Path.GetFileName).Order().ShouldBe(["DhanBrokerFacts.cs", "DhanOrderClient.cs"]);
    }

    // ADR 0171 step 6: the live readiness reads and their token source are composed in one place, behind live AND the default-off readiness switch; the token source feeds only those reads (never the order client) and nothing adds a live host.
    [Fact]
    public void The_live_readiness_reads_are_composed_only_behind_live_and_the_switch_and_the_hand_over_token_reaches_nothing_else()
    {
        var sources = SourceFiles(ExecutionSource).ToList();

        sources.Where(f => File.ReadAllText(f).Contains("new LiveBrokerFacts(", StringComparison.Ordinal)).Select(f => SrcRelative(f)).ShouldBe(["XDrishti.Execution/Host/ExecServices.cs"]);
        var services = File.ReadAllText(Path.Combine(ExecutionSource, "Host", "ExecServices.cs"));
        Regex.IsMatch(services, @"== ExecEnvironment\.Live && ExecOptions\.IsReadinessLiveEnabled\(configuration\)\)\s*\{[^}]*new LiveBrokerFacts\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        sources.Where(f => File.ReadAllText(f).Contains("ILiveTokenSource", StringComparison.Ordinal)).Select(f => SrcRelative(f)).Order(StringComparer.Ordinal).ShouldBe(
            ["XDrishti.Execution/Application/Ports/ILiveTokenSource.cs", "XDrishti.Execution/Dhan/DhanBrokerFacts.cs", "XDrishti.Execution/Host/ExecServices.cs", "XDrishti.Execution/Persistence/PgExecTokenHandover.cs"]);
        File.ReadAllText(Path.Combine(ExecutionSource, "appsettings.json")).ShouldNotContain("ReadinessLiveEnabled\": true");

        // ADR 0172: the proxy setting is read in one place and used only by the live reads; every other client of the process keeps UseProxy = false.
        sources.Where(f => File.ReadAllText(f).Contains("\"HTTPS_PROXY\"", StringComparison.Ordinal)).Select(f => SrcRelative(f)).ShouldBe(["XDrishti.Execution/Host/ExecOptions.cs"]);
        sources.Where(f => File.ReadAllText(f).Contains("LiveProxy(configuration)", StringComparison.Ordinal)).Select(f => SrcRelative(f)).ShouldBe(["XDrishti.Execution/Host/ExecServices.cs"]);
        Regex.IsMatch(services, @"IsReadinessLiveEnabled\(configuration\)\)\s*\{[^}]*LiveProxy\(configuration\)[^}]*ProxySocket\(proxy\)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).ShouldBeTrue();
        sources.Where(f => Regex.IsMatch(File.ReadAllText(f), @"UseProxy\s*=\s*true", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))).Select(f => SrcRelative(f)).ShouldBe(["XDrishti.Execution/Dhan/DhanBrokerFacts.cs"]);
        File.ReadAllText(Path.Combine(ExecutionSource, "Dhan", "DhanSandboxRegistration.cs")).ShouldContain("UseProxy = false");
    }

    // P12-02b (ADR 0158): the raw order surface is never a service; the vault is never read; the send switch is a second default-off setting.
    [Fact]
    public void The_raw_order_port_is_never_registered_the_vault_is_never_named_and_the_send_switch_defaults_off()
    {
        var sources = SourceFiles(ExecutionSource).ToList();

        sources.Where(f => Regex.IsMatch(File.ReadAllText(f), @"Add(Singleton|Scoped|Transient)(<|\()[^;]*IBrokerOrders|TryAdd(Singleton|Scoped|Transient)(<|\()[^;]*IBrokerOrders", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Select(Path.GetFileName).ShouldBeEmpty();
        sources.Where(f => File.ReadAllText(f).Contains("broker_secrets", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).ShouldBeEmpty();
        File.ReadAllText(Path.Combine(ExecutionSource, "appsettings.json")).ShouldNotContain("SendEnabled\": true");

        var migration = string.Join('\n', File.ReadAllLines(Path.Combine(Backend, "src", "XDrishti.Infrastructure", "Persistence", "Migrations", "20261029170000_AddExecutionBrokerLog.cs")).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));
        Regex.Matches(migration, @"GRANT[^;]*broker_secrets", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)).ShouldBeEmpty();
        migration.ShouldContain("GRANT SELECT (account_id, status, expires_at) ON accounts.broker_connections TO xd_exec");
        Regex.Matches(migration, @"GRANT[^;]*(UPDATE|DELETE|TRUNCATE)[^;]*broker_calls", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)).ShouldBeEmpty();
    }

    [Fact]
    public void The_xd_api_allow_list_files_are_byte_for_byte_the_committed_ones()
    {
        // zero diff against HEAD for the data-side allow-list and the broker clients (needs git; skipped quietly when the checkout has none)
        var root = Path.GetDirectoryName(Backend)!;
        var files = new[] { "backend/src/XDrishti.Infrastructure/Brokers/BrokerEndpoints.cs", "backend/tests/XDrishti.Infrastructure.Tests/BrokerAllowListTests.cs" };
        foreach (var file in files.Where(f => File.Exists(Path.Combine(root, f))))
        {
            var info = new System.Diagnostics.ProcessStartInfo("git", $"diff --quiet HEAD -- {file}") { WorkingDirectory = root, RedirectStandardError = true, RedirectStandardOutput = true };
            System.Diagnostics.Process? p;
            try
            {
                p = System.Diagnostics.Process.Start(info);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return; // no git on this machine
            }

            using var started = p;
            if (p is null)
            {
                return;
            }

            p.WaitForExit(10_000);
            p.ExitCode.ShouldBeOneOf(0, 128, 127);
        }
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
    public void The_execution_project_has_no_http_server_no_http_client_factory_and_no_broker_library()
    {
        var references = Execution.GetReferencedAssemblies().Select(r => r.Name ?? string.Empty).ToList();

        references.ShouldNotContain(r => r.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        // P12-02 (ADR 0157): System.Net.Http is allowed, but its use is limited to the Dhan folder (Http_a_host_name_... above); no HTTP server, no Microsoft.Extensions.Http.
        // P12-01b (ADR 0156): plain Npgsql is allowed for the order record (Persistence/), as the role xd_exec; EF Core, the shared Infrastructure and any HTTP library are not.
        references.ShouldNotContain(r => r == "Microsoft.Extensions.Http" || r.Contains("EntityFramework", StringComparison.Ordinal));
        references.ShouldNotContain(r => Regex.IsMatch(r, "dhan|kite|zerodha|upstox|fyers|smartapi|angelone|alpaca|ibapi|ccxt|5paisa|groww|quickfix", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
        references.ShouldNotContain("XDrishti.Infrastructure");
        references.ShouldNotContain("XDrishti.Api");

        var sources = SourceFiles(ExecutionSource).Select(File.ReadAllText).ToList();
        sources.ShouldNotContain(s => s.Contains("AddHttpClient", StringComparison.Ordinal) || s.Contains("WebSocket", StringComparison.Ordinal));
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

        // ADR 0172: the overlay of the live route adds exactly one member to exec (the proxy), which is also the only one on egress there; xd-exec gains no network.
        var overlay = string.Join('\n', File.ReadAllLines(RepositoryFiles.RepositoryFile("deploy", "compose.exec-live.yaml")).Where(l => !l.TrimStart().StartsWith('#')));
        var overlayBlocks = Regex.Split(overlay, @"\n(?=  [a-z][a-z0-9-]*:\n)", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        overlayBlocks.Where(b => b.Contains("networks:", StringComparison.Ordinal)).Select(b => b.Trim().Split(':')[0]).ShouldBe(["xd-exec-proxy"]);
        overlay.ShouldContain("networks: [exec, egress]");
        overlay.ShouldNotContain("secrets");
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
