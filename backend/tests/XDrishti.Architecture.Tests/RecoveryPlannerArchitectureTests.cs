using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>The recovery planner (ADR 0240, AU-01) is pure and places no order: the pure domain code does no I/O, touches no clock or random source, and no recovery code names the execution service or a broker order endpoint.</summary>
public sealed class RecoveryPlannerArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    [Fact]
    public void The_planner_and_its_records_are_pure_and_name_no_io_execution_or_infrastructure()
    {
        var result = Types.InAssembly(Domain).That().HaveNameStartingWith("Recovery")
            .ShouldNot().HaveDependencyOnAny(
                "System.IO", "System.Net", "System.Data", "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.Extensions", "XDrishti.Execution", "XDrishti.Infrastructure", "XDrishti.Application")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_planner_reads_no_clock_the_instant_is_passed_in()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Platform", "Services", "RecoveryPlanner.cs"));

        source.ShouldNotContain("DateTimeOffset.Now");
        source.ShouldNotContain("UtcNow");
        source.ShouldNotContain("TimeProvider");
        source.ShouldNotContain("Random");
    }

    [Fact]
    public void The_recovery_settings_name_no_execution_type()
    {
        var result = Types.InAssembly(Application).That().HaveName("RecoverySettings")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Execution", "XDrishti.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_recovery_runner_and_ledger_name_no_execution_type_no_broker_and_only_use_ports()
    {
        var application = Types.InAssembly(Application).That().HaveNameStartingWith("Recovery").Or().HaveName("IRecoveryLedger").Or().HaveName("IRecoveryTrigger")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Execution", "XDrishti.Infrastructure", "Npgsql", "Microsoft.EntityFrameworkCore")
            .GetResult();
        var infrastructure = Types.InAssembly(typeof(XDrishti.Infrastructure.DependencyInjection).Assembly).That().HaveName("RecoveryLedger")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Execution")
            .GetResult();

        application.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", application.FailingTypeNames ?? []));
        infrastructure.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", infrastructure.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_recovery_sources_place_no_order_and_the_ledger_never_deletes()
    {
        foreach (var file in new[] { new[] { "XDrishti.Application", "Platform", "Services", "RecoveryRunner.cs" }, ["XDrishti.Infrastructure", "Platform", "RecoveryLedger.cs"] })
        {
            var source = File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. file]));
            source.ShouldNotContain("XDrishti.Execution");
            source.ShouldNotContain("PlaceOrder", Case.Insensitive);
            source.ShouldNotContain("DELETE FROM", Case.Insensitive);
        }
    }

    [Fact]
    public void The_scheduler_has_no_catch_up_branch_of_its_own_and_only_asks_the_recovery_runner()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Platform", "Services", "ServiceScheduler.cs"));

        source.ShouldNotContain("CatchUpAsync");
        source.ShouldNotContain("MissedOccurrence");
        source.ShouldNotContain("ServiceRunTrigger.CatchUp");
        source.ShouldContain("recovery.Request(");
    }
}
