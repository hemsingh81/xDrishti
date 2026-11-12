namespace XDrishti.Architecture.Tests;

/// <summary>DATA-01 (ADR 0274): the dated ban-file path is named in one place and fetched by one class; the as-of port is read only by the plan's list filter and the wiring.</summary>
public sealed class ListHistoryArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> SourceFiles() =>
        Directory.GetFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Relative(string file) => Path.GetRelativePath(Src, file).Replace(Path.DirectorySeparatorChar, '/');

    [Fact]
    public void Only_the_archive_source_calls_the_dated_path_and_only_the_declaring_file_names_it()
    {
        var callers = SourceFiles().Where(f => File.ReadAllText(f).Contains("ListArchive.BanPath", StringComparison.Ordinal)).Select(Relative).ToList();
        var literal = SourceFiles().Where(f => File.ReadAllText(f).Contains("fo_secban_", StringComparison.Ordinal)).Select(Relative).ToList();

        callers.ShouldBe(["XDrishti.Infrastructure/Market/Lists/NseListArchiveSource.cs"]);
        literal.ShouldBe(["XDrishti.Application/Market/Lists/ListArchive.cs"]);
    }

    [Fact]
    public void The_as_of_port_is_used_only_by_the_plan_list_filter_the_reader_and_the_wiring()
    {
        string[] allowed =
        [
            "XDrishti.Application/Abstractions/Market/Lists/IListFlagsAsOf.cs",
            "XDrishti.Application/Planning/PlanListFilter.cs",
            "XDrishti.Infrastructure/Market/Lists/ListFlagsAsOfReader.cs",
            "XDrishti.Infrastructure/DependencyInjection.cs",
        ];
        var users = SourceFiles().Where(f => File.ReadAllText(f).Contains("IListFlagsAsOf", StringComparison.Ordinal)).Select(Relative).Order(StringComparer.Ordinal).ToList();

        users.ShouldBe([.. allowed.Order(StringComparer.Ordinal)]);
    }
}
