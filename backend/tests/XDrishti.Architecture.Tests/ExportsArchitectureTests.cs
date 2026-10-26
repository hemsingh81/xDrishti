using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Saving a table as a file (spec P8-20, ADR 0131): the structural proof that nothing about an account can reach a file and that data leaves only as the bytes of one response. The code that makes a file (namespace <c>XDrishti.Api.Exports</c>) reads only the reporting views and the
/// writer of SimExport, none of the account, portfolio, plan, auth, live or broker namespaces, no file, network or process; the mapper reads nothing else than the views; no column is named like something that belongs to an account; one module has the route, and it asks the
/// capability first and records the audit row before the file is given. Each scan has a sentinel.
/// </summary>
public sealed class ExportsArchitectureTests
{
    private const string ExportsNamespace = "XDrishti.Api.Exports";

    private static readonly Assembly Api = typeof(Program).Assembly;

    private static readonly string[] Forbidden =
    [
        "XDrishti.Application.Accounts", "XDrishti.Domain.Accounts", "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Portfolio", "XDrishti.Domain.Portfolio", "XDrishti.Application.Abstractions.Portfolio", "XDrishti.Application.Planning",
        "XDrishti.Domain.Planning", "XDrishti.Application.Abstractions.Planning", "XDrishti.Domain.Allocation", "XDrishti.Application.Abstractions.Platform", "XDrishti.Application.Auth", "XDrishti.Domain.Auth", "XDrishti.Application.Abstractions.Auth", "XDrishti.Application.Live",
        "XDrishti.Application.Abstractions.Live", "XDrishti.Application.Paper", "XDrishti.Domain.Paper", "XDrishti.Application.Abstractions.Paper", "XDrishti.Application.Autonomy", "XDrishti.Application.Abstractions.Autonomy", "XDrishti.Infrastructure", "XDrishti.Application.Audit",
        "System.IO", "System.Net", "System.Diagnostics.Process", "Microsoft.AspNetCore",
    ];

    private static readonly Regex AccountColumn = new(@"account|client|token|email|balance|holding|quantity_held|password|secret", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly string[] RouteOrder = ["RefusedWhileOffAsync(", "SessionOwner(", "slot.TryEnter()", "gate.RequireAsync(", "await make()", "RecordAsync(", "TypedResults.File("];

    private static string Source(params string[] parts) => File.ReadAllText(RepositoryFiles.RepositoryFile(["backend", "src", "XDrishti.Api", .. parts]));

    private static List<string> FilesNaming(string folder, string pattern)
    {
        var dir = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", folder, folder == "Endpoints" ? "ExportsEndpoints.cs" : "ExportCommand.cs"))!;
        return [.. Directory.GetFiles(dir, "*.cs").Where(f => Regex.IsMatch(File.ReadAllText(f), pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))).Select(Path.GetFileName).Cast<string>().Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void The_code_that_makes_a_file_touches_no_account_portfolio_plan_auth_live_or_broker_namespace_no_file_no_network_and_no_process()
    {
        var result = Types.InAssembly(Api).That().ResideInNamespace(ExportsNamespace).ShouldNot().HaveDependencyOnAny(Forbidden).GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Api).That().ResideInNamespace(ExportsNamespace).GetTypes().Count().ShouldBeGreaterThan(5, "the scan sees the exports");
        Types.InAssembly(Api).That().HaveName("ExportsEndpoints").ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Accounts").GetResult().IsSuccessful.ShouldBeFalse("the rule does see a dependency (the endpoint records the audit row)");
    }

    [Fact]
    public void The_mapper_of_the_tables_reads_only_the_reporting_views_and_the_writer()
    {
        Types.InAssembly(Api).That().HaveName("ExportTables").GetTypes().Count().ShouldBe(1, "the scan sees the mapper");
        Types.InAssembly(Api).That().HaveName("ExportTables").ShouldNot().HaveDependencyOnAny("XDrishti.Domain", "XDrishti.Application.Abstractions", "XDrishti.Application.Research", "XDrishti.Application.Learning", "XDrishti.Application.Lab", "XDrishti.Application.Market").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Api).That().HaveName("ExportTables").ShouldNot().HaveDependencyOnAny("XDrishti.Application.Reporting").GetResult().IsSuccessful.ShouldBeFalse("the rule does see the views it reads");
    }

    [Fact]
    public void No_column_of_any_table_is_named_like_something_that_belongs_to_an_account()
    {
        var tables = Api.GetType(ExportsNamespace + ".ExportTables")!;
        var columns = tables.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(IReadOnlyList<string>)).SelectMany(p => (IReadOnlyList<string>)p.GetValue(null)!).ToList();
        columns.AddRange((IReadOnlyList<string>)tables.GetMethod("PivotColumns")!.Invoke(null, ["total_r"])!);

        columns.Count.ShouldBeGreaterThan(40);
        columns.Where(c => AccountColumn.IsMatch(c)).ShouldBeEmpty();
        AccountColumn.IsMatch("client_id").ShouldBeTrue("the check sees a name");
        AccountColumn.IsMatch("avg_holding_sessions").ShouldBeTrue("and would catch the old spelling of a column that is not one");
    }

    [Fact]
    public void One_module_has_the_route_one_post_and_nothing_else_and_it_asks_the_capability_before_anything_and_records_before_it_gives_the_file()
    {
        var endpoint = Source("Endpoints", "ExportsEndpoints.cs");

        Regex.Count(endpoint, @"\.Map(Post|Put|Delete|Patch|Get)\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(1);
        endpoint.ShouldContain(".MapPost(\"/{kind}\"");
        FilesNaming("Endpoints", @"\bExportCommand\b").ShouldBe(["ExportsEndpoints.cs"]);
        FilesNaming("Exports", @"\bExportHandler\b").ShouldBe(["ExportHandler.cs"]);

        var delivery = Source("Endpoints", "ExportDelivery.cs");
        var order = RouteOrder.Select(t => delivery.IndexOf(t, StringComparison.Ordinal)).ToList();
        order.ShouldAllBe(i => i > 0);
        order.ShouldBe([.. order.Order()], "capability, owner, rate rule, the file, the audit row, then the bytes");
        endpoint.ShouldContain("ExportDelivery.DeliverAsync(");
        delivery.ShouldNotContain("AuthorizeAsync(", Case.Sensitive, "an export asks for no password: the decision of the spec (D5) is that the protection is the switch, the audit and the cap");
        Source("Exports", "ExportHandler.cs").ShouldContain("gate.RequireAsync(");
    }

    [Fact]
    public void Both_downloads_of_the_lab_go_through_the_same_delivery_and_nothing_else_in_the_endpoints_gives_a_file()
    {
        var lab = Source("Endpoints", "LabRunsEndpoints.cs");

        Regex.Count(lab, @"ExportDelivery\.DeliverAsync\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).ShouldBe(2);
        lab.ShouldNotContain("TypedResults.File(");
        lab.ShouldNotContain("TypedResults.Text(");
    }

    [Fact]
    public void The_body_names_no_actor_no_password_and_no_file_name_and_no_confirmation_of_the_charge_sheet()
    {
        var request = Api.GetType("XDrishti.Api.Endpoints.ExportRequest")!;

        request.GetProperties().Select(p => p.Name).Order().ShouldBe(["Format", "Query"]);
        typeof(XDrishti.Application.Reporting.ReportQueryInput).GetProperties().Select(p => p.Name).ShouldNotContain(n => Regex.IsMatch(n, "confirm|sheet|actor|password|file|name", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
    }
}
