using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-02 (ADR 0255): the derived-baskets engine is pure. <c>Domain/Baskets/Derived</c> has no I/O, no network, no database, no clock, no logging, and no order-named type or member.
/// </summary>
public sealed class DerivedBasketsArchitectureTests
{
    private const string Namespace = "XDrishti.Domain.Baskets.Derived";

    [Fact]
    public void The_engine_references_no_io_network_database_or_logging_type()
    {
        var result = Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().ResideInNamespaceStartingWith(Namespace)
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "System.Threading.Tasks", "System.Diagnostics")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_engine_sources_read_no_clock_and_name_no_order()
    {
        var folder = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!, "Baskets", "Derived");
        var files = Directory.EnumerateFiles(folder, "*.cs").ToList();
        files.Count.ShouldBeGreaterThan(10);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file).Replace("OrderByDescending", "", StringComparison.Ordinal).Replace("OrderBy", "", StringComparison.Ordinal);
            foreach (var forbidden in new[] { "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "Stopwatch", "Task.", "Order", "Broker", "Execution" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }
}
