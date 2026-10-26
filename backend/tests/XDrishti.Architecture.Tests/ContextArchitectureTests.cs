using System.Reflection;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// P3-01d: the market context is derived in Domain (pure), run by internal sealed handlers in Application, stored and read through ports, and the one place that names the indicator library's <c>MarketContext</c> is the Infrastructure adapter
/// (ADR 0039: Application names no engine type). The verb is host code in Api, internal, and names neither Infrastructure nor the driver. The broker allow-list is not touched by this slice (ADR 0133).
/// </summary>
public sealed class ContextArchitectureTests
{
    private const string VerbNamespace = "XDrishti.Api.Context";

    private static readonly Assembly Domain = typeof(XDrishti.Domain.Market.Context.ContextDerivation).Assembly;

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void Only_the_adapter_in_infrastructure_names_MarketContext()
    {
        var users = Types.InAssembly(Infrastructure).That().HaveDependencyOn("XDrishti.Indicators.MarketContext").GetTypes().Select(t => t.FullName!).ToList();

        users.ShouldNotBeEmpty("the scan does see the adapter");
        users.ShouldAllBe(name => name.StartsWith("XDrishti.Infrastructure.Market.Context.ContextFiguresAdapter", StringComparison.Ordinal));
        foreach (var other in new[] { Domain, Application, Api })
        {
            Types.InAssembly(other).That().HaveDependencyOn("XDrishti.Indicators.MarketContext").GetTypes().ShouldBeEmpty(other.GetName().Name);
        }
    }

    [Fact]
    public void Application_and_domain_name_no_type_of_the_indicator_library_and_the_derivation_is_pure()
    {
        ShouldHaveNone(Types.InAssembly(Application).ShouldNot().HaveDependencyOn("XDrishti.Indicators").GetResult());
        ShouldHaveNone(Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Market.Context").ShouldNot()
            .HaveDependencyOnAny("XDrishti.Indicators", "XDrishti.Application", "XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "Npgsql", "System.Net.Http").GetResult());
        Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Market.Context").GetTypes().ShouldNotBeEmpty();
    }

    [Fact]
    public void The_two_handlers_are_internal_and_sealed_the_ports_are_public_interfaces_and_the_adapters_are_internal_sealed()
    {
        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Market.Context").And().HaveNameEndingWith("Handler").Should().NotBePublic().And().BeSealed().GetResult());
        Application.GetTypes().Count(t => t.Namespace == "XDrishti.Application.Market.Context" && t.Name.EndsWith("Handler", StringComparison.Ordinal)).ShouldBe(2);
        foreach (var name in new[] { "IContextBarSource", "IContextDayStore", "IContextFigures", "IContextSettings" })
        {
            var port = Application.GetType("XDrishti.Application.Abstractions.Market.Context." + name).ShouldNotBeNull();
            (port.IsInterface && port.IsPublic).ShouldBeTrue(name);
        }

        foreach (var name in new[] { "ContextBarSource", "ContextDayStore", "ContextFiguresAdapter", "ContextSettings" })
        {
            var adapter = Infrastructure.GetType("XDrishti.Infrastructure.Market.Context." + name).ShouldNotBeNull();
            (adapter.IsNotPublic && adapter.IsSealed).ShouldBeTrue(name);
        }
    }

    [Fact]
    public void The_verb_is_host_code_in_the_api_internal_and_names_neither_infrastructure_nor_the_driver()
    {
        Api.GetTypes().Count(t => t.Namespace == VerbNamespace).ShouldBeGreaterThanOrEqualTo(4);
        Api.GetExportedTypes().Where(t => t.Namespace == VerbNamespace).ShouldBeEmpty();
        ShouldHaveNone(Types.InAssembly(Api).That().ResideInNamespace(VerbNamespace).ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql").GetResult());
    }

    [Fact]
    public void The_entry_tests_the_verb_before_the_older_steps_and_before_any_web_host()
    {
        var program = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Program.cs"));

        var at = program.IndexOf("Verb.Context", StringComparison.Ordinal);
        at.ShouldBeGreaterThan(-1);
        at.ShouldBeLessThan(program.IndexOf("Verb.HealthCheck", StringComparison.Ordinal));
        at.ShouldBeLessThan(program.IndexOf("Verb.Migrate", StringComparison.Ordinal));
        at.ShouldBeLessThan(program.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal));
    }

    [Fact]
    public void The_slice_does_not_touch_the_broker_allow_list_it_reads_the_chart_endpoint_the_data_job_already_calls()
    {
        var allowed = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Brokers", "BrokerEndpoints.cs"));

        allowed.ShouldNotContain("charts/historical", Case.Sensitive, "the daily-history endpoint is fallback C: an owner decision and a separate slice (ADR 0133)");
        var folder = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Market", "Context", "ContextBarSource.cs"))!;
        Directory.GetFiles(folder, "*.cs").Select(File.ReadAllText).ShouldAllBe(text => !text.Contains("HttpClient", StringComparison.Ordinal));
    }
}
