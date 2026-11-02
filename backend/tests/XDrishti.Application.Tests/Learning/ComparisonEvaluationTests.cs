using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning.Extensions;
using static XDrishti.Application.Tests.Learning.ComparisonKit;
using static XDrishti.Application.Tests.Learning.EvaluationKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Protocol X through the use case (P15-10): criterion 10 (the extraction record gates the verdict), criterion 4 (ledger first), criterion 5 (hold-out once), the honest NOT_EVALUABLE of today's data.</summary>
public sealed class ComparisonEvaluationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ExtensionGroup Ev = EventFeatureCatalog.Group;
    private static readonly ExtensionGroup Fu = FundamentalFeatureCatalog.Group;

    [Fact]
    public async Task Today_with_no_extraction_record_the_answer_is_not_evaluable_and_names_it_and_no_trial_is_counted()
    {
        var rig = NewRig();
        var source = new TrainingSource([new ExtensionRunSource(RunKey, "S", 10, null)], null);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:ev-1", false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("NOT_EVALUABLE");
        outcome.Records.Single().Missing.Single().ShouldStartWith("no_extraction: extraction records 0 of 1 runs");
        rig.Ledger.Rows.ShouldBeEmpty();
        source.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task A_planted_effect_with_a_synthetic_parser_is_not_evaluable_parser_unverified_and_the_rows_are_never_read()
    {
        var rig = NewRig();
        var data = Rows(Fu, true, 1);
        var source = new TrainingSource([Run(Fu, data.Rows.Count, "xbrl-1-synthetic")], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:fu-1", false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("NOT_EVALUABLE");
        outcome.Records.Single().Missing.Single().ShouldStartWith("parser_unverified");
        source.Loads.ShouldBe(0);
        rig.Ledger.Rows.ShouldBeEmpty();
        rig.Store.Records.Single().TrialHash.ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"nan\":{},\"parser_unverified\":{\"sue_yoy\":4}}")]
    [InlineData("garbage")]
    public async Task A_verified_version_does_not_help_when_a_feature_was_built_on_an_unverified_parser_or_the_counts_cannot_be_read(string counts)
    {
        var rig = NewRig();
        var data = Rows(Fu, true, 1);
        var source = new TrainingSource([Run(Fu, data.Rows.Count, "xbrl-1", counts)], data);

        var outcome = (await Evaluation(rig, source, "xbrl-1").RunAsync("ext:fu-1", false, Ct)).Value;

        outcome.Records.Single().Missing.Single().ShouldStartWith("parser_unverified");
    }

    [Fact]
    public async Task The_same_planted_effect_with_a_verified_parser_is_a_keep_and_the_trial_is_counted_before_the_verdict_is_stored()
    {
        var rig = NewRig();
        var data = Rows(Fu, true, 1);
        var source = new TrainingSource([Run(Fu, data.Rows.Count, "xbrl-1")], data);

        var outcome = (await Evaluation(rig, source, "xbrl-1").RunAsync("ext:fu-1", false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("KEEP");
        rig.Log.IndexOf("ledger.append").ShouldBeLessThan(rig.Log.IndexOf("store.append"));
        rig.Ledger.Rows.Single().StrategyId.ShouldBe("ext:fu-1");
        rig.Ledger.Rows.Single().Method.ShouldBe("extension-compare");
        rig.Store.Records.Single().InputsJson.ShouldContain("\"parser_version\":\"xbrl-1\"");
        rig.Store.Records.Single().Family.ShouldBe("ext:fu-1");
    }

    [Fact]
    public async Task A_rerun_appends_no_ledger_row_and_stores_nothing_new_with_the_same_id()
    {
        var rig = NewRig();
        var data = Rows(Ev, true, 1);
        var source = new TrainingSource([Run(Ev, data.Rows.Count, null)], data);
        var evaluation = Evaluation(rig, source);

        var first = (await evaluation.RunAsync("ext:ev-1", false, Ct)).Value;
        var second = (await evaluation.RunAsync("ext:ev-1", false, Ct)).Value;

        first.Records.Single().Stored.ShouldBeTrue();
        second.Records.Single().Stored.ShouldBeFalse();
        second.Records.Single().EvaluationId.ShouldBe(first.Records.Single().EvaluationId);
        rig.Ledger.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Random_labels_are_not_a_keep()
    {
        var rig = NewRig();
        var data = Rows(Ev, false, 2);
        var source = new TrainingSource([Run(Ev, data.Rows.Count, null)], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:ev-1", false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldNotBe("KEEP");
    }

    [Theory]
    [InlineData("lag")]
    [InlineData("rows")]
    public async Task Rows_extracted_with_another_lag_or_for_another_number_of_trades_are_stale(string what)
    {
        var rig = NewRig();
        var data = Rows(Ev, true, 1);
        var run = what == "lag" ? Run(Ev, data.Rows.Count, null, lag: 5) : Run(Ev, data.Rows.Count, null) with { ClosedTrades = data.Rows.Count + 1 };
        var source = new TrainingSource([run], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:ev-1", false, Ct)).Value;

        outcome.Records.Single().Missing.Single().ShouldStartWith("features_stale");
        source.Loads.ShouldBe(0);
    }

    [Fact]
    public async Task The_reserved_seasons_are_not_in_the_development_rows_and_the_flag_reads_them_once_after_a_keep()
    {
        var rig = NewRig();
        var data = Rows(Fu, true, 1);
        var source = new TrainingSource([Run(Fu, data.Rows.Count, "xbrl-1")], data);
        var evaluation = Evaluation(rig, source, "xbrl-1");

        var development = (await evaluation.RunAsync("ext:fu-1", false, Ct)).Value;
        var dev = rig.Store.Records.Single();
        rig.Store.Records.ShouldAllBe(r => !r.HoldOutRead);
        dev.Rows.ShouldBeLessThan(data.Rows.Count(r => r.TradeDate < new DateOnly(2023, 1, 1)) + 1);

        var read = (await evaluation.RunAsync("ext:fu-1", true, Ct)).Value;
        var again = await evaluation.RunAsync("ext:fu-1", true, Ct);

        development.Records.Single().Verdict.ShouldBe("KEEP");
        read.Records.Select(r => r.HoldOutRead).ShouldBe([false, true]);
        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("extensions.holdout_used");
    }

    [Fact]
    public async Task A_history_of_four_seasons_or_fewer_is_not_evaluable_with_the_seasons_named()
    {
        var rig = NewRig();
        var full = Rows(Ev, true, 1);
        var data = full with { Rows = [.. full.Rows.Where(r => r.TradeDate < new DateOnly(2019, 12, 1))] };
        var source = new TrainingSource([Run(Ev, data.Rows.Count, null)], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:ev-1", false, Ct)).Value;

        outcome.Records.Single().Missing.Single().ShouldStartWith("seasons 4 of 5");
        rig.Ledger.Rows.ShouldBeEmpty();
    }
}
