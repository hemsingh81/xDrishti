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

    // The readers that need every stored bar: the pipeline that writes them, the repair, the quality checks and the backfill itself. Every other caller is a simulation, learning, planning or report path and must read through SeamAwareBars.
    private static readonly string[] RawReaders =
    [
        "XDrishti.Application.Market.Bars.Pipeline.DhanPhaseRunner",
        "XDrishti.Application.Market.Bars.Repair.RepairBarHolesHandler",
        "XDrishti.Application.Market.History.ProbeSeamHandler",
        "XDrishti.Application.Market.History.RunBackfillHandler",
        "XDrishti.Application.Market.History.SeamAwareBars",
        "XDrishti.Application.Market.Quality.GetQualityInstrumentHandler",
        "XDrishti.Application.Market.Quality.QualityPhaseRunner",
    ];

    [Fact]
    public void Only_the_pipeline_the_repair_the_quality_checks_and_the_backfill_read_bars_raw_every_other_path_goes_through_the_seam_aware_read()
    {
        var assemblies = new[] { typeof(XDrishti.Application.DependencyInjection).Assembly, typeof(XDrishti.Infrastructure.Persistence.DatabaseOptions).Assembly, System.Reflection.Assembly.Load(new System.Reflection.AssemblyName("XDrishti.Api")) };
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.GetTypes())
            {
                var root = type;
                while (root.DeclaringType is not null)
                {
                    root = root.DeclaringType;
                }

                if (type.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "ReadAsync" && call.DeclaringType.FullName == "XDrishti.Application.Abstractions.Market.Bars.IBarStore"))
                {
                    callers.Add(root.FullName);
                }
            }
        }

        callers.ToList().ShouldBe(RawReaders, ignoreOrder: true, customMessage: "a simulation, learning, planning or report path reads bars through SeamAwareBars (ADR 0270, decision 10)");
    }

    [Fact]
    public void The_one_read_of_a_run_drops_the_bars_before_the_seam()
    {
        var reader = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Research", "InstrumentInputReader.cs"));

        reader.ShouldContain("SeamAwareBars.ReadAsync");
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
