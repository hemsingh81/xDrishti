using System.Reflection;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-02 AC 12 (ADR 0137): the tool server has no write code. <c>XDrishti.Mcp</c> is a host that reaches the application only through the read ports of <c>Application.Abstractions.Ai</c>, the pure types of <c>Domain.Assistant</c> and the one registration of
/// <c>Infrastructure.Ai.ReadOnly</c>: no command handler, no <c>DbContext</c>, no store, no module of the infrastructure, no driver. The rule is proved to fail on a planted dependency.
/// </summary>
public sealed class McpArchitectureTests
{
    private static readonly Assembly Mcp = Assembly.Load("XDrishti.Mcp");
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    /// <summary>Every namespace of the layers that a tool-server type may NOT use; the three allowed ones are the exceptions.</summary>
    private static readonly string[] Allowed =
    [
        "XDrishti.Application.Abstractions.Ai",
        "XDrishti.Domain.Assistant",
        "XDrishti.Infrastructure.Ai.ReadOnly",
        "XDrishti.Hosting",
        "XDrishti.Mcp",
    ];

    /// <summary>
    /// What a tool-server type may not depend on. The dependency check matches by prefix, so a namespace that is the parent of an allowed one (<c>XDrishti.Application</c>, <c>XDrishti.Infrastructure</c>) cannot be listed whole: its own types are listed by name instead.
    /// </summary>
    private static string[] Forbidden
    {
        get
        {
            var types = Application.GetTypes().Concat(Infrastructure.GetTypes()).Concat(Domain.GetTypes()).Where(t => !t.IsNested && t.Namespace is not null && t.Namespace.StartsWith("XDrishti.", StringComparison.Ordinal)).ToList();
            bool IsAllowedOrParent(string ns) => Allowed.Any(a => a.StartsWith(ns, StringComparison.Ordinal));
            var namespaces = types.Select(t => t.Namespace!).Distinct(StringComparer.Ordinal).Where(ns => !IsAllowedOrParent(ns)).Order(StringComparer.Ordinal);
            var parentTypes = types.Where(t => IsAllowedOrParent(t.Namespace!) && !Allowed.Contains(t.Namespace!, StringComparer.Ordinal)).Select(t => t.FullName!);
            return [.. namespaces, .. parentTypes];
        }
    }

    [Fact]
    public void The_tool_server_uses_nothing_of_the_application_the_infrastructure_or_the_domain_but_the_read_ports_and_the_pure_assistant_types()
    {
        var forbidden = Forbidden;
        forbidden.ShouldContain("XDrishti.Infrastructure.Persistence");
        forbidden.ShouldContain("XDrishti.Application.Abstractions.Messaging");

        var result = Types.InAssembly(Mcp).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        // On a failure, name the dependency of each offending type (one entry at a time), not only the type.
        var culprits = result.IsSuccessful ? [] : forbidden.Where(f => !Types.InAssembly(Mcp).ShouldNot().HaveDependencyOn(f).GetResult().IsSuccessful).ToList();
        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []) + " on " + string.Join(", ", culprits));
    }

    [Fact]
    public void The_scan_sees_a_planted_dependency_on_a_command_handler_a_store_or_the_context()
    {
        // Prove the rule has teeth: an assembly of this test project that touches the persistence namespace is reported by the same call.
        var result = Types.InAssembly(typeof(McpArchitectureTests).Assembly).That().HaveNameStartingWith("PlantedWriter").ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(n => n.Contains("PlantedWriter", StringComparison.Ordinal));
    }

    /// <summary>Test-only type that depends on the runtime role's context; never shipped.</summary>
    internal sealed class PlantedWriter(XDrishti.Infrastructure.Persistence.XDrishtiDbContext context)
    {
        public object Context => context;
    }

    [Fact]
    public void The_tool_server_references_no_database_driver_or_entity_framework_directly()
    {
        var references = Mcp.GetReferencedAssemblies().Select(a => a.Name!).ToList();

        references.ShouldNotContain(n => n.Contains("Npgsql", StringComparison.Ordinal) || n.Contains("EntityFrameworkCore", StringComparison.Ordinal));
        references.ShouldContain("XDrishti.Infrastructure");
        references.ShouldNotContain("XDrishti.Simulation");
        references.ShouldNotContain("XDrishti.Learning.MlNet");
    }

    [Fact]
    public void No_type_or_member_of_the_tool_server_is_named_for_placing_modifying_or_cancelling_an_order()
    {
        NoOrderPlacementArchitectureTests.Violations(Mcp).ShouldBeEmpty();
    }

    [Fact]
    public void Each_tool_takes_only_read_ports_in_its_constructor()
    {
        var tools = Mcp.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.BaseType?.Name == "ReadOnlyTool").ToList();
        tools.Select(t => t.Name).Order().ShouldBe(["DescribeTableTool", "LabValidateTool", "ListTablesTool", "RunSqlTool"]);

        var allowed = new[] { "ToolRunner", "IAiCatalog", "IAiSqlRunner", "IAiLabValidator", "IOptions`1" };
        foreach (var tool in tools)
        {
            var parameters = tool.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single().GetParameters();
            parameters.Select(p => p.ParameterType.Name).ShouldAllBe(n => allowed.Contains(n), tool.Name);
        }

        // The read ports have only read methods: no member of the three ports is a write.
        foreach (var port in new[] { typeof(XDrishti.Application.Abstractions.Ai.IAiCatalog), typeof(XDrishti.Application.Abstractions.Ai.IAiSqlRunner), typeof(XDrishti.Application.Abstractions.Ai.IAiContext), typeof(XDrishti.Application.Abstractions.Ai.IAiLabValidator) })
        {
            var readMethods = new[] { "ListAsync", "DescribeAsync", "RunAsync", "GetAsync", "Validate" };
            port.GetMethods().Select(m => m.Name).ShouldAllBe(n => readMethods.Contains(n), port.Name);
        }
    }

    [Fact]
    public void Only_the_read_registration_of_the_assistant_infrastructure_is_public()
    {
        var infrastructure = Infrastructure.GetTypes().Where(t => t.IsPublic && t.Namespace == "XDrishti.Infrastructure.Ai.ReadOnly").Select(t => t.Name).Order().ToList();

        infrastructure.ShouldBe(["AiReadOnlyRegistration", "AiRoleProvisioner", "AiRolesOptions", "ReadOnlyDatabaseOptions"]);
    }
}
