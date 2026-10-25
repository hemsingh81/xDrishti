using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Reporting;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// The report read endpoints (P8-06, ADR 0100) are read only and cannot be made to print net figures by a caller: the module maps GET and nothing else; the port the reports read through names no database type; the stored confirmation of the charge sheet
/// is read by two Application types and by nothing in the Api; the handlers are internal, sealed and registered; the one Infrastructure source is internal and implements the port. Each scan has a sentinel, a case the same code must catch.
/// </summary>
public sealed partial class ReportEndpointsArchitectureTests
{
    private const string PortNamespace = "XDrishti.Application.Abstractions.Reporting";

    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    private static readonly Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] Forbidden = ["Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Infrastructure"];

    private static readonly string ConfirmationSource = typeof(IChargeSheetConfirmationSource).FullName!;

    [GeneratedRegex(@"\.Map(Post|Put|Delete|Patch)\b|\.MapMethods\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex WritingRoute();

    [GeneratedRegex(@"\.MapGet\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 500)]
    private static partial Regex ReadingRoute();

    private static string EndpointsSource => File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "ReportsEndpoints.cs"));

    private static string Code(string source) => string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void The_report_endpoints_map_get_and_nothing_else_and_use_no_command_handler()
    {
        var code = Code(EndpointsSource);

        WritingRoute().IsMatch(code).ShouldBeFalse();
        ReadingRoute().Count(code).ShouldBe(10);
        code.ShouldNotContain("ICommandHandler");
        WritingRoute().IsMatch("reports.MapPost(\"/x\", Handler)").ShouldBeTrue("the scan does see a write route");
        WritingRoute().IsMatch("reports.MapDelete(\"/x\", Handler)").ShouldBeTrue();
        WritingRoute().IsMatch("reports.MapGet(\"/x\", Handler)").ShouldBeFalse("a read is not a write");
    }

    [Fact]
    public void The_port_the_reports_read_through_names_no_database_or_infrastructure_type()
    {
        var types = Types.InAssembly(Application).That().ResideInNamespace(PortNamespace).GetTypes().Select(t => t.Name).ToList();

        types.ShouldContain("IReportTradeSource");
        types.ShouldContain("IChargeSheetConfirmationSource");
        Types.InAssembly(Application).That().ResideInNamespace(PortNamespace).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(typeof(NpgsqlReportSentinel).Assembly).That().HaveName(nameof(NpgsqlReportSentinel)).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult().IsSuccessful.ShouldBeFalse("the scan sees a database type");
    }

    [Fact]
    public void The_stored_confirmation_is_read_by_the_loader_and_the_runs_list_and_by_nothing_in_the_api()
    {
        var readers = Types.InAssembly(Application).That().HaveDependencyOn(ConfirmationSource).GetTypes().Select(t => t.Name).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();

        readers.ShouldBe(["ListReportRunsHandler", "ReportLoader"]);
        Types.InAssembly(Api).That().HaveDependencyOn(ConfirmationSource).GetTypes().ShouldBeEmpty();
        Types.InAssembly(Api).That().HaveDependencyOn(typeof(XDrishti.Domain.Reporting.ChargeSheetConfirmation).FullName!).GetTypes().ShouldBeEmpty();
        Types.InAssembly(typeof(ConfirmationReaderSentinel).Assembly).That().HaveDependencyOn(ConfirmationSource).GetTypes().Select(t => t.Name).ShouldContain(nameof(ConfirmationReaderSentinel));
    }

    [Fact]
    public void The_report_handlers_are_internal_sealed_and_registered_and_the_source_is_internal_and_implements_the_port()
    {
        foreach (var name in new[] { "ListReportRunsHandler", "GetReportSummaryHandler", "GetReportGroupsHandler", "GetReportPivotHandler", "GetReportEquityHandler", "GetReportDistributionHandler", "GetReportInsightsHandler", "GetReportWhatWorksHandler", "PageReportTradesHandler", "ReportLoader" })
        {
            var type = Application.GetType("XDrishti.Application.Reporting." + name);

            type.ShouldNotBeNull(name);
            type.IsPublic.ShouldBeFalse(name + " is internal");
            type.IsSealed.ShouldBeTrue(name + " is sealed");
        }

        var registration = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "DependencyInjection.cs"));
        foreach (var name in new[] { "ListReportRunsHandler", "GetReportSummaryHandler", "GetReportGroupsHandler", "GetReportPivotHandler", "GetReportEquityHandler", "GetReportDistributionHandler", "GetReportInsightsHandler", "GetReportWhatWorksHandler", "PageReportTradesHandler" })
        {
            registration.ShouldContain(name + ">()");
        }

        registration.ShouldContain("AddSingleton<ReportGate>", Case.Sensitive, "one gate for the whole process: the loader is scoped, the memory is not");
        Application.GetType("XDrishti.Application.Reporting.ReportGate")!.IsSealed.ShouldBeTrue();

        var source = Infrastructure.GetType("XDrishti.Infrastructure.Research.ReportTradeSource");
        source.ShouldNotBeNull();
        source.IsPublic.ShouldBeFalse();
        source.IsSealed.ShouldBeTrue();
        source.GetInterfaces().ShouldContain(typeof(IReportTradeSource));
        Infrastructure.GetType("XDrishti.Infrastructure.Reporting.ChargeSheetConfirmationStore")!.GetInterfaces().ShouldContain(typeof(IChargeSheetConfirmationSource));
    }

    [Fact]
    public void The_report_source_never_writes()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Research", "ReportTradeSource.cs"));
        var confirmation = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Reporting", "ChargeSheetConfirmationStore.cs"));

        foreach (var text in new[] { Code(source), Code(confirmation) })
        {
            Regex.IsMatch(text, @"\b(INSERT|UPDATE|DELETE|TRUNCATE|COPY)\b\s", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBeFalse();
            text.ShouldNotContain("SaveChanges");
            text.ShouldNotContain("ExecuteNonQuery");
        }
    }
}

/// <summary>Names a database type on purpose, to prove the port rule fails on one (nothing else uses it).</summary>
internal sealed class NpgsqlReportSentinel
{
    public Npgsql.NpgsqlConnection? Connection { get; init; }
}

/// <summary>Reads the stored confirmation on purpose, to prove the reader scan sees such a use (nothing else uses it).</summary>
internal sealed class ConfirmationReaderSentinel(IChargeSheetConfirmationSource source)
{
    public IChargeSheetConfirmationSource Source { get; } = source;
}
