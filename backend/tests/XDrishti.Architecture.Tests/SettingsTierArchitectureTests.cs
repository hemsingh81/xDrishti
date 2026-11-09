using System.Text.RegularExpressions;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Architecture.Tests;

/// <summary>AU-08 (ADR 0242): every configuration section bound at start-up has a descriptor with a tier, and the Essential tier stays small. Nothing here places an order.</summary>
public sealed partial class SettingsTierArchitectureTests
{
    [GeneratedRegex("""const string Section(?:Name)? = "([A-Za-z:]+)";""")]
    private static partial Regex SectionConstant();

    [GeneratedRegex("""BindConfiguration\("([A-Za-z:]+)"\)""")]
    private static partial Regex BoundLiteral();

    [GeneratedRegex(@"(?:GetSection\(|configuration\[|GetValue(?:<[A-Za-z?\[\]]+>)?\()""([A-Z][A-Za-z]*(?::[A-Za-z]+)+)""")]
    private static partial Regex ReadLiteral();

    /// <summary>Literals that are not settings of their own: sub-sections read relative to a described section, and the section the planning reader enumerates.</summary>
    private static readonly HashSet<string> RelativeOrEnumerated = new(StringComparer.Ordinal) { "FullExits", "Gates", "Robustness:SweepSteps", "Planning" };

    [Fact]
    public void Every_section_bound_at_start_up_has_a_descriptor()
    {
        var src = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!;
        src = Path.GetDirectoryName(src)!;
        var sections = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in SectionConstant().Matches(text))
            {
                sections.Add(m.Groups[1].Value);
            }

            foreach (Match m in BoundLiteral().Matches(text))
            {
                sections.Add(m.Groups[1].Value);
            }

            if (!file.EndsWith("LabSettingsBinder.cs", StringComparison.Ordinal)) // reads keys relative to StrategyLab, which is described
            {
                foreach (Match m in ReadLiteral().Matches(text))
                {
                    if (!RelativeOrEnumerated.Contains(m.Groups[1].Value))
                    {
                        sections.Add(m.Groups[1].Value);
                    }
                }
            }
        }

        sections.Count.ShouldBeGreaterThan(20);
        var described = SettingsRegistry.All.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var missing = sections.Where(s => s != "Confidence" && !Covered(described, s)).ToList(); // Confidence is registered key by key and covered in the Api tests
        missing.ShouldBeEmpty("Sections with no descriptor in SettingsRegistry: " + string.Join(", ", missing));
    }

    /// <summary>A literal is covered by a descriptor with the same key or by a descriptor of an enclosing section.</summary>
    private static bool Covered(HashSet<string> described, string key)
    {
        for (var k = key; ; k = k[..k.LastIndexOf(':')])
        {
            if (described.Contains(k))
            {
                return true;
            }

            if (!k.Contains(':', StringComparison.Ordinal))
            {
                return false;
            }
        }
    }

    [Fact]
    public void Every_descriptor_has_a_tier_and_at_most_eight_are_essential()
    {
        SettingsRegistry.All.Count(d => d.Tier == SettingTier.Essential).ShouldBeLessThanOrEqualTo(SettingsRegistry.MaxEssential);
        SettingsRegistry.All.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count().ShouldBe(SettingsRegistry.All.Count);
        SettingsRegistry.All.ShouldAllBe(d => Enum.IsDefined(d.Tier));
    }

    [Fact]
    public void The_settings_slice_names_no_execution_project()
    {
        var dir = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "SettingsCatalog", "SettingsRegistry.cs"))!;
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs"))
        {
            File.ReadAllText(file).ShouldNotContain("XDrishti.Execution");
        }
    }
}
