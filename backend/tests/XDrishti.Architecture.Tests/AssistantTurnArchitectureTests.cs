using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-03 AC 13 (ADR 0138): the assistant stays apart. <c>Domain/Assistant</c> is pure (no I/O, no driver, no model or tool library); the hosted loops of the worker and the feed reference nothing of the assistant; the model is reached only through <see cref="IChatModel"/>, which only the turn
/// service uses and only <c>Infrastructure/Ai</c> implements; only <c>Infrastructure/Ai</c> references the tool-server client library. (The nightly and the services of the platform are covered by the rules of P9-01: no other module references an assistant namespace.)
/// </summary>
public sealed class AssistantTurnArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly string[] AiNamespaces = ["XDrishti.Application.Ai", "XDrishti.Application.Abstractions.Ai", "XDrishti.Infrastructure.Ai", "XDrishti.Domain.Assistant.Grounding"];

    [Fact]
    public void The_assistant_domain_is_pure()
    {
        var result = Types.InAssembly(Domain).That().ResideInNamespaceStartingWith("XDrishti.Domain.Assistant")
            .ShouldNot().HaveDependencyOnAny("System.Net.Http", "System.Net.Sockets", "System.IO.File", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore", "ModelContextProtocol", "OpenAI", "Microsoft.Extensions.AI", "Microsoft.Extensions.Logging", "Microsoft.AspNetCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("XDrishti.Worker")]
    [InlineData("XDrishti.Feed")]
    public void The_hosts_of_the_nightly_and_the_feed_reference_nothing_of_the_assistant(string host)
    {
        var assembly = Assembly.Load(new AssemblyName(host));

        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(AiNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue($"{host}: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_model_is_reached_only_through_the_turn_service()
    {
        var users = Types.InAssembly(Application).That().HaveDependencyOn(typeof(IChatModel).FullName!).GetTypes().Select(t => t.FullName!).Where(n => !n.Contains("+<", StringComparison.Ordinal)).ToList();

        users.ShouldAllBe(n => n.StartsWith("XDrishti.Application.Ai.AssistantTurnService", StringComparison.Ordinal) || n.StartsWith("XDrishti.Application.DependencyInjection", StringComparison.Ordinal) || n.StartsWith("XDrishti.Application.Ai.EvalTurnRunnerFactory", StringComparison.Ordinal)); // the factory only hands the port to the same turn service (the eval turn, P9-09)
        var implementers = Types.InAssembly(Infrastructure).That().ImplementInterface(typeof(IChatModel)).GetTypes().Select(t => t.Namespace).Distinct().ToList();
        implementers.ShouldBe(["XDrishti.Infrastructure.Ai.Assistant"]);
    }

    [Fact]
    public void The_model_and_tool_libraries_are_referenced_by_the_assistant_infrastructure_alone()
    {
        foreach (var assembly in new[] { Domain, Application })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny("ModelContextProtocol", "OpenAI", "Microsoft.Extensions.AI").GetResult();

            result.IsSuccessful.ShouldBeTrue($"{assembly.GetName().Name}: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_turn_and_its_ports_have_no_write_to_anything_but_the_audit()
    {
        // The audit port writes only the rows of the turn; the catalog port only lists and calls; there is no port that changes a setting, a plan, a run or a secret for the turn.
        typeof(IConversationStore).GetMethods().Select(m => m.Name).Order().ShouldBe(["CompleteTurnAsync", "ExistsAsync", "RecentAsync", "RecordToolCallAsync", "StartTurnAsync"]);
        typeof(IToolCatalog).GetMethods().Select(m => m.Name).Order().ShouldBe(["CallAsync", "ListAsync"]);
        typeof(IConversationReader).GetMethods().Select(m => m.Name).Order().ShouldBe(["GetAsync", "GetToolCallAsync", "ListAsync"]);
    }
}
