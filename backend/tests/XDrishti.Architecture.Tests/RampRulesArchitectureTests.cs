using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P11-04b (ADR 0151): the evidence gates, breach rules, loss measure and halts are pure or read stored data; the evaluator writes rows and moves no ticket and calls no broker; every owner act of the ramp sits behind the capability gate, then the fresh
/// password, then the handler; and nothing in the new code names an order action or an outbound call.
/// </summary>
public sealed class RampRulesArchitectureTests
{
    private static readonly string[] PureFiles =
    [
        "XDrishti.Domain/LiveTrading/RampGates.cs", "XDrishti.Domain/LiveTrading/RampGate.cs", "XDrishti.Domain/LiveTrading/RampEvidence.cs", "XDrishti.Domain/LiveTrading/RampThresholds.cs",
        "XDrishti.Domain/LiveTrading/OneSidedT.cs", "XDrishti.Domain/LiveTrading/PhaseCheck/LivePhaseCheck.cs", "XDrishti.Domain/LiveTrading/PhaseCheck/LivePhaseText.cs", "XDrishti.Domain/LiveTrading/BreachRules.cs", "XDrishti.Domain/LiveTrading/DayLoss.cs", "XDrishti.Domain/LiveTrading/HaltRules.cs", "XDrishti.Domain/LiveTrading/GateSnapshot.cs",
    ];

    private static readonly string[] CodeFiles =
    [
        .. PureFiles,
        "XDrishti.Application/LiveTrading/RampAssessor.cs", "XDrishti.Application/LiveTrading/LiveLossReader.cs", "XDrishti.Application/LiveTrading/EvaluateRampHandler.cs", "XDrishti.Application/LiveTrading/OwnerRampActs.cs",
        "XDrishti.Application/LiveTrading/LiveReadyFactsSource.cs", "XDrishti.Application/LiveTrading/LiveNotices.cs", "XDrishti.Application/LiveTrading/GetLiveHaltsHandler.cs", "XDrishti.Application/LiveTrading/PhaseCheckExitReport.cs", "XDrishti.Application/LiveTrading/EvaluateLivePhaseCheckHandler.cs", "XDrishti.Api/Cli/LivePhaseCheckCommand.cs",
        "XDrishti.Infrastructure/LiveTrading/LiveHaltStore.cs", "XDrishti.Api/Cli/LiveRampCommand.cs",
    ];

    private static string Source(string relative) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. relative.Split('/')]));

    [Fact]
    public void The_rules_are_pure_no_io_no_clock_no_framework()
    {
        foreach (var file in PureFiles)
        {
            var text = Source(file);
            foreach (var forbidden in new[] { "System.IO", "System.Net", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "Random" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void The_new_code_calls_no_broker_and_no_client_and_names_no_order_action()
    {
        foreach (var file in CodeFiles)
        {
            var text = Source(file);
            foreach (var forbidden in new[] { "HttpClient", "IHttpClientFactory", "IBrokerConnector", "IBrokerClient", "IBrokerTradeBook", "IBrokerOrderBook", "IBrokerTrades", "System.Net.Http", "Process.Start", "IDhan", "DhanClient" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }

            Regex.IsMatch(text, @"(?i)\b\w*(placeorder|submitorder|cancelorder|modifyorder|neworder|squareoff)\w*\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_evaluator_and_the_readers_move_no_ticket_and_the_halt_store_only_reads_and_inserts()
    {
        foreach (var file in new[] { "EvaluateRampHandler.cs", "RampAssessor.cs", "LiveLossReader.cs", "LiveReadyFactsSource.cs", "LiveNotices.cs" })
        {
            var text = Source("XDrishti.Application/LiveTrading/" + file);
            text.ShouldNotContain("MoveTicketCommand", Case.Sensitive, file);
            text.ShouldNotContain("IPlanStore.Move", Case.Sensitive, file);
        }

        var store = Source("XDrishti.Infrastructure/LiveTrading/LiveHaltStore.cs");
        store.ShouldNotContain("UPDATE ");
        store.ShouldNotContain("DELETE ");
        store.ShouldNotContain("TRUNCATE");
    }

    [Fact]
    public void Every_owner_act_of_the_ramp_asks_the_gate_then_the_password_then_the_handler()
    {
        var endpoints = Source("XDrishti.Api/Endpoints/LiveTradingEndpoints.cs");
        var shared = endpoints.IndexOf("OwnerRampActAsync(\n", StringComparison.Ordinal);
        shared.ShouldBeGreaterThan(0);
        var body = endpoints[shared..];
        var gate = body.IndexOf("gate.Enabled", StringComparison.Ordinal);
        var password = body.IndexOf("OwnerAct.AuthorizeAsync(", StringComparison.Ordinal);
        var act = body.IndexOf("await act(owner.Value)", StringComparison.Ordinal);
        gate.ShouldBeGreaterThan(0);
        password.ShouldBeGreaterThan(gate);
        act.ShouldBeGreaterThan(password);
        endpoints.ShouldContain("ClearLiveRampHold");
        endpoints.ShouldContain("ConfirmLiveRampExit");
        Regex.Count(endpoints, @"RequireRateLimiting\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(5);
    }

    [Fact]
    public void The_migration_is_append_only_with_a_down()
    {
        var migration = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile(["backend", "src", "XDrishti.Infrastructure", "Persistence", "Schemas.cs"]))!, "Migrations"), "*_AddLiveHalts.cs").Single();
        var text = File.ReadAllText(migration);

        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Trading, \"live_halts\")");
        text.ShouldContain("DROP TABLE IF EXISTS trading.live_halts");
    }
}
