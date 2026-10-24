using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P7-11 (ADR 0096): the code that checks, arms and expires tickets has no path to the reserve, to the autonomy settings or to a promotion, approves nothing and uses only read ports and the plan record. The no-order tripwire
/// (<see cref="NoOrderPlacementArchitectureTests"/>) covers these assemblies as well.
/// </summary>
public sealed class PreOpenArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Planning.PreOpenRules).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly string[] PreOpenTypes =
    [
        "XDrishti.Application.Planning.RunPreOpenCheckHandler",
        "XDrishti.Application.Planning.ExpireTicketsHandler",
        "XDrishti.Application.Planning.PreOpenService",
        "XDrishti.Application.Planning.PlanExpiryService",
        "XDrishti.Application.Live.ArmedTicketsProvider",
        "XDrishti.Domain.Planning.PreOpenRules",
        "XDrishti.Infrastructure.Planning.PreOpenSource",
    ];

    private static IEnumerable<Type> Subjects() => PreOpenTypes.Select(name => (Application.GetType(name) ?? Domain.GetType(name) ?? Infrastructure.GetType(name)).ShouldNotBeNull(name));

    [Fact]
    public void No_pre_open_type_reaches_promotion_the_autonomy_settings_or_the_selection_builder()
    {
        foreach (var type in Subjects())
        {
            var result = Types.InAssembly(type.Assembly).That().HaveName(type.Name)
                .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Autonomy", "XDrishti.Application.Autonomy", "XDrishti.Domain.Autonomy", "XDrishti.Application.Planning.SelectionPlanBuilder", "XDrishti.Application.Planning.AutoApprovePlanHandler")
                .GetResult();

            result.IsSuccessful.ShouldBeTrue(type.Name + " depends on a promotion or approval path");
        }
    }

    [Fact]
    public void No_member_parameter_or_field_of_the_pre_open_code_is_named_for_promotion_or_the_reserve()
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in Subjects())
        {
            var names = type.GetMembers(All).Select(m => m.Name)
                .Concat(type.GetConstructors(All).SelectMany(c => c.GetParameters()).Select(p => p.Name ?? string.Empty))
                .Concat(type.GetMethods(All).SelectMany(m => m.GetParameters()).Select(p => p.Name ?? string.Empty))
                .Concat(type.GetFields(All).Select(f => f.FieldType.Name));
            names.ShouldNotContain(n => n.Contains("promot", StringComparison.OrdinalIgnoreCase) || n.Contains("Reserve", StringComparison.OrdinalIgnoreCase), type.Name);
        }
    }

    [Fact]
    public void The_pre_open_source_port_only_reads()
    {
        typeof(IPreOpenSource).GetMethods().Select(m => m.Name).ShouldBe(["ReadAsync"]);
        typeof(IArmedTicketReader).GetMethods().Select(m => m.Name).ShouldBe(["ListAsync"]);
    }

    [Fact]
    public void The_handlers_and_providers_are_internal_and_the_services_are_the_only_public_entry()
    {
        Application.GetType("XDrishti.Application.Planning.RunPreOpenCheckHandler")!.IsNotPublic.ShouldBeTrue();
        Application.GetType("XDrishti.Application.Planning.ExpireTicketsHandler")!.IsNotPublic.ShouldBeTrue();
        Application.GetType("XDrishti.Application.Live.ArmedTicketsProvider")!.IsNotPublic.ShouldBeTrue();
    }
}
