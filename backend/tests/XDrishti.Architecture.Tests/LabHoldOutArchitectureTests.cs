using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-06, test plan: the sessions inside the locked hold-out are read by one place only. <c>HoldOutPass</c> (the plain pass over the block) is named by its interface and its registration and by the unlock; no handler, report, dashboard or export names it.
/// The new Domain types are pure (no I/O, no clock), and no code path of the Lab can place an order.
/// </summary>
public sealed class LabHoldOutArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly ApiAssembly = Assembly.Load(new AssemblyName("XDrishti.Api"));

    private static readonly string[] PassTypes = ["XDrishti.Application.Lab.IHoldOutPass", "XDrishti.Application.Lab.HoldOutPass"];

    [Fact]
    public void Only_the_unlock_and_the_pass_itself_name_the_hold_out_pass()
    {
        var users = Types.InAssembly(Application).That().HaveDependencyOnAny(PassTypes).GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["DependencyInjection", "HoldOutPass", "HoldOutUnlocker"]);
        Types.InAssembly(Infrastructure).That().HaveDependencyOnAny(PassTypes).GetTypes().ShouldBeEmpty();
        Types.InAssembly(ApiAssembly).That().HaveDependencyOnAny(PassTypes).GetTypes().ShouldBeEmpty();
    }

    [Fact]
    public void The_unlock_is_reached_only_from_the_owner_handler_the_queued_job_and_the_runners_auto_path()
    {
        var users = Types.InAssembly(Application).That().HaveDependencyOn("XDrishti.Application.Lab.HoldOutUnlocker").GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();

        // HO-01b: the seal step queues the auto look through the cheap CheckAsync only (it never calls UnlockAsync; the look itself still runs in the queued job)
        users.ShouldBe(["DependencyInjection", "HoldOutJobRunner", "HoldOutUnlocker", "LabRunner", "StrategyAutoLookStep", "UnlockHoldOutHandler"]);
    }

    [Fact]
    public void The_random_entry_null_and_the_placebo_set_name_no_hold_out_type_and_their_walks_are_pure()
    {
        string[] holdOut = ["XDrishti.Application.Lab.IHoldOutPass", "XDrishti.Application.Lab.HoldOutPass", "XDrishti.Application.Lab.HoldOutUnlocker", "XDrishti.Application.Lab.LabHoldOutGuard", "XDrishti.Application.Abstractions.Lab.IHoldOutStore", "XDrishti.Application.Abstractions.Lab.HoldOutBlock"];

        // CF-07: the null reads the window of the run only (the development window); nothing in it can reach the block
        Types.InAssembly(Application).That().HaveName("LabNulls").Or().HaveName("RunNullWalkQuery").Or().HaveName("IRandomEntryEngine").Or().HaveName("LabPlacebos").Or().HaveName("RunPlaceboWalkQuery").Or().HaveName("IPlaceboEngine").Or().HaveName("LabPlateau").Should().NotHaveDependencyOnAny(holdOut).GetResult().IsSuccessful.ShouldBeTrue();

        var simulation = typeof(XDrishti.Simulation.Replay.RandomEntryWalker).Assembly;
        string[] banned = ["System.IO", "System.Net", "Npgsql", "Microsoft.EntityFrameworkCore", "System.TimeProvider", "System.Random", "XDrishti.Application", "XDrishti.Infrastructure"];
        var walk = Types.InAssembly(simulation).That().HaveName("RandomEntryWalker").Or().HaveName("EntryOverride").Or().HaveName("PlaceboWalker").Should().NotHaveDependencyOnAny(banned).GetResult();
        walk.IsSuccessful.ShouldBeTrue(string.Join(", ", walk.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_plateau_gate_reads_the_stored_sweep_only_it_depends_on_no_port_no_simulation_and_no_store()
    {
        // CF-07 part C (ADR 0210): no new simulation and no new look; the gate is a pure reading of the sweep the robustness stage already stored
        string[] banned = ["XDrishti.Application.Abstractions", "XDrishti.Application.Research", "XDrishti.Infrastructure", "XDrishti.Simulation", "System.IO", "System.Net", "System.TimeProvider", "System.Random"];

        var result = Types.InAssembly(Application).That().HaveName("LabPlateau").Should().NotHaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_domain_lab_and_confidence_types_do_no_io_and_read_no_clock()
    {
        string[] banned = ["System.IO", "System.Net", "System.Diagnostics.Process", "Npgsql", "Microsoft.EntityFrameworkCore", "System.TimeProvider"];

        var result = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Lab").Or().ResideInNamespace("XDrishti.Domain.Confidence").Should().NotHaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_lab_application_code_names_no_broker_or_execution_type()
    {
        string[] banned = ["XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Infrastructure.Brokers"];

        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Lab").Should().NotHaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
