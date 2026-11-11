using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// BK-11b criteria 8 to 10: one hypothesis from a stable tier observation and from nothing else (source universe, knob scan_scope, state proposed), a duplicate refused while one is open, the auto option OFF by default and, ON, proposing as the actor auto with the
/// setting change in the rationale; a scope hypothesis can never be started.
/// </summary>
public sealed class UniverseProposerTests
{
    private static readonly DateOnly AsOf = new(2026, 1, 31);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 20, MinTrades: 30);

    private static readonly CellId Orb = new("ORB", "15m", "BUY", "intraday", "FIXED_RR");

    private static readonly string Base = new('a', 64);

    private const string TierKey = "ORB|15m|BUY|intraday|FIXED_RR|*|liq_high";

    private const string ParentKey = "ORB|15m|BUY|intraday|FIXED_RR|*";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ICellStatsStore _cells = Substitute.For<ICellStatsStore>();

    private readonly IUniverseSettingsStore _settings = Substitute.For<IUniverseSettingsStore>();

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly FakeHypothesisLog _log = new();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 11, 0, 0, 0, TimeSpan.Zero));

    private static List<CellTrade> Trades(int wins, int losses) =>
        [.. Enumerable.Range(0, wins).Select(i => new CellTrade(AsOf.AddDays(-(i % 10)), 1, 0, 1)), .. Enumerable.Range(0, losses).Select(i => new CellTrade(AsOf.AddDays(-(i % 10)), -1, 0, 1))];

    private static IReadOnlyList<StoredCellStats> Stored(int childWins, int childLosses, int parentWins, int parentLosses)
    {
        var child = Trades(childWins, childLosses);
        var star = new CellInput(Orb, false, [.. child, .. Trades(parentWins - childWins, parentLosses - childLosses)]);
        var tier = new CellInput(Orb with { Universe = "liq_high" }, false, child);
        return [.. CellStatsCalculator.Compute([star], AsOf, Options, universeCells: [tier]).Select(r => new StoredCellStats(new string('s', 64), DateTimeOffset.UnixEpoch, Options, "x", r))];
    }

    public UniverseProposerTests()
    {
        _runs.FindAsync(Base, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(1, Base, "sim", 5, 5, "ORB", new string('c', 64), new DateOnly(2022, 1, 1), new DateOnly(2024, 12, 31), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{\"entry_limit_percent\":0.1,\"slippage\":{\"gap_fill\":\"open\",\"ticks\":1,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":1}}", null, null, 0, new string('d', 64), new string('e', 64), DateTimeOffset.UnixEpoch, "t"));
        _runs.ReadRecordTextAsync(Base, Arg.Any<CancellationToken>()).Returns("{\"strategy\":{\"id\":\"ORB\",\"parameters\":{\"or_min\":15,\"rr\":2},\"definition_json\":\"{}\"}}");
        Rows(Stored(88, 32, 100, 100));
        Settings(autoPropose: false);
    }

    private void Rows(IReadOnlyList<StoredCellStats> rows) => _cells.ListUniverseLatestAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(rows);

    private void Settings(bool autoPropose, int linkMin = 100) =>
        _settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(new UniverseSettingsState(
            new UniverseSettings(30, linkMin, UniverseSettings.Defaults.RegisteredBaskets, autoPropose),
            autoPropose ? [new StoredUniverseSettingChange(7, UniverseSettingsRules.AutoProposeKey, "false", "true", "owner:me@example.com", DateTimeOffset.UnixEpoch, new string('f', 64))] : [],
            autoPropose ? 7 : 0));

    private UniverseLinkProposer Proposer() => new(new ProposeHypothesisHandler(_runs, _log, _time, NullLogger<ProposeHypothesisHandler>.Instance), _log);

    private ProposeFromUniverseLinkHandler Manual() => new(_cells, _settings, Proposer());

    private AutoProposeUniverseLinksHandler Auto() => new(_cells, _settings, _log, _runs, Proposer(), NullLogger<AutoProposeUniverseLinksHandler>.Instance);

    [Fact]
    public async Task A_stable_observation_becomes_one_proposed_hypothesis_on_the_parent_cell_with_the_stored_numbers_and_nothing_is_applied()
    {
        var result = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "looks real", "owner:me@example.com"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("proposed");
        var row = _log.Rows.Single().Row;
        (row.Source, row.CellKey, row.KnobName, row.KnobKind, row.BaselineValue, row.CandidateValue, row.State).ShouldBe(("universe", ParentKey, "scan_scope", "Scope", "*", "liq_high", "proposed"));
        row.Rationale.ShouldStartWith("ORB, 15m: stronger on liq_high (lower bound ");
        row.Rationale.ShouldContain("n 120/200; hold-out stable)");
        row.Rationale.ShouldContain("owner: looks real");
        row.Actor.ShouldBe("owner:me@example.com");
    }

    [Theory]
    [InlineData(22, 8, "thin")]
    [InlineData(16, 14, "not read")]
    public async Task A_link_that_is_not_an_observation_is_refused_with_the_reason_and_nothing_is_written(int wins, int losses, string reason)
    {
        Rows(Stored(wins, losses, 100, 100));

        var result = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "x", "owner:me"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.NotAnObservationCode);
        result.Error.Message.ShouldContain(reason);
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_better_tier_whose_hold_out_is_insufficient_is_not_proposed()
    {
        Rows(Stored(22, 8, 100, 100));
        Settings(autoPropose: false, linkMin: 10);

        var result = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "x", "owner:me"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.NotAnObservationCode);
        result.Error.Message.ShouldContain("hold-out");
    }

    [Fact]
    public async Task An_unknown_cell_a_parent_cell_and_a_missing_reason_are_refused()
    {
        (await Manual().HandleAsync(new ProposeFromUniverseLinkCommand("ORB|15m|SELL|intraday|FIXED_RR|*|liq_low", Base, "x", "owner:me"), Ct)).Error.Code.ShouldBe(LearningErrors.CellNotFoundCode);
        (await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(ParentKey, Base, "x", "owner:me"), Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
        (await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, " ", "owner:me"), Ct)).Error.Code.ShouldBe(LearningErrors.InvalidCode);
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_proposal_of_the_same_tier_for_the_same_cell_while_one_is_open_is_a_conflict()
    {
        var first = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "x", "owner:me"), Ct);

        var second = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "again", "owner:me"), Ct);

        second.Error.Code.ShouldBe(LearningErrors.HypothesisExistsCode);
        second.Error.Kind.ShouldBe(ErrorKind.Conflict);
        _log.Rows.Count.ShouldBe(1);
        first.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task The_open_limit_applies_to_a_universe_proposal()
    {
        for (var i = 0; i < HypothesisLifecycle.DefaultMaxOpen; i++)
        {
            _log.Rows.Add(new HypothesisRow(i + 1, new NewHypothesisRow(null, "proposed", null, "owner", null, "rr", "Parameter", "2", "2.5", "r", Base, null, null, "proposed", "cli:xd", DateTimeOffset.UnixEpoch)));
        }

        var result = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "x", "owner:me"), Ct);

        result.Error.Code.ShouldBe(HypothesisErrors.OpenLimitCode);
    }

    [Fact]
    public async Task The_auto_option_is_off_by_default_and_then_nothing_is_proposed()
    {
        var result = await Auto().HandleAsync(new AutoProposeUniverseLinksCommand([new CellSource(Base, "intraday", "FIXED_RR")]), Ct);

        result.Value.ShouldBe(new AutoProposeOutcome(false, 0, 0, 0, false));
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_auto_option_on_a_new_observation_is_proposed_as_auto_with_the_setting_change_in_the_rationale_and_a_repeat_proposes_nothing()
    {
        Settings(autoPropose: true);
        var command = new AutoProposeUniverseLinksCommand([new CellSource(Base, "intraday", "FIXED_RR")]);

        var first = await Auto().HandleAsync(command, Ct);
        var again = await Auto().HandleAsync(command, Ct);

        first.Value.ShouldBe(new AutoProposeOutcome(true, 1, 0, 0, false));
        again.Value.ShouldBe(new AutoProposeOutcome(true, 0, 1, 0, false));
        var row = _log.Rows.Single().Row;
        row.Actor.ShouldBe("auto");
        HypothesisRows.IsOwner(row.Actor).ShouldBeFalse("never recorded as the owner");
        row.Source.ShouldBe("universe");
        row.Rationale.ShouldContain("learning.universe_link.auto_propose ON (change 7, hash ffffffffffff)");
    }

    [Fact]
    public async Task With_the_auto_option_on_a_tier_that_is_not_a_stable_observation_is_never_proposed()
    {
        Settings(autoPropose: true, linkMin: 10);
        Rows(Stored(22, 8, 100, 100));   // hold-out insufficient

        var result = await Auto().HandleAsync(new AutoProposeUniverseLinksCommand([new CellSource(Base, "intraday", "FIXED_RR")]), Ct);

        result.Value.Proposed.ShouldBe(0);
        _log.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_the_auto_option_on_an_observation_of_a_strategy_with_no_baseline_run_is_counted_and_left_alone()
    {
        Settings(autoPropose: true);

        var result = await Auto().HandleAsync(new AutoProposeUniverseLinksCommand([]), Ct);

        result.Value.ShouldBe(new AutoProposeOutcome(true, 0, 0, 1, false));
    }

    [Fact]
    public async Task A_scope_hypothesis_cannot_be_started()
    {
        var proposed = await Manual().HandleAsync(new ProposeFromUniverseLinkCommand(TierKey, Base, "x", "owner:me"), Ct);
        var start = new StartHypothesisHandler(_runs, _log, _time, NullLogger<StartHypothesisHandler>.Instance);

        var result = await start.HandleAsync(new StartHypothesisCommand(proposed.Value.Id, new string('b', 64), "cli:xd"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.KnobNotRunnableCode);
        result.Error.Message.ShouldContain("a scope experiment is not built");
        _log.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public void The_scope_knob_and_the_universe_source_have_the_names_the_column_checks_allow()
    {
        UniverseLinkProposal.Knob("liq_high").ShouldBe(new ExperimentKnob("scan_scope", ExperimentKnobKind.Scope, "*", "liq_high"));
        HypothesisSource.Universe.ToString().ToLowerInvariant().Length.ShouldBe(8, "the column is 8 characters wide");
        UniverseLinkProposal.Knob("liq_high").Problems().ShouldBeEmpty();
    }

    [Fact]
    public void The_notice_of_the_settings_says_the_auto_option_only_proposes()
    {
        UniverseSettingsView.NoticeText.ShouldContain("only proposes a hypothesis");
        UniverseSettings.Defaults.AutoPropose.ShouldBeFalse();
    }
}
