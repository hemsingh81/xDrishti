using System.Reflection;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Autonomy;
using XDrishti.Domain.Paper;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Spec P10-05: fingerprint determinism, the closed classification, the direction table, the epoch decision and the explainer.</summary>
public sealed class ConfigGovernanceTests
{
    private static ConfigComponent C(string key, string value) => new(key, value);

    private static readonly DateOnly Mon = new(2026, 11, 9);

    [Fact]
    public void The_hash_does_not_depend_on_the_read_order_and_any_changed_value_changes_it()
    {
        var a = ConfigFingerprint.Create([C("b.x", "1"), C("a.y", "2")]);
        var b = ConfigFingerprint.Create([C("a.y", "2"), C("b.x", "1")]);
        var c = ConfigFingerprint.Create([C("a.y", "2"), C("b.x", "3")]);

        a.Hash.ShouldBe(b.Hash);
        a.Hash.Length.ShouldBe(64);
        c.Hash.ShouldNotBe(a.Hash);
        a.CanonicalJson.ShouldBe("{\"a.y\":\"2\",\"b.x\":\"1\"}");
        ConfigFingerprint.FromJson(a.CanonicalJson)!.Hash.ShouldBe(a.Hash);
    }

    [Fact]
    public void A_component_named_like_a_secret_is_dropped_and_the_canonical_text_names_none()
    {
        var f = ConfigFingerprint.Create([C("broker.api_key", "x"), C("auth.token", "y"), C("owner.password", "z"), C("autonomy.max_auto_per_day", "3")]);

        f.Dropped.ShouldBe(3);
        foreach (var word in new[] { "token", "key", "secret", "password" })
        {
            f.CanonicalJson.ShouldNotContain(word, Case.Insensitive);
        }
    }

    [Fact]
    public void Two_components_with_one_key_are_refused()
    {
        Should.Throw<ArgumentException>(() => ConfigFingerprint.Create([C("a", "1"), C("a", "2")]));
    }

