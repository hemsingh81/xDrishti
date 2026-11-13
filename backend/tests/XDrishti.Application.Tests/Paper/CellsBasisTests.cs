#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Paper;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Decision 9 (ADR 0286): the snapshot moves with every refresh, the basis (options and sources without their window ends) does not.</summary>
public sealed class CellsBasisTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StoredRunHeader Header(DateOnly to, string definition = "def1") =>
        new(1, "run1", "sim-1", 1, 1, "ORB", definition, new DateOnly(2024, 1, 1), to, "index", "NIFTY 50", 50, 50, 10, 10, 10, 0, false, "next_open", null, null, 1, "r", "t", DateTimeOffset.UnixEpoch, "owner");

    private static StoredCellStats Stats(string snapshot, string sources, CellStatsOptions? options = null)
    {
        var cell = new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR");
        var row = new CellStatsRow(cell, CellWindow.All, new DateOnly(2026, 10, 23), true, 40, 20, 40, 0.5, 0.5, 0.5, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats(snapshot, DateTimeOffset.UnixEpoch, options ?? new CellStatsOptions(), sources, row);
    }

    private static async Task<Dictionary<string, string>> ReadAsync(StoredCellStats stats, StoredRunHeader header, bool runMissing = false)
    {
        var cellStats = Substitute.For<ICellStatsStore>();
        cellStats.ListLatestCashAsync(null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([stats]));
        var changelog = Substitute.For<ILearningChangelog>();
        changelog.ListAsync("cell", null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([]));
        var runs = Substitute.For<IRunStore>();
        runs.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredRunHeader?>(runMissing ? null : header));
        var source = new ConfigSnapshotSource(
            Substitute.For<IPlanningSettingsReader>(), Substitute.For<IAutonomySettingsStore>(), Substitute.For<IAccountStore>(), Substitute.For<IRiskProfileStore>(), changelog, Substitute.For<IStrategyStore>(), cellStats, runs,
            Substitute.For<IModelRegistry>(), Substitute.For<ISimulationEngine>(), Substitute.For<IGradeSettings>(), Substitute.For<ICostSheetStore>());
        var reading = await source.ReadAsync(Ct);
        return reading.Components.Where(c => c.Key.StartsWith("cells.", StringComparison.Ordinal)).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_refresh_with_the_same_basis_moves_the_snapshot_only_and_is_noted_not_a_new_epoch()
    {
        var before = await ReadAsync(Stats("snapA", "run1:intraday:FIXED_RR"), Header(new DateOnly(2026, 10, 1)));
        var after = await ReadAsync(Stats("snapB", "run2:intraday:FIXED_RR"), Header(new DateOnly(2026, 11, 1)));

        after["cells.snapshot"].ShouldNotBe(before["cells.snapshot"]);
        after["cells.basis"].ShouldBe(before["cells.basis"]);
        var changes = ConfigDiff.Compare(before, after);
        changes.Select(c => (c.Key, c.Class)).ShouldBe([("cells.snapshot", ConfigClass.Free)]);
    }

    [Fact]
    public async Task A_recompute_with_other_sources_or_options_changes_the_basis_and_starts_a_new_epoch()
    {
        var before = await ReadAsync(Stats("snapA", "run1:intraday:FIXED_RR"), Header(new DateOnly(2026, 10, 1)));
        var otherStrategy = await ReadAsync(Stats("snapB", "run2:intraday:FIXED_RR"), Header(new DateOnly(2026, 10, 1), "def2"));
        var otherOptions = await ReadAsync(Stats("snapC", "run1:intraday:FIXED_RR", new CellStatsOptions(MinTrades: 10)), Header(new DateOnly(2026, 10, 1)));
        var otherExit = await ReadAsync(Stats("snapD", "run1:intraday:TRAIL"), Header(new DateOnly(2026, 10, 1)));

        foreach (var after in new[] { otherStrategy, otherOptions, otherExit })
        {
            after["cells.basis"].ShouldNotBe(before["cells.basis"]);
            PaperConfigKeys.Classify("cells.basis", before["cells.basis"], after["cells.basis"]).ShouldBe(ConfigClass.Frozen);
        }
    }

    [Fact]
    public async Task An_unreadable_source_run_fails_closed_and_the_basis_is_not_guessed()
    {
        var read = await ReadAsync(Stats("snapA", "run1:intraday:FIXED_RR"), Header(new DateOnly(2026, 10, 1)), runMissing: true);

        read.ShouldNotContainKey("cells.basis");
    }
}
