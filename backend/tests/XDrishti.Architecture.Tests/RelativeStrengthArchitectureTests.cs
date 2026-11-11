using NetArchTest.Rules;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-09 (ADR 0263): the relative-strength kernels are pure (no I/O, no clock, no logging, no order-named type or member), and nothing in the selection reads them or the stored text of <c>RS_ROTATION</c> (the features are for the Lab and the reports until
/// an owner-approved change says otherwise).
/// </summary>
public sealed class RelativeStrengthArchitectureTests
{
    private const string Namespace = "XDrishti.Domain.Market.Strength";

    [Fact]
    public void The_kernels_reference_no_io_network_database_or_logging_type()
    {
        var result = Types.InAssembly(typeof(XDrishti.Domain.Common.Result).Assembly).That().ResideInNamespaceStartingWith(Namespace)
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "System.Threading.Tasks", "System.Diagnostics")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_kernel_sources_read_no_clock_and_name_no_order()
    {
        var folder = Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!, "Market", "Strength");
        var files = Directory.EnumerateFiles(folder, "*.cs").ToList();
        files.Count.ShouldBeGreaterThan(6);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file).Replace("OrderByDescending", "", StringComparison.Ordinal).Replace("OrderBy", "", StringComparison.Ordinal).Replace(".Order()", "", StringComparison.Ordinal);
            foreach (var forbidden in new[] { "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "Stopwatch", "Task.", "Order", "Broker", "Execution" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void The_selection_does_not_reference_the_kernels_or_the_stored_text_of_the_strategy()
    {
        var domain = typeof(XDrishti.Domain.Common.Result).Assembly;
        var violations = Types.InAssembly(domain).That().ResideInNamespaceStartingWith("XDrishti.Domain.Selection")
            .ShouldNot().HaveDependencyOnAny(Namespace)
            .GetResult();
        violations.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", violations.FailingTypeNames ?? []));

        var root = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!;
        foreach (var folder in new[] { Path.Combine(root, "Selection"), Path.Combine(root, "..", "XDrishti.Application", "Selection"), Path.Combine(root, "..", "XDrishti.Application", "Abstractions", "Selection") })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                text.ShouldNotContain("RS_ROTATION", Case.Sensitive, file);
                text.ShouldNotContain("mom_rank", Case.Sensitive, file);
                text.ShouldNotContain("Market.Strength", Case.Sensitive, file);
            }
        }
    }
}
