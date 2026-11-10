using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-05 (ADR 0256): the list parsers and builders are pure (no I/O, network, database, clock or logging), and neither they nor the use cases that apply them name an order, a margin amount or a leverage multiplier.
/// </summary>
public sealed class ListBasketsArchitectureTests
{
    private const string Namespace = "XDrishti.Domain.Market.Baskets.Lists";

    private static string Folder(params string[] parts) =>
        Path.Combine([Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", parts[0], parts[0] + ".csproj"))!, .. parts.Skip(1)]);

    [Fact]
    public void The_list_parsers_and_builders_reference_no_io_network_database_or_logging_type()
    {
        var result = Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().ResideInNamespaceStartingWith(Namespace)
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "System.Threading.Tasks", "System.Diagnostics")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_domain_list_sources_read_no_clock_and_name_no_order_margin_or_multiplier()
    {
        var files = Directory.EnumerateFiles(Folder("XDrishti.Domain", "Market", "Baskets", "Lists"), "*.cs").ToList();
        files.Count.ShouldBeGreaterThanOrEqualTo(5);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file).Replace("OrderByDescending", "", StringComparison.Ordinal).Replace("OrderBy", "", StringComparison.Ordinal).Replace(".Order(", "(", StringComparison.Ordinal);
            foreach (var forbidden in new[] { "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "Stopwatch", "Task.", "Order", "Margin", "Multiplier", "Execution" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void The_application_list_use_cases_name_no_order_margin_or_multiplier()
    {
        var files = Directory.EnumerateFiles(Folder("XDrishti.Application", "Market", "Baskets", "Lists"), "*.cs").ToList();
        files.Count.ShouldBeGreaterThanOrEqualTo(2);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var forbidden in new[] { "PlaceOrder", "SubmitOrder", "Margin", "Multiplier", "Execution", "IBroker" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }
}
