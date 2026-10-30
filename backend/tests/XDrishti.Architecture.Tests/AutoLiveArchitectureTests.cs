using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using XDrishti.Execution.Application.Limits;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P12-09 criteria 11, 13 and 14 (ADR 0167): the reservation, the automatic actions and the loss-cap evaluator receive exactly their ports and no broker, credential, sender or rate limiter (the daily caps never rely on the rate); their
/// sources never name the Dhan namespace or an order path; no host composes them and nothing implements the two hand-over ports; the stores write only their own tables and never update or delete; the shared limits rules have no I/O; the
/// log ids 6720 to 6729 are used once each. A tripwire, not a guarantee: the barriers are the closed mode source (no account can be <c>auto_live</c>), the database trigger and the role.
/// </summary>
public sealed class AutoLiveArchitectureTests
{
    private static string Backend => Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "XDrishti.slnx"))!;

    private static string Execution(params string[] parts) => Path.Combine(Backend, "src", "XDrishti.Execution", Path.Combine(parts));

    private static List<string> Received(Type type) => [.. type.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name)];

    [Fact]
    public void The_reservation_the_actions_and_the_evaluator_receive_exactly_their_ports_and_no_broker_sender_or_rate_limiter()
    {
        Received(typeof(ReserveEntry)).ShouldBe(["IModeSource", "IAutoLiveSettingsSource", "ILimitsStore", "ILedger", "IActionLog", "IOwnerPresence", "IFeedHealth", "ILogger`1"]);
        Received(typeof(AutoActions)).ShouldBe(["ReserveEntry", "IActionLog", "IEntryHandOver", "IReduceHandOver", "ILogger`1"]);
        Received(typeof(LossCapEvaluator)).ShouldBe(["ILossCapSource", "IHaltRequests", "IActionLog", "ILogger`1"]);
        Received(typeof(LimitsLossCapSource)).ShouldBe(["ILimitsStore"]);
    }

    private static List<string> Sources() =>
    [
        .. Directory.GetFiles(Execution("Application", "Limits"), "*.cs"),
        .. Directory.GetFiles(Execution("Domain", "Limits"), "*.cs"),
        Execution("Application", "Ports", "LimitsPorts.cs"),
        Execution("Persistence", "PgLimits.cs"),
        Execution("Persistence", "PgLedger.cs"),
    ];

    [Fact]
    public void The_limits_sources_never_name_the_dhan_namespace_a_sender_a_credential_a_rate_limiter_or_an_order_path()
    {
        var files = Sources();
        files.Count.ShouldBeGreaterThan(10);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution.Dhan", customMessage: file);
            text.ShouldNotContain("IBrokerOrders", customMessage: file);
            text.ShouldNotContain("LegSender", customMessage: file);
            text.ShouldNotContain("IExecCredentials", customMessage: file);
            text.ShouldNotContain("ExecRateLimiter", customMessage: file);
            text.ShouldNotContain("/v2/", customMessage: file);
        }
    }

    [Fact]
    public void No_host_composes_the_automatic_path_and_nothing_implements_the_hand_over_ports()
    {
        foreach (var file in Directory.GetFiles(Execution("Host"), "*.cs").Append(Execution("Program.cs")))
        {
            var text = File.ReadAllText(file);
            foreach (var name in new[] { "ReserveEntry", "AutoActions", "LossCapEvaluator", "LimitsLossCapSource", "PgLedger", "PgLimits", "IEntryHandOver", "IReduceHandOver", "ILedger", "IActionLog" })
            {
                text.ShouldNotContain(name, customMessage: file);
            }
        }

        typeof(ReserveEntry).Assembly.GetTypes().Where(t => !t.IsInterface && (typeof(IEntryHandOver).IsAssignableFrom(t) || typeof(IReduceHandOver).IsAssignableFrom(t))).ShouldBeEmpty();
    }

    [Fact]
    public void The_stores_write_only_their_own_tables_and_never_update_or_delete()
    {
        var exec = File.ReadAllText(Execution("Persistence", "PgLedger.cs"));
        var reads = File.ReadAllText(Execution("Persistence", "PgLimits.cs"));
        var api = File.ReadAllText(Path.Combine(Backend, "src", "XDrishti.Infrastructure", "Execution", "ExecutionLimitsAccess.cs"));

        foreach (var text in new[] { exec, reads, api })
        {
            text.ShouldNotMatch(@"(?is)\bUPDATE\s+\w+\.");
            text.ShouldNotMatch(@"(?is)DELETE\s+FROM");
        }

        reads.ShouldNotContain("INSERT INTO");
        Regex.Matches(exec, @"INSERT INTO ([a-z_.]+)", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().Order().ShouldBe(["execution.action_log", "execution.ledger_entries"]);
        Regex.Matches(api, @"INSERT INTO ([a-z_.]+)", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).Distinct().ShouldBe(["execution.limits"]);
    }

    [Fact]
    public void The_shared_limits_rules_have_no_io()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Backend, "src", "XDrishti.Domain", "Execution", "Limits"), "*.cs").Concat(Directory.GetFiles(Execution("Domain", "Limits"), "*.cs")))
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
    public void The_log_ids_6720_to_6729_are_each_used_once_in_the_shipped_assemblies()
    {
        var ids = new[] { typeof(ReserveEntry).Assembly, Assembly.Load("XDrishti.Application") }
            .SelectMany(a => a.GetTypes())
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId))
            .Where(id => id is >= 6720 and <= 6729)
            .Order()
            .ToList();

        ids.ShouldBe([6720, 6721, 6722, 6723, 6724, 6725, 6726, 6727, 6728, 6729]);
    }
}
