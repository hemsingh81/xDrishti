namespace XDrishti.Architecture.Tests;

/// <summary>
/// P15-09 criterion 8: the research strategy <c>PEAD</c> and its builder are offered to no plan, selection or paper account. Nothing under the planning, selection or paper folders of the application, the infrastructure or the hosts names the strategy id, its
/// builder or its event source; the only users of the strategy are the engine's table of built-ins, its builder and the source that reads its stored runs for protocol P. The no-order tripwire is unchanged and covers this slice like any other.
/// </summary>
public sealed class PeadArchitectureTests
{
    private static readonly string[] Words = ["PEAD", "PeadV1", "PeadRunEventSource"];

    private static readonly string[] Folders =
    [
        "XDrishti.Application/Planning", "XDrishti.Application/Selection", "XDrishti.Application/Paper", "XDrishti.Application/Live", "XDrishti.Application/LiveTrading", "XDrishti.Application/Execution",
        "XDrishti.Infrastructure/Planning", "XDrishti.Infrastructure/Paper", "XDrishti.Infrastructure/LiveTrading", "XDrishti.Infrastructure/Execution",
        "XDrishti.Execution", "XDrishti.Worker", "XDrishti.Feed",
    ];

    [Fact]
    public void No_plan_selection_paper_or_execution_code_names_the_pead_strategy_its_builder_or_its_event_source()
    {
        var source = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!;
        var files = Folders
            .Select(f => Path.Combine(source, "src", f))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.GetFiles(d, "*.cs", SearchOption.AllDirectories))
            .Where(f => !RepositoryFiles.IsBuildOutput(f))
            .ToList();

        files.ShouldNotBeEmpty("the folders of the rule exist");
        var users = files.Where(f => Words.Any(w => File.ReadAllText(f).Contains(w, StringComparison.Ordinal))).Select(f => Path.GetRelativePath(source, f)).Order(StringComparer.Ordinal).ToList();

        users.ShouldBeEmpty("PEAD is a research strategy: it is in no plan, selection, paper account or order path");
    }

    [Fact]
    public void The_strategy_id_is_named_only_by_the_engine_the_event_source_the_run_label_and_the_protocol()
    {
        var source = Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "Directory.Packages.props"))!;
        var users = Directory.GetFiles(Path.Combine(source, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("\"PEAD\"", StringComparison.Ordinal) || File.ReadAllText(f).Contains("PeadV1", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .Order(StringComparer.Ordinal)
            .ToList();

        users.ShouldBe(["BuiltInStrategies.cs", "PeadRunEventSource.cs", "PeadV1.cs", "RunLabels.cs"]);
    }
}
