using System.Reflection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec P9-01 AC 8 (ADR 0136): the nightly plan, the API's health and every hosted service work whether or not a model server exists. No health check, hosted service or code of another module references the assistant's types; the OpenAI packages are used by
/// <c>Infrastructure/Ai</c> alone. The status is asked on request only, never from start-up code.
/// </summary>
public sealed class AssistantLlmArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;
    private static readonly string[] AiNamespaces = ["XDrishti.Application.Ai", "XDrishti.Application.Abstractions.Ai", "XDrishti.Infrastructure.Ai"];
    private static readonly string[] Shipped = ["XDrishti.Domain", "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Hosting", "XDrishti.Api", "XDrishti.Worker", "XDrishti.Feed"];
    private static readonly string[] ClientLibraries = ["OpenAI", "System.ClientModel", "Microsoft.Extensions.AI", "ModelContextProtocol"];

    [Fact]
    public void Application_code_of_other_modules_references_no_assistant_type()
    {
        var result = Types.InAssembly(Application).That()
            .DoNotResideInNamespaceStartingWith("XDrishti.Application.Ai").And().DoNotResideInNamespace("XDrishti.Application.Abstractions.Ai").And().DoNotResideInNamespace("XDrishti.Application")
            .ShouldNot().HaveDependencyOnAny(AiNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_code_outside_the_assistant_folder_references_no_assistant_type_except_the_wiring()
    {
        var result = Types.InAssembly(Infrastructure).That()
            .DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.Ai").And().DoNotResideInNamespace("XDrishti.Infrastructure")
            .ShouldNot().HaveDependencyOnAny(AiNamespaces).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void No_health_check_or_hosted_service_in_any_shipped_assembly_references_the_assistant()
    {
        foreach (var name in Shipped)
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            foreach (var contract in new[] { typeof(IHealthCheck), typeof(IHostedService) })
            {
                var result = Types.InAssembly(assembly).That().ImplementInterface(contract).ShouldNot().HaveDependencyOnAny(AiNamespaces).GetResult();

                result.IsSuccessful.ShouldBeTrue($"{name} {contract.Name}: " + string.Join(", ", result.FailingTypeNames ?? []));
            }
        }
    }

    [Fact]
    public void In_the_API_only_the_assistant_endpoints_the_briefing_verb_and_the_wiring_reference_the_assistant()
    {
        var api = Assembly.Load(new AssemblyName("XDrishti.Api"));
        var offenders = Types.InAssembly(api).That().HaveDependencyOnAny(AiNamespaces).GetTypes()
            .Select(t => t.FullName!).Where(n => !n.StartsWith("XDrishti.Api.Endpoints.Assistant", StringComparison.Ordinal) && !n.StartsWith("XDrishti.Api.Cli.AssistantBriefing", StringComparison.Ordinal) && !n.StartsWith("XDrishti.Api.Cli.AssistantDraft", StringComparison.Ordinal) && !n.StartsWith("XDrishti.Api.Infrastructure.ApiJsonContext", StringComparison.Ordinal) && !n.StartsWith("XDrishti.Api.Infrastructure.ApiSetup", StringComparison.Ordinal)).ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_assistant_folder_of_the_infrastructure_references_the_model_client_libraries()
    {
        foreach (var name in Shipped)
        {
            var assembly = Assembly.Load(new AssemblyName(name));
            var result = Types.InAssembly(assembly).That().DoNotResideInNamespaceStartingWith("XDrishti.Infrastructure.Ai").ShouldNot().HaveDependencyOnAny(ClientLibraries).GetResult();

            result.IsSuccessful.ShouldBeTrue($"{name}: " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }
}
