using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Criterion 12 of P3-03f (ADR 0044): the store of the charge sheet is a port in Application and an <c>internal sealed</c> adapter in Infrastructure; the handlers of the three actions are internal and sealed; the verb's code is in
/// <c>XDrishti.Api</c> only, internal, and names neither Infrastructure nor the database driver (only the shared host does); the reader extension adds no public type; no rate is written in <c>src/</c>; no project, package or secret was added.
/// Each rule has a sentinel, a case the same code must reject.
/// </summary>
public sealed partial class CostSheetArchitectureTests
{
    private const string VerbNamespace = "XDrishti.Api.CostSheets";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] Persistence = ["XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql"];

    [GeneratedRegex(@"(brokerage_pct|stt_buy_pct|stt_sell_pct|exchange_txn_pct|sebi_per_crore|stamp_buy_pct|gst_pct)\\?""\s*:\s*[0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex RateLiteral();

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    [Fact]
    public void The_store_is_a_port_in_application_and_an_internal_sealed_adapter_in_infrastructure()
    {
        var port = Application.GetType("XDrishti.Application.Abstractions.Research.ICostSheetStore").ShouldNotBeNull();
        var adapter = Infrastructure.GetType("XDrishti.Infrastructure.Research.CostSheetStore").ShouldNotBeNull();

        port.IsInterface.ShouldBeTrue();
        port.IsPublic.ShouldBeTrue();
        adapter.IsNotPublic.ShouldBeTrue();
        adapter.IsSealed.ShouldBeTrue();
        adapter.GetInterfaces().ShouldContain(port);
        Application.GetTypes().ShouldNotContain(t => t.Name == "CostSheetStore", "Application holds the port, never the adapter");
        Types.InAssembly(Application).That().HaveName("RunSimulationHandler").ShouldNot().HaveDependencyOnAny(Persistence).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Infrastructure).That().HaveName("CostSheetStore").ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore").GetResult().IsSuccessful.ShouldBeFalse("the adapter does use the context: the scan sees it");
        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Research.CostSheets").Or().HaveName("RunSimulationHandler").ShouldNot().HaveDependencyOnAny("XDrishti.Infrastructure", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Indicators", "XDrishti.Api")
            .GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_three_handlers_are_internal_and_sealed_and_their_messages_are_public()
    {
        string[] handlers = ["LoadCostSheetHandler", "ShowCostSheetHandler", "CheckCostSheetHandler"];

        foreach (var name in handlers)
        {
            var type = Application.GetType("XDrishti.Application.Research.CostSheets." + name).ShouldNotBeNull();
            (type.IsNotPublic && type.IsSealed).ShouldBeTrue(name + " is internal sealed");
        }

        ShouldHaveNone(Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Research.CostSheets").And().HaveNameEndingWith("Handler").Should().NotBePublic().And().BeSealed().GetResult());
        Types.InAssembly(Application).That().HaveName("CostChoice").Should().NotBePublic().GetResult().IsSuccessful.ShouldBeFalse("the cost choice is public: the request names it");
    }

    [Fact]
    public void The_verb_is_in_the_api_only_internal_and_adds_no_public_type()
    {
        Api.GetTypes().Count(t => t.Namespace == VerbNamespace).ShouldBeGreaterThanOrEqualTo(4, "the scan does see the verb's types");

        Api.GetExportedTypes().Where(t => t.Namespace == VerbNamespace).ShouldBeEmpty();
        foreach (var other in new[] { Application, Infrastructure })
        {
            other.GetTypes().Where(t => t.Name is "CostSheetCommand" or "CostSheetProgram" or "CostSheetOptions" or "CostSheetLog" or "CostCheckText").ShouldBeEmpty("the verb is host code");
        }
    }

    [Fact]
    public void The_verb_names_neither_infrastructure_nor_the_driver_and_the_shared_host_does()
    {
        ShouldHaveNone(Types.InAssembly(Api).That().ResideInNamespace(VerbNamespace).ShouldNot().HaveDependencyOnAny(Persistence).GetResult());
        Types.InAssembly(Api).That().ResideInNamespace("XDrishti.Api.Simulate").And().HaveName("SimulateHost").ShouldNot().HaveDependencyOnAny(Persistence).GetResult().IsSuccessful.ShouldBeFalse("the shared host builds the container");
    }

    [Fact]
    public void The_entry_tests_the_verb_through_the_dispatch_before_the_older_steps_and_before_any_web_host()
    {
        var program = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Program.cs"));

        program.IndexOf("Verb.CostSheet", StringComparison.Ordinal).ShouldBeGreaterThan(-1);
        program.IndexOf("Verb.CostSheet", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("Verb.HealthCheck", StringComparison.Ordinal));
        program.IndexOf("Verb.CostSheet", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("Verb.Migrate", StringComparison.Ordinal));
        program.IndexOf("Verb.CostSheet", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("Verb.OwnerReset", StringComparison.Ordinal));
        program.IndexOf("Verb.CostSheet", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal));
        program.IndexOf("Verb.Simulate", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("Verb.HealthCheck", StringComparison.Ordinal), "the older verb keeps its place: the sentinel for the order check");
    }

    [Fact]
    public void The_verb_logs_with_the_ids_4412_to_4414_and_no_other()
    {
        var ids = Api.GetType(VerbNamespace + ".CostSheetLog").ShouldNotBeNull()
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId)).Order().ToList();

        ids.ShouldBe([4412, 4413, 4414]);
    }

    [Fact]
    public void The_reader_extension_adds_no_public_type_and_the_row_stays_internal()
    {
        Application.GetType("XDrishti.Application.Portfolio.Reports.GtrLines").ShouldNotBeNull().IsNotPublic.ShouldBeTrue();
        Application.GetType("XDrishti.Application.Portfolio.Reports.GtrRow").ShouldNotBeNull().IsNotPublic.ShouldBeTrue();
        Application.GetExportedTypes().Where(t => t.Name.StartsWith("Gtr", StringComparison.Ordinal)).ShouldBeEmpty();
        Application.GetExportedTypes().ShouldContain(t => t.Name == "CostCheckReport", "the scan does see the public check types (the report carries no identifier)");
    }

    [Fact]
    public void No_rate_is_written_in_the_source_and_the_rule_does_see_one()
    {
        var root = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!, "src");
        var offenders = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && Path.GetExtension(f) is ".cs" or ".json" or ".sql" or ".csv" or ".txt")
            .Where(f => RateLiteral().IsMatch(File.ReadAllText(f)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty("a charge sheet is the owner's data in the database, never a file or a literal of src/");
        RateLiteral().IsMatch("{\"brokerage_pct\":0.03,").ShouldBeTrue();
        RateLiteral().IsMatch("\\\"gst_pct\\\": 18").ShouldBeTrue();
        RateLiteral().IsMatch("\"stt_sell_pct\":\"x\"").ShouldBeFalse();
    }

    [Fact]
    public void The_fixture_sheet_is_in_the_test_projects_only_and_the_script_holds_no_secret()
    {
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "tests", "Shared", "FixtureSheet.cs")).ShouldContain("fixture-a");
        Application.GetTypes().Concat(Infrastructure.GetTypes()).Concat(Api.GetTypes()).Where(t => t.Name is "FixtureSheet" or "TestCostSheet").ShouldBeEmpty();
        var script = File.ReadAllText(RepositoryFiles.RepositoryFile("scripts", "cost-sheet.sh"));
        script.ShouldNotContain("vault-key");
        script.ShouldNotContain("set -x");
        File.ReadAllText(RepositoryFiles.RepositoryFile("scripts", "dev-api.sh")).ShouldContain("vault-key", Case.Sensitive, "the scan does see a vault key read where there is one");
    }
}
