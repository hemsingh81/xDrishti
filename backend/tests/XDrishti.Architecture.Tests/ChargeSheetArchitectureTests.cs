using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.ChargeSheet;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.ChargeSheet;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The charge-sheet confirmation writer (spec P8-23, ADR 0127) is the owner's act and nothing else: one Application handler depends on the write port, one Api endpoint module calls that handler and asks for a fresh password first, no job, service or rule calls it, the
/// read handlers have no write port, the log is insert-only for the runtime role and the confirmation the reports read is the one the owner wrote. Each scan has a sentinel, a case the same code must catch.
/// </summary>
public sealed partial class ChargeSheetArchitectureTests
{
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly Assembly Worker = Assembly.Load(new AssemblyName("XDrishti.Worker"));

    private static readonly string Writer = typeof(IChargeSheetLogWriter).FullName!;

    private static readonly string Command = typeof(RecordChargeSheetActCommand).FullName!;

    [GeneratedRegex(@"\b(UPDATE|DELETE|TRUNCATE)\b\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex Rewriting();

    [GeneratedRegex(@"\b(INSERT|UPDATE|DELETE|TRUNCATE|COPY)\b\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex AnyWrite();

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", .. parts]));

    private static List<string> Names(IEnumerable<Type> types) => [.. types.Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal)];

    [Fact]
    public void Only_the_owner_handler_depends_on_the_write_port_in_the_application()
    {
        Names(Types.InAssembly(Application).That().HaveDependencyOn(Writer).GetTypes()).ShouldBe(["RecordChargeSheetActHandler"]);
        Names(Types.InAssembly(typeof(WriterSentinel).Assembly).That().HaveDependencyOn(Writer).GetTypes()).ShouldContain(nameof(WriterSentinel), "the scan sees a use of the write port");
    }

    [Fact]
    public void Only_the_owner_endpoint_module_names_the_act_command_and_nothing_in_the_worker_or_the_infrastructure_does()
    {
        Names(Types.InAssembly(Api).That().HaveDependencyOn(Command).GetTypes()).ShouldBe(["ChargeSheetEndpoints"]);
        Names(Types.InAssembly(Application).That().HaveDependencyOn(Command).GetTypes()).ShouldBe(["DependencyInjection", "RecordChargeSheetActCommand", "RecordChargeSheetActHandler"]);
        Types.InAssembly(Infrastructure).That().HaveDependencyOn(Command).GetTypes().ShouldBeEmpty();
        Types.InAssembly(Worker).That().HaveDependencyOn(Command).GetTypes().ShouldBeEmpty();
        Names(Types.InAssembly(typeof(WriterSentinel).Assembly).That().HaveDependencyOn(Command).GetTypes()).ShouldContain(nameof(WriterSentinel));
    }

    [Fact]
    public void No_read_handler_and_no_reader_of_the_confirmation_has_a_write_port()
    {
        foreach (var name in new[] { "GetChargeSheetStatusHandler", "ChargeSheetStatusBuilder" })
        {
            Types.InAssembly(Application).That().HaveName(name).ShouldNot().HaveDependencyOn(Writer).GetResult().IsSuccessful.ShouldBeTrue(name);
        }

        foreach (var name in new[] { "ChargeSheetConfirmationStore", "StoredCostConfirmation" })
        {
            Types.InAssembly(Infrastructure).That().HaveName(name).ShouldNot().HaveDependencyOn(Writer).GetResult().IsSuccessful.ShouldBeTrue(name);
        }

        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Reporting").ShouldNot().HaveDependencyOn(Writer).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Paper").ShouldNot().HaveDependencyOn(Writer).GetResult().IsSuccessful.ShouldBeTrue();
        typeof(IChargeSheetLog).GetMethods().Select(m => m.Name).ShouldBe(["ReadAsync"]);
        typeof(IChargeSheetLogWriter).GetMethods().Select(m => m.Name).ShouldBe(["AppendAsync"]);
    }

    [Fact]
    public void The_endpoint_module_checks_a_fresh_password_in_every_write_and_limits_the_attempts_and_the_only_writes_of_charge_sheets_are_there()
    {
        var text = Source("XDrishti.Api", "Endpoints", "ChargeSheetEndpoints.cs");

        Regex.Count(text, @"\.MapGet\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(1);
        Regex.Count(text, @"\.Map(Post|Put)\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(3);
        Regex.Count(text, @"\.MapDelete\(|\.MapPatch\(|\.MapMethods\(", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(0);
        Regex.Count(text, @"RequireRateLimiting\(SessionAuthentication\.LoginRateLimitPolicy\)", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(3);
        text.ShouldContain("OwnerAct.AuthorizeAsync(");
        text.ShouldNotContain("Elevate");
        text.ShouldNotContain("StepUpWindow");
        Source("XDrishti.Api", "Endpoints", "OwnerAct.cs").ShouldContain("Elevate: false");

        var others = Directory.GetFiles(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "ChargeSheetEndpoints.cs"))!, "*.cs").Where(f => !f.EndsWith("ChargeSheetEndpoints.cs", StringComparison.Ordinal));
        others.Where(f => File.ReadAllText(f).Contains("/charge-sheet", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Fact]
    public void The_owner_is_the_signed_in_session_never_a_field_of_a_body()
    {
        foreach (var type in new[] { "ConfirmChargeSheetRequest", "RevokeChargeSheetRequest", "ChangeChargeSheetCapabilityRequest" })
        {
            var request = Api.GetType("XDrishti.Api.Endpoints." + type)!;

            request.GetProperties().Select(p => p.Name).ShouldNotContain("Actor");
            request.GetProperties().Select(p => p.Name).ShouldNotContain("Owner");
            request.GetProperties().ShouldContain(p => p.Name == "Password");
        }
    }

    [Fact]
    public void The_log_is_written_with_insert_statements_only_and_the_readers_write_nothing()
    {
        var store = Source("XDrishti.Infrastructure", "Reporting", "ChargeSheetEventStore.cs");

        Rewriting().IsMatch(store).ShouldBeFalse();
        store.ShouldContain("INSERT INTO workflow.charge_sheet_events");
        foreach (var file in new[] { "ChargeSheetConfirmationStore.cs", "StoredCostConfirmation.cs" })
        {
            var text = Source("XDrishti.Infrastructure", "Reporting", file);
            AnyWrite().IsMatch(text).ShouldBeFalse(file);
            text.ShouldNotContain("SaveChanges");
        }

        Rewriting().IsMatch("UPDATE workflow.charge_sheet_events SET kind = 'x'").ShouldBeTrue("the scan sees an update");
        Rewriting().IsMatch("DELETE FROM workflow.charge_sheet_events").ShouldBeTrue("the scan sees a delete");
    }

    [Fact]
    public void The_table_is_granted_on_its_own_and_made_append_only_and_the_schema_is_not_regranted()
    {
        var migration = Directory.GetFiles(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Persistence", "Migrations", "XDrishtiDbContextModelSnapshot.cs"))!, "*AddChargeSheetEvents.cs").Single();
        var text = File.ReadAllText(migration);

        text.ShouldContain("GrantTableToAppRole(Schemas.Workflow, \"charge_sheet_events\")");
        text.ShouldContain("MakeAppendOnlyForAppRole(Schemas.Workflow, \"charge_sheet_events\")");
        text.ShouldNotContain("GrantSchemaUsageToAppRole");
        text.ShouldContain("DROP TRIGGER");
        text.ShouldContain("DROP FUNCTION");
    }

    [Fact]
    public void The_confirmation_the_reports_read_and_the_fact_the_paper_comparison_reads_are_the_stored_owner_confirmation()
    {
        var infrastructure = Names(Types.InAssembly(Infrastructure).That().HaveDependencyOn(typeof(IChargeSheetConfirmationSource).FullName!).GetTypes());

        infrastructure.ShouldBe(["AiReadOnlyRegistration", "ChargeSheetConfirmationStore", "DependencyInjection", "NoChargeSheetConfirmation", "StoredCostConfirmation"]); // the registrations name the port; the store and the cost fact read the stored confirmation, the tool server's class is always absent (P9-05) and reads nothing
        Source("XDrishti.Infrastructure", "DependencyInjection.cs").ShouldContain("ServiceDescriptor.Scoped<ICostConfirmationFact, StoredCostConfirmation>()");
        Source("XDrishti.Application", "DependencyInjection.cs").ShouldContain("TryAddSingleton<ICostConfirmationFact, NotConfirmed>()");
        Source("XDrishti.Domain", "Platform", "AppSettingKeys.cs").ShouldNotContain("charge_sheet_confirmation");
    }
}

/// <summary>Uses the write port and the act command on purpose, to prove the scans see such uses (nothing else uses it).</summary>
internal sealed class WriterSentinel(IChargeSheetLogWriter writer)
{
    public IChargeSheetLogWriter Writer { get; } = writer;

    public RecordChargeSheetActCommand? Command { get; init; }
}
