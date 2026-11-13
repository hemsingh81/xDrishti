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
    private static readonly ExtensionGroup Iv = IvFeatureCatalog.Group;

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
    public async Task Evidence_set_registration_rows_of_the_family_change_neither_the_trial_hashes_nor_the_stored_inputs()
    {
        var data = Rows(Fu, true, 1);
        var source = new TrainingSource([Run(Fu, data.Rows.Count, "xbrl-1")], data);
        var plain = NewRig();
        await Evaluation(plain, source, "xbrl-1").RunAsync("ext:fu-1", false, Ct);

        var registered = NewRig();
        for (var i = 0; i < 4; i++)
        {
            registered.Ledger.Rows.Add(new XDrishti.Application.Abstractions.Lab.LedgerRow(Guid.NewGuid(), new string('k', 64), "ext:fu-1", 1, new string((char)('b' + i), 64), null, "u", new DateOnly(2025, 5, 1), new DateOnly(2026, 4, 30), "evidence_set", DateTimeOffset.UnixEpoch, XDrishti.Domain.Shadow.EvidenceSetRules.TrialKind));
        }

        await Evaluation(registered, source, "xbrl-1").RunAsync("ext:fu-1", false, Ct);

        registered.Store.Records.Single().Trials.ShouldBe(plain.Store.Records.Single().Trials);
        registered.Store.Records.Single().InputsJson.ShouldBe(plain.Store.Records.Single().InputsJson);
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

    private const string IvCounts = "{\"nan\":{},\"parser_unverified\":{},\"sources\":{\"archive_watermark\":\"42\",\"iv_unit\":\"percent_assumed\",\"quality_run_ids\":\"3,5\"}}";

    [Fact]
    public async Task The_iv_family_runs_protocol_x_on_the_options_preset_with_the_ledger_first_and_names_the_archive_sources_in_the_record()
    {
        var rig = NewRig();
        var data = Rows(Iv, true, 1);
        var source = new TrainingSource([Run(Iv, data.Rows.Count, null, IvCounts, lag: 0)], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:iv-1", false, Ct)).Value;

        var record = outcome.Records.Single();
        record.Verdict.ShouldNotBe("NOT_EVALUABLE", string.Join("; ", record.Missing));
        rig.Log.IndexOf("ledger.append").ShouldBeLessThan(rig.Log.IndexOf("store.append"));
        rig.Ledger.Rows.Single().StrategyId.ShouldBe("ext:iv-1");
        var stored = rig.Store.Records.Single();
        stored.VersionKey.ShouldStartWith("iv-1:iv_atm,iv_rank_252");
        stored.InputsJson.ShouldContain("\"extension_version\":\"iv-1\"");
        stored.InputsJson.ShouldContain("\"archive_watermark\":\"42\"");
        stored.InputsJson.ShouldContain("\"quality_run_ids\":\"3,5\"");
        stored.InputsJson.ShouldContain("\"iv_unit\":\"percent_assumed\"");
        stored.InputsJson.ShouldContain("\"calendar_sha256\":\"" + new string('c', 64) + "\"");
        stored.EvidenceJson.ShouldNotContain("\"blocks_sensitivity\":0", Case.Sensitive, "the options preset runs the 5-session sensitivity that the filings preset does not");
        stored.FoldUnit.ShouldBe("quarter");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(5, true)]
    [InlineData(30, true)]
    public async Task An_iv_extraction_must_have_lag_zero_whatever_the_calendar_lag_in_force_is(int lag, bool stale)
    {
        var rig = NewRig();
        var data = Rows(Iv, true, 1);
        var source = new TrainingSource([Run(Iv, data.Rows.Count, null, IvCounts, lag: lag)], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:iv-1", false, Ct)).Value;

        if (stale)
        {
            outcome.Records.Single().Missing.Single().ShouldStartWith("features_stale");
            source.Loads.ShouldBe(0);
        }
        else
        {
            outcome.Records.Single().Missing.ShouldNotContain(m => m.StartsWith("features_stale", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Today_with_no_iv_extraction_the_answer_is_not_evaluable_and_names_the_verb_and_no_trial_is_counted()
    {
        var rig = NewRig();
        var source = new TrainingSource([new ExtensionRunSource(RunKey, "S", 10, null)], null);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:iv-1", false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("NOT_EVALUABLE");
        outcome.Records.Single().Missing.Single().ShouldContain("--extension iv-1");
        rig.Ledger.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_iv_hold_out_is_read_once_after_a_development_keep_and_never_for_the_other_families_key()
    {
        var rig = NewRig();
        var data = Rows(Iv, true, 1);
        var source = new TrainingSource([Run(Iv, data.Rows.Count, null, IvCounts, lag: 0)], data);
        var evaluation = Evaluation(rig, source);

        var development = (await evaluation.RunAsync("ext:iv-1", false, Ct)).Value;
        rig.Store.Records.ShouldAllBe(r => !r.HoldOutRead);
        if (development.Records.Single().Verdict != "KEEP")
        {
            var refused = (await evaluation.RunAsync("ext:iv-1", true, Ct)).Value;
            refused.Note.ShouldNotBeNull().ShouldContain("not read");
            rig.Store.Records.ShouldAllBe(r => !r.HoldOutRead, "no development KEEP: the hold-out is not read");
            return;
        }

        var read = (await evaluation.RunAsync("ext:iv-1", true, Ct)).Value;
        var again = await evaluation.RunAsync("ext:iv-1", true, Ct);

        read.Records.Select(r => r.HoldOutRead).ShouldBe([false, true]);
        again.Error.Code.ShouldBe("extensions.holdout_used");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("unavailable")]
    public async Task Rows_extracted_from_an_empty_archive_are_stale_and_no_trial_is_counted(string watermark)
    {
        var rig = NewRig();
        var data = Rows(Iv, true, 1);
        var counts = "{\"nan\":{},\"parser_unverified\":{},\"sources\":{\"archive_watermark\":\"" + watermark + "\",\"iv_unit\":\"percent_assumed\",\"quality_run_ids\":\"none\"}}";
        var source = new TrainingSource([Run(Iv, data.Rows.Count, null, watermark == "unavailable" ? Clean : counts, lag: 0)], data);

        var outcome = (await Evaluation(rig, source).RunAsync("ext:iv-1", false, Ct)).Value;

        outcome.Records.Single().Verdict.ShouldBe("NOT_EVALUABLE");
        outcome.Records.Single().Missing.Single().ShouldContain("features_stale: run ");
        outcome.Records.Single().Missing.Single().ShouldContain("extracted from an empty archive");
        source.Loads.ShouldBe(0);
        rig.Ledger.Rows.ShouldBeEmpty();
    }
}
