using System.Reflection;
using Microsoft.Extensions.Logging;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Readiness;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P12-11 criteria 13 and 16, ADR 0164: the readiness probe has exactly two broker reads and no order, trade, position or funds call; it never names the Dhan namespace, a credential or a token; its pure rules have no I/O; the log ids 6740 to 6749 are used once each.
/// A tripwire, not a guarantee: the barriers are the account gate, the role and the network.
/// </summary>
public sealed class ReadinessArchitectureTests
{
    private static string Source(params string[] parts) => Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!, "src", "XDrishti.Execution", Path.Combine(parts));

    [Fact]
    public void The_broker_facts_port_has_exactly_the_profile_and_the_static_ip_reads()
    {
        typeof(IBrokerFacts).GetMethods().Select(m => m.Name).Order().ShouldBe(["GetProfileAsync", "GetStaticIpAsync"]);
    }

    [Fact]
    public void The_loop_receives_no_order_read_or_credential_port()
    {
        var received = typeof(ReadinessLoop).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();

        received.ShouldContain(nameof(IBrokerFacts));
        received.ShouldNotContain(nameof(IBrokerOrders));
        received.ShouldNotContain(nameof(IBrokerReads));
        received.ShouldNotContain(nameof(IBrokerStops));
        received.ShouldNotContain("IExecCredentials");
        received.ShouldNotContain("NpgsqlDataSource");
        received.Count.ShouldBe(9);
    }

    [Theory]
    [InlineData("Application", "Readiness")]
    [InlineData("Domain", "Readiness")]
    public void The_readiness_sources_never_name_the_dhan_namespace_a_credential_a_token_value_or_an_order_path(string layer, string folder)
    {
        var files = Directory.GetFiles(Source(layer, folder), "*.cs");
        files.Length.ShouldBeGreaterThan(2);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution.Dhan", customMessage: file);
            text.ShouldNotContain("DhanCredentials", customMessage: file);
            text.ShouldNotContain("IExecCredentials", customMessage: file);
            text.ShouldNotContain("IBrokerOrders", customMessage: file);
            text.ShouldNotContain("/v2/", customMessage: file);
            text.ShouldNotContain("access-token", Case.Insensitive, file);
        }
    }

    [Fact]
    public void The_pure_readiness_rules_have_no_io_and_know_no_application_layer()
    {
        foreach (var file in Directory.GetFiles(Source("Domain", "Readiness"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("using XDrishti.Execution.Application", customMessage: file);
            text.ShouldNotContain("using XDrishti.Execution.Persistence", customMessage: file);
            text.ShouldNotContain("using Npgsql", customMessage: file);
            text.ShouldNotContain("System.Net.Http", customMessage: file);
            text.ShouldNotContain("DateTimeOffset.UtcNow", customMessage: file);
            text.ShouldNotContain("TimeProvider.System", customMessage: file);
        }
    }

    [Fact]
    public void The_log_ids_6740_to_6749_are_each_used_once_in_the_shipped_assemblies()
    {
        var ids = new[] { typeof(ReadinessLoop).Assembly, Assembly.Load("XDrishti.Application") }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId))
            .Where(id => id is >= 6740 and <= 6749)
            .Order()
            .ToList();

        ids.Distinct().Count().ShouldBe(ids.Count);
        ids.Count.ShouldBeGreaterThanOrEqualTo(8);
    }
}