    private static IEnumerable<string> Properties(Type type, bool declaredOnly = true) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance | (declaredOnly ? BindingFlags.DeclaredOnly : 0)).Where(p => p.Name != "EqualityContract").Select(p => p.Name);

    [Fact]
    public void Every_property_of_the_typed_settings_is_in_a_table_or_the_explicit_free_list_with_a_reason()
    {
        string[] Covered(Type t, params IReadOnlyDictionary<string, ConfigDirection>[] tables) =>
            [.. Properties(t).Where(p => !tables.Any(x => x.ContainsKey(p)) && !PaperConfigKeys.Folded.ContainsKey(p))];

        Covered(typeof(TradingSettings), PaperConfigKeys.TradingFields).ShouldBeEmpty();
        Covered(typeof(RiskLimits), PaperConfigKeys.RiskLimitFields).ShouldBeEmpty();
        Covered(typeof(RiskProfile), PaperConfigKeys.RiskLimitFields).ShouldBeEmpty();
        Covered(typeof(SmartRulesOptions), PaperConfigKeys.SmartRuleFields).ShouldBeEmpty();
        Covered(typeof(AutonomySettings), PaperConfigKeys.AutonomyFields).ShouldBeEmpty();
        PaperConfigKeys.Folded.Values.ShouldAllBe(reason => reason.Length > 5);
    }

    [Fact]
    public void The_selection_rules_are_read_whole_so_a_new_one_is_frozen_by_its_prefix()
    {
        Properties(typeof(SelectionRules)).ShouldNotBeEmpty();
        PaperConfigKeys.Classify(PaperConfigKeys.SelectionPrefix + "a_new_threshold", "1", "2").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Classify("planning.anything:new", "a", "b").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Classify("some.unknown.key", "a", "b").ShouldBe(ConfigClass.Frozen);
    }

    [Theory]
    [InlineData("accounts.paper/a1/risk_per_trade_pct", "1.0", "0.5", ConfigClass.RiskDown)]
    [InlineData("accounts.paper/a1/risk_per_trade_pct", "0.5", "1.0", ConfigClass.Frozen)]
    [InlineData("accounts.paper/a1/max_open_positions", "5", "3", ConfigClass.RiskDown)]
    [InlineData("accounts.paper/a1/max_open_positions", "3", "5", ConfigClass.Frozen)]
    [InlineData("accounts.paper/a1/enabled", "true", "false", ConfigClass.RiskDown)]
    [InlineData("accounts.paper/a1/enabled", "false", "true", ConfigClass.Frozen)]
    [InlineData("accounts.paper/a1/capital_amount", "100", "50", ConfigClass.Frozen)]
    [InlineData("autonomy.auto_approve_tickets", "false", "true", ConfigClass.Frozen)]
    [InlineData("autonomy.auto_approve_tickets", "true", "false", ConfigClass.RiskDown)]
    [InlineData("autonomy.max_auto_per_day", "3", "5", ConfigClass.Frozen)]
    [InlineData("autonomy.min_p_win", "0.58", "0.7", ConfigClass.RiskDown)]
    [InlineData("autonomy.row_hash", "a", "b", ConfigClass.Free)]
    [InlineData("smart_rules.gate_enabled", "false", "true", ConfigClass.RiskDown)]
    [InlineData("smart_rules.gate_enabled", "true", "false", ConfigClass.Frozen)]
    [InlineData("smart_rules.correlation_max", "0.6", "0.8", ConfigClass.Frozen)]
    [InlineData("smart_rules.honesty_fraction", "0.5", "0.4", ConfigClass.Frozen)]
    [InlineData("cells.snapshot", "a", "b", ConfigClass.Frozen)]
    [InlineData("models.champion", "1", "2", ConfigClass.Frozen)]
    [InlineData("engine.version", "sim-6", "sim-7", ConfigClass.Frozen)]
    [InlineData("planning.builder:useselection", "false", "true", ConfigClass.Frozen)]
    [InlineData("planning.openbook:assumedempty", "true", "false", ConfigClass.Frozen)]
    public void The_direction_of_a_change_decides_its_class(string key, string before, string after, ConfigClass expected) =>
        PaperConfigKeys.Classify(key, before, after).ShouldBe(expected);

    [Fact]
    public void A_cell_added_or_with_a_new_strategy_hash_is_frozen_and_a_removed_cell_is_risk_down()
    {
        PaperConfigKeys.Classify("cells.eligible/c", null, "h1").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Classify("cells.eligible/c", "h1", "h2").ShouldBe(ConfigClass.Frozen);
        PaperConfigKeys.Classify("cells.eligible/c", "h1", null).ShouldBe(ConfigClass.RiskDown);
    }

    [Fact]
    public void The_diff_lists_added_removed_and_changed_keys_in_order_and_nothing_for_equal_sets()
    {
        var before = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["cells.eligible/x"] = "h" };
        var after = new Dictionary<string, string> { ["a"] = "1", ["b"] = "3", ["c"] = "4" };

        var diff = ConfigDiff.Compare(before, after);

        diff.Select(d => d.Key).ShouldBe(["b", "c", "cells.eligible/x"]);
        diff[2].Class.ShouldBe(ConfigClass.RiskDown);
        ConfigDiff.Compare(before, before).ShouldBeEmpty();
    }

    private static ConfigChange Change(ConfigClass c) => new("k", c, "1", "2");

    private static int? Since(DateOnly _) => 1;

    [Fact]
    public void Equal_snapshots_measure_the_sessions()
    {
        var d = EpochDecision.Decide([Mon], true, false, [], Since);

        d.Outcome.ShouldBe(EpochOutcome.Unchanged);
        d.MeasureStatus[Mon].ShouldBe("measured");
    }

    [Fact]
    public void Only_risk_down_or_free_differences_keep_the_epoch()
    {
        var d = EpochDecision.Decide([Mon], true, false, [Change(ConfigClass.RiskDown), Change(ConfigClass.Free)], Since);

        d.Outcome.ShouldBe(EpochOutcome.Noted);
        d.MeasureStatus[Mon].ShouldBe("measured");
    }

    [Fact]
    public void A_frozen_difference_sets_every_session_of_a_catch_up_aside()
    {
        DateOnly[] sessions = [Mon, Mon.AddDays(1), Mon.AddDays(2)];

        var d = EpochDecision.Decide(sessions, true, false, [Change(ConfigClass.RiskDown), Change(ConfigClass.Frozen)], Since);

        d.Outcome.ShouldBe(EpochOutcome.NewEpoch);
        d.MeasureStatus.Values.ShouldAllBe(v => v == "straddle");
        d.MeasureStatus.Count.ShouldBe(3);
    }

    [Fact]
    public void Without_an_earlier_snapshot_no_change_is_claimed()
    {
        EpochDecision.Decide([Mon], false, false, [], Since).Outcome.ShouldBe(EpochOutcome.FirstSnapshot);
    }

    [Theory]
    [InlineData(3, "measured")]
    [InlineData(4, "straddle")]
    public void A_blind_read_counts_only_within_three_sessions_of_the_last_full_one(int sessionsSince, string expected)
    {
        var d = EpochDecision.Decide([Mon], true, true, [], _ => sessionsSince);

        d.Outcome.ShouldBe(EpochOutcome.Blind);
        d.MeasureStatus[Mon].ShouldBe(expected);
        EpochDecision.Decide([Mon], false, true, [], _ => null).MeasureStatus[Mon].ShouldBe("straddle");
    }

    private const string Cell20 = "ORB|15m|BUY|INTRADAY|FIXED_RR{rr=2.0}|*";

    private const string Cell25 = "ORB|15m|BUY|INTRADAY|FIXED_RR{rr=2.5}|*";

    private static readonly DateTimeOffset Prev = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Detected = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);

    private static IReadOnlyDictionary<string, string?> Explain(IReadOnlyList<ConfigChange> changes, IReadOnlyList<AcceptedHypothesis> hypotheses, IReadOnlySet<long>? used = null, IReadOnlyList<ChangelogFact>? log = null, Func<string, IReadOnlyDictionary<string, string>?>? parameters = null) =>
        ChangeExplainer.Explain(changes, hypotheses, used ?? new HashSet<long>(), log ?? [], parameters ?? (_ => null), Prev, Detected);

    [Fact]
    public void A_new_exit_label_is_explained_by_the_accepted_hypothesis_of_the_hand_fixture()
    {
        var change = new ConfigChange("cells.eligible/" + Cell25, ConfigClass.Frozen, null, "h3");
        var h7 = new AcceptedHypothesis(7, Cell20, "Exit", "exit", Cell25.Split('|')[4], new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));

        Explain([change], [h7])[change.Key].ShouldBe("hypothesis:7");
    }

    [Fact]
    public void A_parameter_hypothesis_matches_the_stored_parameter_of_the_new_version_or_the_value_in_the_new_exit()
    {
        var accepted = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);
        var sameCell = new ConfigChange("cells.eligible/" + Cell20, ConfigClass.Frozen, "h1", "h2");
        var byValue = new ConfigChange("cells.eligible/" + Cell25, ConfigClass.Frozen, null, "h3");

        Explain([sameCell], [new AcceptedHypothesis(1, Cell20, "Parameter", "period", "14", accepted)], parameters: _ => new Dictionary<string, string> { ["period"] = "14.0" })[sameCell.Key].ShouldBe("hypothesis:1");
        Explain([byValue], [new AcceptedHypothesis(2, Cell20, "Parameter", "rr", "2.5", accepted)])[byValue.Key].ShouldBe("hypothesis:2");
        Explain([sameCell], [new AcceptedHypothesis(3, Cell20, "Parameter", "period", "20", accepted)], parameters: _ => new Dictionary<string, string> { ["period"] = "14" })[sameCell.Key].ShouldBeNull();
    }

    [Fact]
    public void A_hypothesis_accepted_after_detection_or_already_used_does_not_explain()
    {
        var change = new ConfigChange("cells.eligible/" + Cell25, ConfigClass.Frozen, null, "h3");
        var exit = Cell25.Split('|')[4];

        Explain([change], [new AcceptedHypothesis(7, Cell20, "Exit", "exit", exit, Detected.AddDays(1))])[change.Key].ShouldBeNull();
        Explain([change], [new AcceptedHypothesis(7, Cell20, "Exit", "exit", exit, Detected.AddDays(-1))], new HashSet<long> { 7 })[change.Key].ShouldBeNull();
    }

    [Fact]
    public void One_hypothesis_explains_one_change_only_and_a_changelog_row_between_the_snapshots_is_the_fallback()
    {
        var a = new ConfigChange("cells.eligible/" + Cell25, ConfigClass.Frozen, null, "h3");
        var b = new ConfigChange("cells.eligible/" + Cell25.Replace("ORB", "VWAP", StringComparison.Ordinal), ConfigClass.Frozen, null, "h4");
        var h = new AcceptedHypothesis(7, Cell20, "Exit", "exit", Cell25.Split('|')[4], Prev);
        var log = new[] { new ChangelogFact(55, b.Key["cells.eligible/".Length..], Prev.AddDays(1)), new ChangelogFact(56, "other", Prev.AddDays(1)) };

        var result = Explain([a, b], [h], log: log);

        result[a.Key].ShouldBe("hypothesis:7");
        result[b.Key].ShouldBe("changelog:55");
    }

    [Fact]
    public void Settings_accounts_and_autonomy_are_never_explained_by_a_hypothesis()
    {
        var change = new ConfigChange("autonomy.auto_approve_tickets", ConfigClass.Frozen, "false", "true");

        Explain([change], [new AcceptedHypothesis(1, Cell20, "Exit", "exit", "x", Prev)])[change.Key].ShouldBeNull();
    }

    [Fact]
    public void The_snake_names_follow_the_property_names()
    {
        PaperConfigKeys.Snake("MaxAutoPerDay").ShouldBe("max_auto_per_day");
        PaperConfigKeys.Snake("MinPWin").ShouldBe("min_p_win");
        PaperConfigKeys.Snake("RiskPerTradePct").ShouldBe("risk_per_trade_pct");
    }
}
