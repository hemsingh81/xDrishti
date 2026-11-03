using System.Reflection;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// CF-04 Part A (ADR 0195), criterion 2 and the "nothing reads it yet" rule: no code adds an entry to the master-data allow-list (the pattern list ships empty and is the owner's to edit, in the one file that declares it), and the daily-history
/// types are used only by the history slice itself, the wiring and the owner's verbs: no decision, simulation, live, execution or planning code reads <c>market.daily_eod</c> in this slice.
/// </summary>
public sealed class DailyHistoryArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> SourceFiles() =>
        Directory.GetFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void No_source_file_but_the_declaring_ones_creates_an_allow_list_entry()
    {
        var creators = SourceFiles().Where(f => File.ReadAllText(f).Contains("new AllowedPattern(", StringComparison.Ordinal) || File.ReadAllText(f).Contains("AllowedPattern(", StringComparison.Ordinal))
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        creators.ShouldBe(["AllowedPattern.cs"], "the entries are the owner's act (ADR 0097): they are added in MasterDataEndpoints.Patterns by hand and by nobody else; this rule is then loosened by the owner");
    }

    [Fact]
    public void The_pattern_list_is_a_read_only_static_and_ships_empty()
    {
        var type = typeof(XDrishti.Infrastructure.Persistence.DatabaseOptions).Assembly.GetType("XDrishti.Infrastructure.Market.MasterDataEndpoints")!;
        var field = type.GetField("Patterns", BindingFlags.Public | BindingFlags.Static)!;

        field.IsInitOnly.ShouldBeTrue();
        ((System.Collections.ICollection)field.GetValue(null)!).Count.ShouldBe(0, "the owner adds the entries named in ADR 0195 after reading NSE's terms");
    }

    [Fact]
    public void Only_the_history_slice_the_wiring_and_the_owners_verbs_use_the_daily_history_ports()
    {
        string[] allowed = ["Eod", "DependencyInjection.cs", "DataEodCommand.cs", "XdCommand.cs"];
        var users = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("Market.Eod", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Eod{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(n => !allowed.Contains(n))
            .ToList();

        users.ShouldBeEmpty("no decision, simulation, live, execution or planning code may read the daily history in this slice");
    }
}
