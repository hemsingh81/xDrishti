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

    private static string Relative(string file) => Path.GetRelativePath(Src, file).Replace(Path.DirectorySeparatorChar, '/');

    private static readonly string[] HistoryFolders = ["XDrishti.Application/Market/Eod", "XDrishti.Application/Abstractions/Market/Eod", "XDrishti.Infrastructure/Market/Eod"];

    private static readonly string[] WiringFiles = ["XDrishti.Application/DependencyInjection.cs", "XDrishti.Infrastructure/DependencyInjection.cs"];

    [Fact]
    public void No_source_file_but_the_declaring_ones_mentions_the_allow_list_entry_type_or_builds_a_guard_with_arguments()
    {
        string[] declaring = ["AllowedPattern.cs", "MasterDataEndpoints.cs", "MasterDataEndpointGuard.cs"];
        var mentions = SourceFiles().Where(f => File.ReadAllText(f).Contains("AllowedPattern", StringComparison.Ordinal))
            .Select(Path.GetFileName).Where(n => !declaring.Contains(n)).Order(StringComparer.Ordinal).ToList();
        var guards = SourceFiles().Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"new\s+MasterDataEndpointGuard\s*\(\s*[^\s)]", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)))
            .Select(Relative).ToList();

        mentions.ShouldBeEmpty("the entries are the owner's act (ADR 0010, 0017, 0195): they are added in MasterDataEndpoints.Patterns by hand and by nobody else; this rule is then loosened by the owner");
        guards.ShouldBeEmpty("the guard in the application always reads MasterDataEndpoints.Patterns; only tests give it patterns of their own");
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
        string[] verbs = ["XDrishti.Api/Cli/DataEodCommand.cs"];
        var users = SourceFiles()
            .Where(f => File.ReadAllText(f).Contains("Market.Eod", StringComparison.Ordinal))
            .Select(Relative)
            .Where(r => !HistoryFolders.Contains(Path.GetDirectoryName(r)!.Replace('\\', '/')) && !WiringFiles.Contains(r) && !verbs.Contains(r))
            .ToList();

        users.ShouldBeEmpty("no decision, simulation, live, execution or planning code may read the daily history in this slice");
    }

    [Fact]
    public void The_wiring_files_name_the_history_types_only_in_registration_comment_and_options_lines()
    {
        var offenders = WiringFiles.SelectMany(r => File.ReadAllLines(Path.Combine(Src, r)).Select((line, i) => (r, line: line.Trim(), n: i + 1)))
            .Where(x => x.line.Contains("Market.Eod", StringComparison.Ordinal))
            .Where(x => !x.line.StartsWith("services.Add", StringComparison.Ordinal) && !x.line.StartsWith("//", StringComparison.Ordinal) && !x.line.Contains("HistoryOptions", StringComparison.Ordinal))
            .Select(x => $"{x.r}:{x.n}")
            .ToList();

        offenders.ShouldBeEmpty("the wiring files may register the history types; they must not use them");
    }

    [Fact]
    public void The_daily_tables_are_named_in_code_only_by_the_store_and_the_migrations()
    {
        string[] names = ["daily_eod", "eod_universe", "eod_files"];
        var offenders = SourceFiles()
            .Where(f => Path.GetFileName(f) != "EodStore.cs")
            .Where(f => File.ReadAllLines(f).Any(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && names.Any(n => l.Contains(n, StringComparison.Ordinal))))
            .Select(Relative)
            .ToList();

        offenders.ShouldBeEmpty("only EodStore.cs (and the migrations, excluded here) may name the daily tables in SQL");
    }
}
