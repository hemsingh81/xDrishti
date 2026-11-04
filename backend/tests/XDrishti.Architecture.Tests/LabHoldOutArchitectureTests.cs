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
    public void The_unlock_is_reached_only_from_the_owner_handler_and_the_runners_auto_path()
    {
        var users = Types.InAssembly(Application).That().HaveDependencyOn("XDrishti.Application.Lab.HoldOutUnlocker").GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["DependencyInjection", "HoldOutUnlocker", "LabRunner", "UnlockHoldOutHandler"]);
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
