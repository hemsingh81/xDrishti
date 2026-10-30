using System.Reflection;
using Microsoft.Extensions.Logging;
using XDrishti.Execution.Application.Confirm;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P12-08 criteria 12 and 17 (P12-08b, ADR 0166): the ticket issuer, the stored confirm gate and the hand-off receive no broker, credential or sender port; their sources never name the Dhan namespace or an order path; no host composes the
/// issuer, the stored gate or the hand-off (the confirm gate stays the closed default); the stores write only their own confirm tables; the shared rules have no I/O; the log ids 6760 to 6769 are used once each. A tripwire, not a guarantee:
/// the barriers are the closed mode source (no account can be <c>semi_auto</c>), the database trigger and the role.
/// </summary>
public sealed class ConfirmTicketArchitectureTests
{
    private static string Backend => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Execution(params string[] parts) => Path.Combine(Backend, "src", "XDrishti.Execution", Path.Combine(parts));

    private static List<string> Received(Type type) => [.. type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name)];

    [Fact]
    public void The_issuer_the_closer_and_the_hand_off_receive_exactly_their_ports_and_no_broker_or_sender()
    {
        Received(typeof(ConfirmTicketIssuer)).ShouldBe(["IConfirmTicketStore", "IModeSource", "IIntentQuotes", "IIntentSettings", "IConfirmSettings", "ExecEnvironment", "TimeProvider", "ILogger`1"]);
        Received(typeof(TicketCloser)).ShouldBe(["IConfirmTicketStore", "ILogger`1"]);
        Received(typeof(StoredConfirmGate)).ShouldBe(["IConfirmDecisions"]);
        Received(typeof(ConfirmHandOff)).ShouldBe(["IConfirmGate", "IExecEventStore", "TimeProvider", "ILogger`1"]);
    }

    [Fact]
    public void The_confirm_sources_never_name_the_dhan_namespace_a_sender_a_credential_or_an_order_path()
    {
        var files = Directory.GetFiles(Execution("Application", "Confirm"), "*.cs").Append(Execution("Persistence", "PgConfirmTickets.cs")).Append(Execution("Host", "TicketExpiryService.cs")).ToList();
        files.Count.ShouldBeGreaterThan(4);

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

    [Fact]
    public void No_host_composes_the_issuer_the_stored_gate_or_the_hand_off_and_the_confirm_gate_stays_closed()
    {
        foreach (var file in Directory.GetFiles(Execution("Host"), "*.cs").Append(Execution("Program.cs")))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("ConfirmTicketIssuer", customMessage: file);
            text.ShouldNotContain("StoredConfirmGate", customMessage: file);
            text.ShouldNotContain("ConfirmHandOff", customMessage: file);
            text.ShouldNotContain("IConfirmDecisions", customMessage: file);
        }

        var services = File.ReadAllText(Execution("Host", "ExecServices.cs"));
        services.ShouldContain("services.TryAddSingleton<IConfirmGate, ClosedConfirmGate>();");
        services.ShouldContain("services.AddHostedService<TicketExpiryService>();");
    }

    [Fact]
    public void The_stores_write_only_their_own_confirm_tables_and_never_update_or_delete()
    {
        var exec = File.ReadAllText(Execution("Persistence", "PgConfirmTickets.cs"));
        var api = File.ReadAllText(Path.Combine(Backend, "src", "XDrishti.Infrastructure", "Execution", "ExecutionConfirmAccess.cs"));

        foreach (var text in new[] { exec, api })
        {
            text.ShouldNotMatch(@"(?is)\bUPDATE\s+\w+\.");
            text.ShouldNotMatch(@"(?is)DELETE\s+FROM");
        }

        System.Text.RegularExpressions.Regex.Matches(exec, @"INSERT INTO ([a-z_.]+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().Order()
            .ShouldBe(["execution.confirm_tickets", "execution.ticket_outcomes"]);
        System.Text.RegularExpressions.Regex.Matches(api, @"INSERT INTO ([a-z_.]+)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().Order()
            .ShouldBe(["execution.day_unlocks", "execution.ticket_outcomes"]);
    }

    [Fact]
    public void The_shared_confirm_rules_have_no_io()
    {
        var folder = Path.Combine(Backend, "src", "XDrishti.Domain", "Execution", "Confirm");
        foreach (var file in Directory.GetFiles(folder, "*.cs"))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("Npgsql", customMessage: file);
            text.ShouldNotContain("DateTimeOffset.UtcNow", customMessage: file);
            text.ShouldNotContain("DateTime.Now", customMessage: file);
            text.ShouldNotContain("TimeProvider.System", customMessage: file);
            text.ShouldNotContain("System.IO", customMessage: file);
        }
    }

    [Fact]
    public void The_log_ids_6760_to_6769_are_each_used_once_in_the_shipped_assemblies()
    {
        var ids = new[] { typeof(ConfirmTicketIssuer).Assembly, Assembly.Load("XDrishti.Application") }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId))
            .Where(id => id is >= 6760 and <= 6769)
            .Order()
            .ToList();

        ids.ShouldBe([6760, 6761, 6762, 6763, 6764]);
    }
}
