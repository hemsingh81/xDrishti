namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// Criteria 1, 8, 9 and 10 of P4-06 through the verb over the real host and a real TimescaleDB: <c>--select-exit</c> adds the choice to the comparison (its folder, its files, its banner, its key), nothing changes without the flag, and <c>--store</c>
/// keeps the choice after the comparison. The seeded window is three sessions, so the walk-forward has no fold: the folds themselves are tested on synthetic series; here the wiring is.
/// </summary>
public sealed partial class SimulateDatabaseTests
{
    private const string SelectArgs = ExitArgs + " --exit FIXED_RR:rr=2|3 --select-exit --min-train-months 1 --test-months 1 --min-trades 2";

    [Fact]
    public async Task Select_exit_writes_the_choice_beside_the_comparison_and_prints_the_banner_and_the_verdict()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(SelectArgs, temp);

        outcome.Exit.ShouldBe(0, outcome.Error);
        var folder = RunFolder(temp);
        File.Exists(Path.Combine(folder, "exit-selection.json")).ShouldBeTrue();
        File.Exists(Path.Combine(folder, "exit-selection.csv")).ShouldBeTrue();
        File.ReadAllText(Path.Combine(folder, "exit-selection.json")).ShouldContain("in-sample on the data shown");
        File.ReadAllText(Path.Combine(folder, "exit-selection.csv")).ShouldContain("in-sample on the data shown");
        File.ReadAllText(Path.Combine(folder, "README.txt")).ShouldContain("exit choice for ORB");
        outcome.Out.ShouldContain("exit selection key: ");
        outcome.Out.ShouldContain("the choice is in-sample on the data shown (");
        outcome.Out.ShouldContain(", no hold-out) and is not evidence of an edge");
        outcome.Out.ShouldContain("no stability statement");
        outcome.Out.ShouldContain("final choice: no choice, default FIXED_RR{rr=2}");
    }

    [Fact]
    public async Task Without_select_exit_the_comparison_folder_has_no_choice_files_and_the_folder_is_not_the_folder_of_a_choice()
    {
        await SeedStoreInstrumentsAsync();
        using var plain = new TempFolder();
        using var chosen = new TempFolder();

        var a = await VerbAsync(ExitArgs + " --exit FIXED_RR:rr=2|3", plain);
        var b = await VerbAsync(SelectArgs, chosen);

        a.Exit.ShouldBe(0, a.Error);
        b.Exit.ShouldBe(0, b.Error);
        File.Exists(Path.Combine(RunFolder(plain), "exit-selection.json")).ShouldBeFalse();
        a.Out.ShouldNotContain("exit selection key");
        Path.GetFileName(RunFolder(plain)).ShouldNotBe(Path.GetFileName(RunFolder(chosen)));
    }

    [Fact]
    public async Task Select_exit_with_store_keeps_the_choice_after_the_comparison_and_the_same_again_is_already_stored()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();
        var args = SelectArgs + " --store";

        var first = await VerbAsync(args, temp);
        var second = await VerbAsync(args, temp);

        first.Exit.ShouldBe(0, first.Error);
        second.Exit.ShouldBe(0, second.Error);
        var key = first.Out.Split('\n').Single(l => l.StartsWith("exit selection key: ", StringComparison.Ordinal))["exit selection key: ".Length..].Trim();
        first.Out.ShouldContain("stored: exit selection " + key[..12]);
        second.Out.ShouldContain("already stored: exit selection " + key[..12]);
        (await ResearchCountAsync("SELECT count(*) FROM research.exit_selections WHERE selection_key = @k AND chosen_candidate_id IS NULL AND stable IS NULL", ("k", key))).ShouldBe(1);
    }

    [Fact]
    public async Task Select_exit_without_a_comparison_is_a_usage_error()
    {
        await SeedStoreInstrumentsAsync();
        using var temp = new TempFolder();

        var outcome = await VerbAsync(ExitArgs + " --exit FIXED_RR:rr=2 --select-exit", temp);

        outcome.Exit.ShouldBe(2);
        outcome.Error.ShouldContain("--select-exit");
    }
}
