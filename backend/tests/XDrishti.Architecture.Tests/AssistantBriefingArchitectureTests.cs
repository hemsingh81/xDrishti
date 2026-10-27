using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-06 AC 14 (ADR 0140): the briefing stays text. The Domain types of the guard hold no I/O (covered by the assistant domain rule of P9-03); the briefing handlers reach the model only through <see cref="IBriefingWriter"/>, whose only implementation lives in <c>Infrastructure/Ai</c>; and
/// nothing in the briefing references a plan-writing, approval or order port (the no-order rule stays).
/// </summary>
public sealed class AssistantBriefingArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void The_briefing_reaches_the_model_only_through_its_writer_port()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Ai.Briefing").ShouldNot().HaveDependencyOn(typeof(IChatModel).FullName!).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Infrastructure).That().ImplementInterface(typeof(IBriefingWriter)).GetTypes().Select(t => t.Namespace).Distinct().ShouldBe(["XDrishti.Infrastructure.Ai.Assistant"]);
    }

    [Fact]
    public void The_briefing_has_no_reference_to_a_port_that_writes_a_plan_an_approval_or_an_order()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Ai.Briefing")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Live", "XDrishti.Application.Abstractions.Paper", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Abstractions.Brokers")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        var planPorts = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Ai.Briefing").GetTypes().SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters())).Select(p => p.ParameterType.Name).ToList();
        planPorts.ShouldNotContain("IAutoApprovalStore");
        planPorts.ShouldNotContain("IPlanBuilder");
    }

    [Fact]
    public void The_only_write_port_of_the_briefing_is_its_own_insert_only_store()
    {
        typeof(IBriefingStore).GetMethods().Select(m => m.Name).Order().ShouldBe(["FindAsync", "InsertAsync", "ListAsync"]);
        typeof(IBriefingWriter).GetMethods().Select(m => m.Name).ShouldBe(["WriteAsync"]);
        typeof(IBriefingFactSource).GetMethods().Select(m => m.Name).Order().ShouldBe(["BuildAsync", "NewestPlanDateAsync"]);
    }
}
