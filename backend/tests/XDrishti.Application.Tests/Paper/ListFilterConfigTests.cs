using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Autonomy;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Paper;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Tests.Paper;

/// <summary>AM-01a (ADR 0282) review M4: <c>Selection:ListFilters:Enabled</c> is a component of the paper fingerprint, so a later flip is a record and never silent, and its first appearance does not restart an open epoch.</summary>
public sealed class ListFilterConfigTests
{
    private const string Key = "smart_rules.list_filters.enabled";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ConfigSnapshotSource Source(bool enabled)
    {
        var settings = Substitute.For<IListFilterSettings>();
        settings.Enabled.Returns(enabled);
        return new ConfigSnapshotSource(
            Substitute.For<IPlanningSettingsReader>(), Substitute.For<IAutonomySettingsStore>(), Substitute.For<IAccountStore>(), Substitute.For<IRiskProfileStore>(), Substitute.For<ILearningChangelog>(),
            Substitute.For<IStrategyStore>(), Substitute.For<ICellStatsStore>(), Substitute.For<IModelRegistry>(), Substitute.For<ISimulationEngine>(), Substitute.For<IGradeSettings>(), Substitute.For<ICostSheetStore>(), eventFilter: null, listFilters: settings);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public async Task The_snapshot_lists_the_switch_as_read_from_the_settings(bool enabled, string expected)
    {
        var reading = await Source(enabled).ReadAsync(Ct);

        reading.Components.Single(c => c.Key == Key).Value.ShouldBe(expected);
    }

    [Fact]
    public void Turning_it_on_is_risk_down_and_turning_it_off_is_frozen()
    {
        PaperConfigKeys.Classify(Key, "false", "true").ShouldBe(ConfigClass.RiskDown);
        PaperConfigKeys.Classify(Key, "true", "false").ShouldBe(ConfigClass.Frozen, "a loosening restarts the count");
    }

    [Theory]
    [InlineData("true", ConfigClass.RiskDown)]
    [InlineData("false", ConfigClass.Free)]
    public void A_first_appearance_never_restarts_an_open_epoch(string after, ConfigClass expected)
    {
        PaperConfigKeys.Classify(Key, null, after).ShouldBe(expected);

        var changes = ConfigDiff.Compare(new Dictionary<string, string> { ["planning.x"] = "1" }, new Dictionary<string, string> { ["planning.x"] = "1", [Key] = after });
        EpochDecision.Decide([new DateOnly(2026, 11, 16)], true, false, changes, _ => 1).Outcome.ShouldBe(EpochOutcome.Noted);
    }
}
