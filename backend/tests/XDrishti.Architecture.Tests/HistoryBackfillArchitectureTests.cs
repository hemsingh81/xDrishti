namespace XDrishti.Architecture.Tests;

/// <summary>CF-04 Part B (ADR 0270): the ledger and the seam table are written and read by one store only, and the bar store is begun for the Dhan source by the backfill and the existing pipeline only.</summary>
public sealed class HistoryBackfillArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> SourceFiles() =>
        Directory.GetFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void The_backfill_tables_are_named_in_code_only_by_the_backfill_store()
    {
        string[] names = ["backfill_chunks", "backfill_seams"];
        var offenders = SourceFiles()
            .Where(f => Path.GetFileName(f) != "BackfillStore.cs")
            .Where(f => File.ReadAllLines(f).Any(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && names.Any(n => l.Contains(n, StringComparison.Ordinal))))
            .Select(f => Path.GetRelativePath(Src, f))
            .ToList();

        offenders.ShouldBeEmpty("only BackfillStore.cs (and the migrations, excluded here) may name the backfill tables in SQL");
    }

    [Fact]
    public void The_one_read_of_a_run_drops_the_bars_before_the_seam()
    {
        var reader = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Research", "InstrumentInputReader.cs"));

        reader.ShouldContain("IBackfillSeams", Case.Sensitive, "a run reads no pre-seam bar (ADR 0270, decision 10)");
        reader.ShouldContain("EpochSeconds >= seamEpoch");
    }

    [Fact]
    public void The_backfill_writer_passes_every_bar_through_the_seam_guard_before_it_merges()
    {
        var runner = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Market", "History", "RunBackfillHandler.cs"));

        runner.IndexOf("SeamGuard.Split", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        runner.IndexOf("SeamGuard.Split", StringComparison.Ordinal).ShouldBeLessThan(runner.IndexOf("WriteAsync(target.InstrumentId", StringComparison.Ordinal));
        runner.ShouldNotContain("BarSource.Csv");
    }
}
