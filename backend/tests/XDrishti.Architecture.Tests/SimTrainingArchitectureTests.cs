using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// AU-13 (ADR 0243 point 4, ADR 0249): training from the simulation is a challenger-only, hold-out-untouched, order-free path. These pins fail any change that lets it designate or promote a model, switch influence on, read the hold-out, read training runs through the evidence run
/// store, write the research cell statistics, name a broker or the execution service, or lets a database row claim otherwise.
/// </summary>
public sealed class SimTrainingArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static string Folder => Path.Combine(Src, "XDrishti.Application", "Learning", "SimTraining");

    private static IEnumerable<string> FolderSources() => Directory.EnumerateFiles(Folder, "*.cs", SearchOption.AllDirectories);

    private static string Code(string file) => Regex.Replace(Regex.Replace(File.ReadAllText(file), @"/\*.*?\*/", " ", RegexOptions.Singleline, TimeSpan.FromSeconds(2)), @"(?<=^|\s)//[^\n]*", " ", RegexOptions.Multiline, TimeSpan.FromSeconds(2));

    [Fact]
    public void The_training_folder_cannot_designate_promote_roll_back_or_switch_influence_on()
    {
        FolderSources().ShouldNotBeEmpty();
        string[] forbidden =
        [
            "DesignateAsync", "DesignateModel", "OwnerDesignate", "PromoteChallenger", "DemoteModel", "ApplyRollback", "ProposeRollback", "IMlInfluenceGate", "IMlInfluenceSwitch", "IMlInfluenceEvidence", "MlMayInfluence", "MlInfluence", "ChampionRule", "ModelRole.Champion",
        ];

        foreach (var file in FolderSources())
        {
            var code = Code(file);
            foreach (var word in forbidden)
            {
                code.ShouldNotContain(word, Case.Sensitive, $"{Path.GetFileName(file)} names {word}: this path registers a challenger and nothing else (spec AU-13, acceptance 5)");
            }
        }
    }

    [Fact]
    public void The_training_folder_never_reads_the_hold_out_the_evidence_run_store_the_research_cell_statistics_a_broker_or_the_execution_service()
    {
        string[] forbidden =
        [
            "IHoldOutStore", "HoldOut.Reserve", "SharedHoldOut", "LabHoldOutGuard", "IRunStore", "ICellStatsStore", "IReportTradeSource", "IBroker", "Broker", "PlaceOrder", "ModifyOrder", "CancelOrder", "XDrishti.Execution", "IOrder",
        ];

        foreach (var file in FolderSources())
        {
            var code = Code(file);
            foreach (var word in forbidden)
            {
                code.ShouldNotContain(word, Case.Sensitive, $"{Path.GetFileName(file)} names {word} (spec AU-13: hold-out untouched, training runs only through ITrainingRunReader, no orders)");
            }
        }
    }

    [Fact]
    public void Only_the_training_store_touches_the_new_tables_and_the_migration_pins_the_challenger_and_the_unread_hold_out_in_the_database()
    {
        var offenders = Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(Code(f), @"sim_training_runs|training_sim_cell_stats", RegexOptions.None, TimeSpan.FromSeconds(2)))
            .Select(f => Path.GetRelativePath(Src, f).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBe(["XDrishti.Infrastructure/SimAll/SimTrainingStore.cs"], "no other source reads or writes the training record or the training_sim cell statistics, so no evidence reader can see them");

        var migration = File.ReadAllText(Path.Combine(Src, "XDrishti.Infrastructure", "Persistence", "Migrations", "20261110120000_AddTrainingSimBasis.cs"));
        migration.ShouldContain("hold_out_read = false");
        migration.ShouldContain("verdict <> 'Eligible'");
        migration.ShouldContain("basis = 'training_sim'");
        migration.ShouldContain("GRANT SELECT, INSERT ON research.sim_training_runs, research.training_sim_cell_stats TO xd_app");
        Regex.IsMatch(File.ReadAllText(Path.Combine(Src, "XDrishti.Infrastructure", "SimAll", "SimTrainingStore.cs")), @"\b(UPDATE|DELETE)\b", RegexOptions.None, TimeSpan.FromSeconds(2)).ShouldBeFalse("the training store is insert-only");
    }

    [Fact]
    public void The_verdict_of_a_training_from_the_simulation_is_capped_in_the_handler()
    {
        var text = Code(Path.Combine(Folder, "TrainFromSimulationHandler.cs"));

        text.ShouldContain("ValidationOutcome.Eligible");
        text.ShouldContain("outcome = ValidationOutcome.Inconclusive");
        text.ShouldContain("SimTrainingRules.Admits");
    }
}
