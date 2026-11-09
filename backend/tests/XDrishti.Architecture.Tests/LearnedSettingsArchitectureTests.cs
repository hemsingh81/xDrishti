namespace XDrishti.Architecture.Tests;

/// <summary>
/// AU-10 (ADR 0242, ADR 0247): the pins that keep learned changes tighten-only. There is one place that decides a proposed change as <c>auto</c>, it only does so for a tightening and only when Auto is ON; the descriptor for a learnable key has a declared direction;
/// and nothing in the feature references the execution service or an order path.
/// </summary>
public sealed class LearnedSettingsArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f));

    [Fact]
    public void Only_the_weekly_run_decides_a_change_as_auto_and_only_for_a_tightening_with_auto_on()
    {
        var users = Sources().Where(f => File.ReadAllText(f).Contains("LearnedSettingProposer.AutoActor", StringComparison.Ordinal)).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        // the run (decides), the notice source and the start-up overlay (both only read the actor name)
        users.ShouldBe(["LearnedHandlers.cs", "LearnedOverridesPostConfigure.cs", "LearnedTighteningNotices.cs"]);

        var handlers = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "LearnedSettings", "LearnedHandlers.cs"));
        var autoDecisions = handlers.Split('\n').Where(l => l.Contains("DecideAsync(", StringComparison.Ordinal) && l.Contains("AutoActor", StringComparison.Ordinal)).ToList();
        autoDecisions.Count.ShouldBe(1);
        autoDecisions[0].ShouldContain("LearnedOutcome.Tighten");
        autoDecisions[0].ShouldContain("autoOn");
        autoDecisions[0].ShouldContain("WeeklyCap");
    }

    [Fact]
    public void The_store_is_written_only_by_the_learned_handlers_and_its_own_implementation()
    {
        var writers = Sources()
            .Where(f => File.ReadAllText(f) is var t && t.Contains("ILearnedSettingStore", StringComparison.Ordinal)
                && (t.Contains(".InsertProposedAsync(", StringComparison.Ordinal) || t.Contains(".DecideAsync(", StringComparison.Ordinal) || t.Contains(".UndoAsync(", StringComparison.Ordinal)))
            .Select(Path.GetFileName)
            .ToList();

        writers.ShouldBe(["LearnedHandlers.cs"]);
    }

    [Fact]
    public void The_learned_feature_has_no_order_path_and_no_reference_to_the_execution_service()
    {
        var files = Sources().Where(f => f.Contains("LearnedSettings", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("Learned", StringComparison.Ordinal) || Path.GetFileName(f).StartsWith("EssentialSetting", StringComparison.Ordinal)).ToList();

        files.ShouldNotBeEmpty();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution", Case.Sensitive, file);
            text.ShouldNotContain("IBrokerOrder", Case.Sensitive, file);
            text.ShouldNotContain("PlaceOrder", Case.Sensitive, file);
        }
    }

    [Fact]
    public void The_migration_keeps_the_database_side_of_the_rule()
    {
        var migration = File.ReadAllText(Path.Combine(Src, "XDrishti.Infrastructure", "Persistence", "Migrations", "20261109130000_AddLearnedSettingChanges.cs"));

        migration.ShouldContain("NOT (decided_by = 'auto' AND (direction <> 'tighten' OR source <> 'learning'))");
        migration.ShouldContain("An automatic decision needs the Auto switch to be ON");
        migration.ShouldContain("MakeAppendOnlyForAppRole");
    }
}
