using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Domain.Selection.SizeScaling;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// BK-10 criterion 8 (ADR 0259): the size scaling can only scale down. The kernels are pure (no I/O, no clock, no logging, no order-named member); the ceiling of the volatility scale is a constant of 1.0, not a setting; and no type, member, setting key or DTO field of the slice
/// says upsize, leverage, margin or multiplier (a whole word or an identifier part, case-insensitive). The slice's own files are scanned; existing code (for example the gate's reduced-risk multiplier) is not part of it.
/// </summary>
public sealed partial class SizeScalingArchitectureTests
{
    private const string Namespace = "XDrishti.Domain.Selection.SizeScaling";

    private static readonly string[] SliceFolders =
    [
        Path.Combine("XDrishti.Domain", "Selection", "SizeScaling"),
        Path.Combine("XDrishti.Application", "Sizing"),
        Path.Combine("XDrishti.Infrastructure", "Selection"),
    ];

    private static readonly string[] SliceFiles =
    [
        Path.Combine("XDrishti.Api", "Endpoints", "SizingEndpoints.cs"),
        Path.Combine("XDrishti.Api", "Endpoints", "SizingRequests.cs"),
        Path.Combine("XDrishti.Application", "Abstractions", "Selection", "ISizingStore.cs"),
        Path.Combine("XDrishti.Application", "Abstractions", "Planning", "ISizingInputs.cs"),
    ];

    [GeneratedRegex("upsiz|leverag|margin|multiplier", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Banned();

    private static IEnumerable<string> SliceSources()
    {
        var src = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!);
        foreach (var folder in SliceFolders)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(src, folder), "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
            {
                yield return file;
            }
        }

        foreach (var file in SliceFiles)
        {
            yield return Path.Combine(src, file);
        }
    }

    [Fact]
    public void The_kernels_reference_no_io_network_database_clock_or_logging_type()
    {
        var result = Types.InAssembly(typeof(VolTarget).Assembly).That().ResideInNamespaceStartingWith(Namespace)
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "Npgsql", "Microsoft.EntityFrameworkCore", "Microsoft.Extensions", "System.Threading.Tasks", "System.Diagnostics")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_kernel_sources_read_no_clock_and_name_no_order_or_broker()
    {
        var folder = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!, SliceFolders[0]);
        var files = Directory.EnumerateFiles(folder, "*.cs").ToList();
        files.Count.ShouldBeGreaterThan(8);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file).Replace(".Order()", "", StringComparison.Ordinal);
            foreach (var forbidden in new[] { "DateTime.Now", "DateTime.UtcNow", "DateTimeOffset.Now", "DateTimeOffset.UtcNow", "TimeProvider", "Stopwatch", "Order", "Broker", "Execution" })
            {
                text.ShouldNotContain(forbidden, Case.Sensitive, file);
            }
        }
    }

    [Fact]
    public void No_file_of_the_slice_says_upsize_leverage_margin_or_multiplier()
    {
        var files = SliceSources().ToList();
        files.Count.ShouldBeGreaterThan(20);

        foreach (var file in files)
        {
            Banned().IsMatch(File.ReadAllText(file)).ShouldBeFalse(file);
        }
    }

    [Fact]
    public void The_ceiling_is_a_constant_of_one_and_no_setting_or_option_can_raise_it()
    {
        VolTarget.Ceiling.ShouldBe(1.0);
        typeof(VolTarget).GetField(nameof(VolTarget.Ceiling))!.IsLiteral.ShouldBeTrue();

        var names = typeof(SizingSettings).GetProperties().Select(p => p.Name).Concat(typeof(SizingOptions).GetProperties().Select(p => p.Name)).ToList();
        names.ShouldNotContain(n => n.Contains("Cap", StringComparison.Ordinal) && !n.Contains("Capacity", StringComparison.Ordinal), "a Cap setting does not exist");
        names.ShouldNotContain(n => n.Contains("Ceiling", StringComparison.Ordinal) || n.Contains("Allow", StringComparison.Ordinal) || n.Contains("Max", StringComparison.Ordinal));
        SizingOptions.FloorIsValid(0.2).ShouldBeFalse();
        SizingOptions.FloorIsValid(1.0).ShouldBeTrue();
    }

    [Fact]
    public void The_four_settings_keys_are_exactly_the_stored_ones_and_all_default_off_or_neutral()
    {
        var keys = typeof(SizingSettings).GetFields().Where(f => f.IsLiteral && f.Name.EndsWith("Key", StringComparison.Ordinal)).Select(f => (string)f.GetRawConstantValue()!).Order().ToList();

        keys.ShouldBe(["sizing.capacity.enabled", "sizing.capacity.pct", "sizing.vol_target.enabled", "sizing.vol_target.floor"]);
        SizingSettings.Defaults.VolTargetEnabled.ShouldBeFalse();
        SizingSettings.Defaults.CapacityEnabled.ShouldBeFalse();
    }
}
