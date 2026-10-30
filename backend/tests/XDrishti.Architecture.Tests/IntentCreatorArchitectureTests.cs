using System.Reflection;
using Microsoft.Extensions.Logging;
using XDrishti.Execution.Application.Intents;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P12-08 criteria 16 and 17 (P12-08a): the intent creator receives no broker, credential, sender or raw database port and no plan writer; its sources never name the Dhan namespace or an order path; its pure rules have no I/O; no host
/// composes it; the log ids 6710 to 6719 are used once each. A tripwire, not a guarantee: the barriers are the account gate, the closed mode source and the role.
/// </summary>
public sealed class IntentCreatorArchitectureTests
{
    private static string Source(params string[] parts) => Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!, "src", "XDrishti.Execution", Path.Combine(parts));

    [Fact]
    public void The_creator_receives_exactly_its_read_ports_the_event_store_and_the_intent_record()
    {
        var received = typeof(IntentCreator).GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();

        received.ShouldBe(
            ["IIntentPlanSource", "IIntentRecord", "IExecEventStore", "IModeSource", "IHaltSource", "IReadinessReads", "IIntentQuotes", "IIntentSettings", "ExecEnvironment", "TimeProvider", "ILogger`1"]);
    }

    [Theory]
    [InlineData("Application", "Intents")]
    [InlineData("Domain", "Intents")]
    public void The_intent_sources_never_name_the_dhan_namespace_a_sender_a_credential_or_an_order_path(string layer, string folder)
    {
        var files = Directory.GetFiles(Source(layer, folder), "*.cs");
        files.Length.ShouldBeGreaterThan(0);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution.Dhan", customMessage: file);
            text.ShouldNotContain("IBrokerOrders", customMessage: file);
            text.ShouldNotContain("LegSender", customMessage: file);
            text.ShouldNotContain("IExecCredentials", customMessage: file);
            text.ShouldNotContain("/v2/", customMessage: file);
        }
    }

    [Theory]
    [InlineData("PgIntentPlanSource.cs")]
    [InlineData("PgIntentRecord.cs")]
    public void The_stores_write_no_row_outside_the_execution_schema(string file)
    {
        var text = File.ReadAllText(Source("Persistence", file));

        text.ShouldNotMatch(@"(?is)(INSERT\s+INTO|UPDATE|DELETE\s+FROM)\s+(trading|workflow|market|accounts)\.");
        text.ShouldNotContain("UPDATE execution.", Case.Insensitive);
        text.ShouldNotContain("DELETE FROM execution.", Case.Insensitive);
    }

    [Fact]
    public void The_pure_intent_rules_have_no_io_and_know_no_application_layer()
    {
        foreach (var file in Directory.GetFiles(Source("Domain", "Intents"), "*.cs"))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("using XDrishti.Execution.Application", customMessage: file);
            text.ShouldNotContain("using Npgsql", customMessage: file);
            text.ShouldNotContain("DateTimeOffset.UtcNow", customMessage: file);
            text.ShouldNotContain("TimeProvider.System", customMessage: file);
        }
    }

    [Fact]
    public void No_host_composes_the_creator()
    {
        foreach (var file in Directory.GetFiles(Source("Host"), "*.cs").Append(Source("Program.cs")))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("IntentCreator", customMessage: file);
            text.ShouldNotContain("PgIntentRecord", customMessage: file);
        }
    }

    [Fact]
    public void The_log_ids_6710_to_6719_are_each_used_once_in_the_shipped_assemblies()
    {
        var ids = new[] { typeof(IntentCreator).Assembly, Assembly.Load("XDrishti.Application") }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId))
            .Where(id => id is >= 6710 and <= 6719)
            .Order()
            .ToList();

        ids.ShouldBe([6710, 6711, 6712, 6713, 6714, 6715]);
    }
}
