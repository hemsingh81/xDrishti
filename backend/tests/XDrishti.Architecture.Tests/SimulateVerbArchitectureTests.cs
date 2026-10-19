using System.Reflection;
using Microsoft.Extensions.Logging;
using NetArchTest.Rules;
using ArchResult = NetArchTest.Rules.TestResult;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The shape of the <c>simulate</c> verb (P3-04b2, criterion 13; ADR 0041): a host step that calls handlers, on the precedent of <c>OwnerBootstrap</c>. Its types are internal to <c>XDrishti.Api</c> and add no public surface; only its host builds the
/// container (so only the host names the Infrastructure project); that host registers no endpoints and no vault-key check; the entry tests the verb first and the older steps through the dispatch, not by looking at the arguments again; and its log
/// ids are the five after the handler's. Each rule has a sentinel, a case the same code must reject.
/// </summary>
public sealed class SimulateVerbArchitectureTests
{
    private const string VerbNamespace = "XDrishti.Api.Simulate";

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static void ShouldHaveNone(ArchResult result) => result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));

    private static Type Verb(string name) => Api.GetType(VerbNamespace + "." + name) ?? throw new InvalidOperationException(name + " is not a type of the verb.");

    [Fact]
    public void The_verb_adds_no_public_type_to_the_api_assembly()
    {
        Api.GetTypes().Count(t => t.Namespace == VerbNamespace).ShouldBeGreaterThan(10, "the scan does see the verb's types");

        Api.GetExportedTypes().Where(t => t.Namespace == VerbNamespace).ShouldBeEmpty();
        Api.GetExportedTypes().ShouldContain(typeof(Program), "the host exports its entry point only, so a public type of the verb would show");
    }

    [Fact]
    public void Only_the_host_of_the_verb_names_the_infrastructure_project_the_entity_framework_or_the_driver()
    {
        string[] persistence = ["XDrishti.Infrastructure", "Microsoft.EntityFrameworkCore", "Npgsql"];

        ShouldHaveNone(Types.InAssembly(Api).That().ResideInNamespace(VerbNamespace).And().DoNotHaveName("SimulateHost").ShouldNot().HaveDependencyOnAny(persistence).GetResult());
        Types.InAssembly(Api).That().ResideInNamespace(VerbNamespace).And().HaveName("SimulateHost").ShouldNot().HaveDependencyOnAny(persistence).GetResult().IsSuccessful.ShouldBeFalse("the host does build the container with AddInfrastructure");
    }

    [Fact]
    public void The_host_of_the_verb_registers_no_endpoints_and_does_not_require_the_vault_key()
    {
        var used = AssemblyScan.MembersUsedBy(Api, Verb("SimulateHost").FullName!);

        used.ShouldNotContain(m => m.Member == "AddApi");
        used.ShouldNotContain(m => m.Member == "RequireBrokerVaultKey");
        used.ShouldNotContain(m => m.Member.Contains("AddOpenApi", StringComparison.Ordinal));
        used.ShouldContain(m => m.Member == "AddApplication");
        used.ShouldContain(m => m.Member == "AddInfrastructure");
        AssemblyScan.MembersUsedBy(Api, "Program").ShouldContain(m => m.Member == "AddApi", "the scan does see AddApi in the API entry");
        AssemblyScan.MembersUsedBy(Api, "Program").ShouldContain(m => m.Member == "RequireBrokerVaultKey");
    }

    [Fact]
    public void The_verb_and_its_script_read_no_vault_key_no_owner_secret_and_no_password_and_the_script_has_no_trace_flag()
    {
        string[] sources = ["Verb.cs", "VerbDispatch.cs", "SimulateCommand.cs", "SimulateHost.cs", "SimulateProgram.cs", "SimulateOptions.cs", "UniverseResolver.cs", "SimulateSummary.cs", "SimulateLog.cs", "SimulatePipeline.cs", "VerifyPackCommand.cs", "VerifyPackOptions.cs"];
        var text = string.Join("\n", sources.Select(s => File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Simulate", s))));

        foreach (var secret in new[] { "BrokerVault", "Auth:Owner", "Auth__Owner", "Database:Password", "Database__Password", "vault-key" })
        {
            text.ShouldNotContain(secret);
        }

        File.ReadAllText(RepositoryFiles.RepositoryFile("scripts", "simulate.sh")).ShouldNotContain("set -x");
        File.ReadAllText(RepositoryFiles.RepositoryFile("scripts", "simulate.sh")).ShouldNotContain("vault-key");
        File.ReadAllText(RepositoryFiles.RepositoryFile("scripts", "dev-api.sh")).ShouldContain("vault-key", Case.Sensitive, "the scan does see a vault key read where there is one");
    }

    [Fact]
    public void The_entry_selects_the_verb_through_the_dispatch_first_and_does_not_look_at_the_arguments_again()
    {
        var program = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Program.cs"));

        program.ShouldNotContain("args.Contains");
        program.IndexOf("VerbDispatch.Of(args)", StringComparison.Ordinal).ShouldBeGreaterThan(-1);
        program.IndexOf("Verb.Simulate", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("Verb.HealthCheck", StringComparison.Ordinal), "the verb is tested before the health probe");
        program.IndexOf("Verb.Simulate", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("Verb.Migrate", StringComparison.Ordinal));
        program.IndexOf("Verb.Simulate", StringComparison.Ordinal).ShouldBeLessThan(program.IndexOf("WebApplication.CreateBuilder", StringComparison.Ordinal), "no web host is built for the verb");
    }

    [Fact]
    public void The_verb_logs_with_the_ids_4405_to_4411_and_4419_to_4423_and_no_other()
    {
        var ids = Verb("SimulateLog").GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .SelectMany(m => m.GetCustomAttributes<LoggerMessageAttribute>().Select(a => a.EventId)).Order().ToList();

        ids.ShouldBe([4405, 4406, 4407, 4408, 4409, 4410, 4411, 4419, 4420, 4421, 4422, 4423]);
    }

    [Fact]
    public void The_verify_pack_verb_pools_and_writes_in_the_host_does_not_use_the_result_store_and_the_extraction_keeps_the_shared_steps_in_one_place()
    {
        var folder = (string name) => File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Simulate", name));
        var pack = folder("VerifyPackCommand.cs") + folder("VerifyPackOptions.cs");

        foreach (var store in new[] { "IRunStore", "StoreRunCommand", "SimulateStore", "--store" })
        {
            pack.ShouldNotContain(store, Case.Sensitive);
        }

        folder("SimulateCommand.cs").ShouldContain("SimulateStore", Case.Sensitive, "the scan does see a use of the store where there is one");
        foreach (var shared in new[] { "TickUnitCheck", "RunSimulationQuery", "BuildTradeCardsQuery" })
        {
            folder("SimulatePipeline.cs").ShouldContain(shared, Case.Sensitive);
            (folder("SimulateCommand.cs") + pack).ShouldNotContain(shared, Case.Sensitive, "the verbs call the shared pipeline and do not fork it");
        }

        Verb("VerifyPackCommand").IsSealed.ShouldBeTrue("a static class: not a handler");
        Verb("VerifyPackCommand").GetInterfaces().ShouldBeEmpty();
    }
}
