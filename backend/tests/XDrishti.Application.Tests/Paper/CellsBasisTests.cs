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
using XDrishti.Application.Tests.Learning.Jobs;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>Decision 9 (ADR 0286): the snapshot moves with every refresh, the basis (options and sources without their window ends) does not.</summary>
public sealed class CellsBasisTests
{
    private const string Source = "run1:intraday:FIXED_RR";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static StoredCellStats Stats(string snapshot, string sources = Source, CellStatsOptions? options = null)
    {
        var cell = new CellId("ORB", "15m", "BUY", "intraday", "FIXED_RR");
        var row = new CellStatsRow(cell, CellWindow.All, new DateOnly(2026, 10, 23), true, 40, 20, 40, 0.5, 0.5, 0.5, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);
        return new StoredCellStats(snapshot, DateTimeOffset.UnixEpoch, options ?? new CellStatsOptions(), sources, row);
    }

    private static string Sheet(decimal capital = 1_000_000m, string profile = "standard") =>
        $$"""{"sheet_id":"default","rates_sha256":"x","sheet_json":"{}","checked":null,"capital":{{capital}},"profile":{"name":"{{profile}}","risk_per_trade_pct":0.5,"max_capital_per_trade_pct":25},"max_cost_r":0.1}""";

    private static async Task<ConfigReading> ReadAsync(StoredCellStats stats, string? record = null, bool runMissing = false, bool recordMissing = false)
    {
        var cellStats = Substitute.For<ICellStatsStore>();
        cellStats.ListLatestCashAsync(null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<StoredCellStats>>([stats]));
        var changelog = Substitute.For<ILearningChangelog>();
        changelog.ListAsync("cell", null, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ChangelogEntry>>([]));
        var runs = Substitute.For<IRunStore>();
        runs.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<StoredRunHeader?>(runMissing ? null : JobFixtures.Header()));
        runs.ReadRecordTextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<string?>(recordMissing ? null : record ?? JobFixtures.Record()));
        var source = new ConfigSnapshotSource(
            Substitute.For<IPlanningSettingsReader>(), Substitute.For<IAutonomySettingsStore>(), Substitute.For<IAccountStore>(), Substitute.For<IRiskProfileStore>(), changelog, Substitute.For<IStrategyStore>(), cellStats, runs,
            Substitute.For<IModelRegistry>(), Substitute.For<ISimulationEngine>(), Substitute.For<IGradeSettings>(), Substitute.For<ICostSheetStore>());
        return await source.ReadAsync(Ct);
    }

    private static async Task<Dictionary<string, string>> CellsAsync(StoredCellStats stats, string? record = null)
    {
        var reading = await ReadAsync(stats, record);
        return reading.Components.Where(c => c.Key.StartsWith("cells.", StringComparison.Ordinal)).ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_refresh_with_the_same_basis_moves_the_snapshot_only_and_is_noted_not_a_new_epoch()
    {
        var before = await CellsAsync(Stats("snapA"));
        var after = await CellsAsync(Stats("snapB", "run2:intraday:FIXED_RR"), JobFixtures.Record().Replace("2026-04-30", "2026-10-30", StringComparison.Ordinal)); // only the window end moves

        after["cells.snapshot"].ShouldNotBe(before["cells.snapshot"]);
        after["cells.basis"].ShouldBe(before["cells.basis"]);
        ConfigDiff.Compare(before, after).Select(c => (c.Key, c.Class)).ShouldBe([("cells.snapshot", ConfigClass.Free)]);
    }

    [Fact]
    public async Task A_recompute_with_another_rule_input_changes_the_basis_and_starts_a_new_epoch()
    {
        var before = await CellsAsync(Stats("snapA"), JobFixtures.Record(costs: Sheet()));
        var changed = new Dictionary<string, (StoredCellStats Stats, string Record)>
        {
            ["strategy"] = (Stats("s1"), JobFixtures.Record(costs: Sheet(), sha: new string('e', 64))),
            ["options"] = (Stats("s2", options: new CellStatsOptions(MinTrades: 10)), JobFixtures.Record(costs: Sheet())),
            ["exit label"] = (Stats("s3", "run1:intraday:TRAIL"), JobFixtures.Record(costs: Sheet())),
            ["timeframe"] = (Stats("s4"), JobFixtures.Record(costs: Sheet(), timeframe: "5m")),
            ["capital"] = (Stats("s5"), JobFixtures.Record(costs: Sheet(capital: 500_000m))),
            ["profile"] = (Stats("s6"), JobFixtures.Record(costs: Sheet(profile: "conservative"))),
            ["gross costs"] = (Stats("s7"), JobFixtures.Record()),
            ["run data options"] = (Stats("s8"), JobFixtures.Record(costs: Sheet()).Replace("\"min_session_completeness\":0.9", "\"min_session_completeness\":0.8", StringComparison.Ordinal)),
        };

        foreach (var (name, (stats, record)) in changed)
        {
            var after = await CellsAsync(stats, record);

            after["cells.basis"].ShouldNotBe(before["cells.basis"], name);
            PaperConfigKeys.Classify("cells.basis", before["cells.basis"], after["cells.basis"]).ShouldBe(ConfigClass.Frozen, name);
        }
    }

    [Fact]
    public async Task An_unreadable_source_fails_closed_the_cells_group_is_unreadable_and_nothing_is_guessed()
    {
        var cases = new (string Name, StoredCellStats Stats, bool RunMissing, bool RecordMissing, string? Record)[]
        {
            ("run missing", Stats("a"), true, false, null),
            ("record missing", Stats("b"), false, true, null),
            ("record unreadable", Stats("c"), false, false, "not json"),
            ("sources text malformed", Stats("d", "garbage"), false, false, null),
        };

        foreach (var (name, stats, runMissing, recordMissing, record) in cases)
        {
            var reading = await ReadAsync(stats, record, runMissing, recordMissing);

            reading.Unreadable.ShouldContain("cells", name);
            reading.Components.ShouldNotContain(c => c.Key == "cells.basis", name);
        }
    }
}
