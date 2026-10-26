using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Capabilities;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The capability gate (spec P8-18 Part A, ADR 0128) is the owner's switch and nothing else: one Application handler depends on the write port, one Api endpoint module calls that handler and asks for a fresh password before it switches ON, no job, service, rule or other endpoint
/// can switch a capability, the readers and the gate have no write port, and the log is insert-only for the runtime role. Each scan has a sentinel, a case the same code must catch.
/// </summary>
public sealed partial class CapabilityArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly Assembly Worker = Assembly.Load(new AssemblyName("XDrishti.Worker"));

    private static readonly string Writer = typeof(ICapabilityLogWriter).FullName!;

    private static readonly string Command = typeof(ChangeCapabilityCommand).FullName!;

    [GeneratedRegex(@"\b(UPDATE|DELETE|TRUNCATE)\b\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Rewriting();

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static List<string> Names(IEnumerable<Type> types) => [.. types.Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void Only_the_owner_handler_depends_on_the_write_port_in_the_application_and_nothing_else_in_the_hosts_does()
    {
        Names(Types.InAssembly(Application).That().HaveDependencyOn(Writer).GetTypes()).ShouldBe(["ChangeCapabilityHandler"]);
        Names(Types.InAssembly(Infrastructure).That().HaveDependencyOn(Writer).GetTypes()).ShouldBe(["CapabilityLogStore", "DependencyInjection"]); // the store implements it, the registration names it
        Types.InAssembly(Api).That().HaveDependencyOn(Writer).GetTypes().ShouldBeEmpty();
        Types.InAssembly(Worker).That().HaveDependencyOn(Writer).GetTypes().ShouldBeEmpty();
        Names(Types.InAssembly(typeof(CapabilityWriterSentinel).Assembly).That().HaveDependencyOn(Writer).GetTypes()).ShouldContain(nameof(CapabilityWriterSentinel), "the scan sees a use of the write port");
    }

    [Fact]
    public void Only_the_owner_endpoint_module_names_the_switch_command_and_nothing_in_the_worker_or_the_infrastructure_does()
    {
        Names(Types.InAssembly(Api).That().HaveDependencyOn(Command).GetTypes()).ShouldBe(["CapabilitiesEndpoints"]);
        Names(Types.InAssembly(Application).That().HaveDependencyOn(Command).GetTypes()).ShouldBe(["ChangeCapabilityCommand", "ChangeCapabilityHandler", "DependencyInjection"]);
        Types.InAssembly(Infrastructure).That().HaveDependencyOn(Command).GetTypes().ShouldBeEmpty();
        Types.InAssembly(Worker).That().HaveDependencyOn(Command).GetTypes().ShouldBeEmpty();
        Names(Types.InAssembly(typeof(CapabilityWriterSentinel).Assembly).That().HaveDependencyOn(Command).GetTypes()).ShouldContain(nameof(CapabilityWriterSentinel));
    }

    [Fact]
    public void The_gate_and_the_readers_have_no_write_port_and_the_gate_is_the_only_thing_a_guarded_write_asks()
    {
        foreach (var name in new[] { "CapabilityGate", "CapabilityViews", "GetCapabilitiesHandler" })
        {
            Types.InAssembly(Application).That().HaveName(name).ShouldNot().HaveDependencyOn(Writer).GetResult().IsSuccessful.ShouldBeTrue(name);
            Types.InAssembly(Application).That().HaveName(name).ShouldNot().HaveDependencyOn(Command).GetResult().IsSuccessful.ShouldBeTrue(name);
        }

        typeof(ICapabilityLog).GetMethods().Select(m => m.Name).ShouldBe(["NewestAsync", "HistoryAsync"], ignoreOrder: true);
        typeof(ICapabilityGate).GetMethods().Select(m => m.Name).ShouldBe(["RequireAsync", "IsEnabledAsync"], ignoreOrder: true);
        typeof(ICapabilityLogWriter).GetMethods().Select(m => m.Name).ShouldBe(["AppendAsync"]);
    }

    [Fact]
    public void The_endpoint_module_asks_for_a_fresh_password_to_switch_on_limits_the_attempts_and_is_the_only_place_that_maps_capabilities()
    {
        var text = Source("XDrishti.Api", "Endpoints", "CapabilitiesEndpoints.cs");

        Regex.Count(text, @"\.MapGet\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(1);
        Regex.Count(text, @"\.MapPut\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(1);
        Regex.Count(text, @"\.MapPost\(|\.MapDelete\(|\.MapPatch\(|\.MapMethods\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(0);
        Regex.Count(text, @"RequireRateLimiting\(SessionAuthentication\.LoginRateLimitPolicy\)", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(1);
        text.ShouldContain("request.Enabled\n            ? await OwnerAct.AuthorizeAsync(");
        text.ShouldContain(": OwnerAct.SessionOwner(http)");
        text.ShouldNotContain("Elevate");
        text.ShouldNotContain("StepUpWindow");
        Source("XDrishti.Api", "Endpoints", "OwnerAct.cs").ShouldContain("Elevate: false");

        var dir = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "CapabilitiesEndpoints.cs"))!;
        Directory.GetFiles(dir, "*.cs").Where(f => !f.EndsWith("CapabilitiesEndpoints.cs", StringComparison.Ordinal) && File.ReadAllText(f).Contains("\"/capabilities", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_owner_is_the_signed_in_session_never_a_field_of_a_body()
    {
        var request = Api.GetType("XDrishti.Api.Endpoints.ChangeCapabilityRequest")!;

        request.GetProperties().Select(p => p.Name).ShouldNotContain("Actor");
        request.GetProperties().Select(p => p.Name).ShouldNotContain("Owner");
        request.GetProperties().ShouldContain(p => p.Name == "Password");
    }

    [Fact]
    public void The_log_is_written_with_an_insert_statement_only_and_the_scan_sees_a_rewrite()
    {
        var store = Source("XDrishti.Infrastructure", "Capabilities", "CapabilityLogStore.cs");

        Rewriting().IsMatch(store).ShouldBeFalse();
        store.ShouldContain("INSERT INTO platform.capability_changes");
        Rewriting().IsMatch("UPDATE platform.capability_changes SET enabled = true").ShouldBeTrue("the scan sees an update");
        Rewriting().IsMatch("DELETE FROM platform.capability_changes").ShouldBeTrue("the scan sees a delete");
    }

    [Fact]
    public void The_table_is_granted_on_its_own_and_made_append_only_and_the_schema_is_not_regranted()
    {
        var migrations = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Persistence", "Migrations", "XDrishtiDbContextModelSnapshot.cs"))!;
        var text = File.ReadAllText(Directory.GetFiles(migrations, "*AddCapabilityChanges.cs").Single());

        text.ShouldContain("GrantTableToAppRole(Schemas.Platform, \"capability_changes\")");
        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Platform, \"capability_changes\")");
        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldContain("DROP TRIGGER");
        text.ShouldContain("DROP FUNCTION");
    }

    [Fact]
    public void The_whitelist_is_a_code_constant_not_a_check_constraint_so_a_new_capability_is_not_a_migration()
    {
        var migrations = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Persistence", "Migrations", "XDrishtiDbContextModelSnapshot.cs"))!;
        var text = File.ReadAllText(Directory.GetFiles(migrations, "*AddCapabilityChanges.cs").Single());

        text.ShouldNotContain("experiments_ui");
        XDrishti.Domain.Capabilities.Capabilities.All.ShouldAllBe(c => Regex.IsMatch(c.Name, "^[a-z][a-z0-9_]{0,39}$", RegexOptions.None, TimeSpan.FromSeconds(1)));
    }
}

/// <summary>Uses the write port and the switch command on purpose, to prove the scans see such uses (nothing else uses them).</summary>
internal sealed class CapabilityWriterSentinel(ICapabilityLogWriter writer)
{
    public ICapabilityLogWriter Writer { get; } = writer;

    public ChangeCapabilityCommand? Command { get; init; }
}
