using System.Text.Json;
using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P14-17 criteria 1, 2, 3, 5 and 6: the verdict table, stale and gross records, the cut families, honest output (no digit for a non-value, the limits always, the JSON with the same fields) and determinism.</summary>
public sealed class OptionsPhaseCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] NoCut = [];

    private static Dictionary<string, string> Versions(string remark = "expr-1") => new Dictionary<string, string> { ["remark_version"] = remark, ["engine"] = "sim-9" };

    private static string ProtocolOf(string family) => family switch
    {
        ExpressionFamilies.IronCondor => "R",
        PairFamilies.Intraday => "S",
        IvFeatureCatalog.Family => "X",
        _ => "O",
    };

    private static OptionsRecordFact Record(string id, string verdict, string protocol, bool holdOut = false, int rows = 500, IReadOnlyList<string>? missing = null, string? basis = null, string remark = "expr-1", OptionsWatermarkFact? watermark = null, string? developmentRecord = null) =>
        new(id, protocol, verdict, holdOut, 3, rows, missing ?? [], basis ?? "net", Versions(remark), Versions(), watermark, developmentRecord, 0.25, 0.04);

    private static OptionsFamilyFacts Family(string family, params OptionsRecordFact[] records) => new(family, true, records);

    private static OptionsFamilyFacts Dropped(string family) => Family(family, Record("drop-" + family, "DROP", ProtocolOf(family)));

    private static OptionsFamilyFacts Kept(string family) => Family(family, Record("hold-" + family, "KEEP", ProtocolOf(family), holdOut: true, developmentRecord: "dev-" + family), Record("dev-" + family, "KEEP", ProtocolOf(family)));

    private static OptionsPhaseFacts Facts(IEnumerable<OptionsFamilyFacts> families, IReadOnlyList<OptionsCellRow>? rows = null, bool cellsReadable = true, bool recorderOn = true, double? years = 2.1) =>
        new([.. families], new OptionsCellFacts(cellsReadable, rows ?? [], false), new OptionsArchiveFacts(years, [new OptionsUnderlyingFact("NIFTY", 12, 40, 2)], recorderOn, false), Now, 90, Now);

    private static OptionsPhaseFacts AllDropped(Func<string, OptionsFamilyFacts>? make = null) =>
        Facts(OptionsPhaseCheck.Families.Select(make ?? Dropped));

    [Fact]
    public void Every_expression_family_dropped_and_the_rest_dropped_or_cut_is_met_as_stays_disabled()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped(), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.Met);
        report.Summary.ShouldContain("option expressions stay disabled");
        report.Open.ShouldBeEmpty();
        report.Violations.ShouldBeEmpty();
        report.ArchiveNote.ShouldNotBeNull().ShouldContain("archive still recording after DROP: owner may switch it off");
        report.Cut.ShouldBe(NoCut);
    }

    [Fact]
    public void The_archive_note_is_information_when_the_recorder_is_off_and_absent_when_a_family_is_kept()
    {
        var off = OptionsPhaseCheck.Evaluate(Facts(OptionsPhaseCheck.Families.Select(Dropped), recorderOn: false), NoCut);
        var kept = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? Kept(f) : Dropped(f)), NoCut);

        off.ArchiveNote.ShouldNotBeNull().ShouldContain("not recording");
        kept.ArchiveNote.ShouldBeNull();
    }

    [Fact]
    public void An_option_buy_keep_confirmed_on_the_hold_out_with_the_rest_resolved_is_met()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? Kept(f) : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.Met);
        report.Families.Single(f => f.Family == ExpressionFamilies.OptionBuy).Resolved.ShouldBeTrue();
        report.Families.Single(f => f.Family == ExpressionFamilies.OptionBuy).HoldOut.ShouldBe("read");
    }

    [Fact]
    public void Another_expression_family_kept_while_option_buy_is_dropped_is_met()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.DebitSpread ? Kept(f) : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public void A_development_keep_without_a_hold_out_record_is_not_evaluable_with_hold_out_not_read()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? Family(f, Record("dev", "KEEP", "O")) : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("hold-out not read");
        var line = report.Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);
        (line.Status, line.Resolved, line.HoldOut).ShouldBe(("KEEP", false, "not read"));
    }

    [Fact]
    public void A_hold_out_that_did_not_confirm_leaves_the_family_open()
    {
        var family = Family(ExpressionFamilies.OptionBuy, Record("hold", "NOT_EVALUABLE", "O", holdOut: true, missing: ["hold-out paired trades 40 of 100"], developmentRecord: "dev"), Record("dev", "KEEP", "O"));

        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? family : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("hold-out read, not confirmed");
        report.Open[0].ShouldContain("hold-out paired trades 40 of 100");
    }

    [Fact]
    public void One_not_evaluable_family_makes_the_check_not_evaluable_and_names_it_and_its_missing_quantity()
    {
        var family = Family(ExpressionFamilies.CreditSpread, Record("c1", "NOT_EVALUABLE", "O", rows: 0, missing: ["folds 0 of 6", "spread_unavailable"]));

        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.CreditSpread ? family : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("expr:credit_spread");
        report.Open[0].ShouldContain("folds 0 of 6");
        report.ToText().ShouldContain("spread_unavailable");
    }

    [Fact]
    public void A_non_cash_stored_plan_item_is_not_met_and_names_the_row()
    {
        var report = OptionsPhaseCheck.Evaluate(Facts(OptionsPhaseCheck.Families.Select(Dropped), [new OptionsCellRow("trading.plan_items", 4711)]), []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        report.Violations.ShouldHaveSingleItem().Id.ShouldBe(4711);
        report.ToText().ShouldContain("trading.plan_items row 4711");
        report.ToJson().ShouldContain("\"row_id\": 4711");
    }

    [Fact]
    public void A_violation_is_judged_even_when_the_family_is_cut_and_a_paper_entry_counts_too()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped() with { Cells = new OptionsCellFacts(true, [new OptionsCellRow("paper.entries", 9)], false) }, [.. NoCut, ExpressionFamilies.IronCondor]);

        report.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        report.Violations.ShouldHaveSingleItem().Source.ShouldBe("paper.entries");
    }

    [Fact]
    public void Unreadable_plan_rows_can_never_be_met()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped() with { Cells = new OptionsCellFacts(false, [], false) }, NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("could not be read");
        report.ToText().ShouldContain("unavailable: the stored plan items and paper entries could not be read");
        report.ToJson().ShouldContain("\"violations\": \"unavailable\"");
    }

    [Fact]
    public void No_record_at_all_is_not_evaluable_with_the_plain_sentence()
    {
        var report = OptionsPhaseCheck.Evaluate(Facts(OptionsPhaseCheck.Families.Select(f => Family(f))), []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Summary.ShouldBe("no P14 evaluation stored.");
        report.Families.ShouldAllBe(f => f.Status == OptionsPhaseCheck.StatusNone);
    }

    [Fact]
    public void The_basis_carry_family_is_not_a_family_and_the_six_decide_the_exit_without_a_cut()
    {
        OptionsPhaseCheck.Families.Count.ShouldBe(6);
        OptionsPhaseCheck.Families.ShouldNotContain("basis:carry-1");
        OptionsPhaseCheck.Evaluate(AllDropped(), []).Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public void A_keep_for_an_older_remark_version_is_stale_and_open()
    {
        var family = Family(ExpressionFamilies.OptionBuy, Record("old", "KEEP", "O", remark: "expr-0"));

        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? family : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        var line = report.Families.Single(f => f.Family == ExpressionFamilies.OptionBuy);
        (line.Status, line.Resolved).ShouldBe((OptionsPhaseCheck.StatusStale, false));
        line.Detail.ShouldContain("remark_version expr-0, current expr-1");
    }

    [Theory]
    [InlineData("engine", "sim-8")]
    [InlineData("spread_model", "model-b")]
    [InlineData("cost_sheet", "sha-b")]
    [InlineData("profile", "profile-b")]
    public void Each_version_that_differs_or_is_not_recorded_makes_the_record_stale(string key, string recorded)
    {
        var current = new Dictionary<string, string>(Versions()) { [key] = "now" };
        var recordedVersions = new Dictionary<string, string>(Versions()) { [key] = recorded };
        var differing = Record("r", "DROP", "O") with { Recorded = recordedVersions, Current = current };
        var withoutKey = Versions();
        withoutKey.Remove(key);
        var missing = Record("r", "DROP", "O") with { Recorded = withoutKey, Current = current };

        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? Family(f, differing) : Dropped(f)), NoCut).Families[0].Status.ShouldBe(OptionsPhaseCheck.StatusStale);
        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? Family(f, missing) : Dropped(f)), NoCut).Families[0].Detail.ShouldContain(key + " not recorded");
    }

    [Fact]
    public void An_archive_that_moved_on_more_than_the_bound_from_the_watermark_is_stale_and_a_stopped_archive_does_not_age_a_verdict()
    {
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(10, Now.AddDays(-91)), Now), NoCut).Families[0].Status.ShouldBe(OptionsPhaseCheck.StatusStale);
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(10, Now.AddDays(-91)), Now), NoCut).Families[0].Detail.ShouldContain("more than 90 days behind the newest fetch");
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(10, null), Now), NoCut).Families[0].Detail.ShouldContain("archive watermark not found");
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(10, Now.AddDays(-89)), Now), NoCut).Verdict.ShouldBe(PhaseVerdicts.Met);
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(0, null), Now), NoCut).Verdict.ShouldBe(PhaseVerdicts.Met);
        // the archive stopped long ago: the watermark is the newest fetch, so the verdict is still current a year later
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(10, Now.AddDays(-400)), Now.AddDays(-400)) with { Now = Now.AddDays(300) }, NoCut).Verdict.ShouldBe(PhaseVerdicts.Met);
        // no fetch stored at all: a record that rests on the archive is stale
        OptionsPhaseCheck.Evaluate(WithWatermark(new OptionsWatermarkFact(10, Now), null), NoCut).Families[0].Detail.ShouldContain("no archive fetch is stored");

        static OptionsPhaseFacts WithWatermark(OptionsWatermarkFact mark, DateTimeOffset? latest) => AllDropped(f => f == ExpressionFamilies.OptionBuy ? Family(f, Record("d", "DROP", "O", watermark: mark)) : Dropped(f)) with { LatestFetchEndedAt = latest };
    }

    [Fact]
    public void An_iv_record_with_an_old_archive_watermark_is_stale()
    {
        var old = Record("iv", "DROP", "X", basis: "unavailable (protocol X may be gross)", watermark: new OptionsWatermarkFact(5, Now.AddDays(-120)));

        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == IvFeatureCatalog.Family ? Family(f, old) : Dropped(f)), NoCut);

        var line = report.Families.Single(f => f.Family == IvFeatureCatalog.Family);
        (line.Status, line.Resolved).ShouldBe((OptionsPhaseCheck.StatusStale, false));
    }

    [Fact]
    public void A_hold_out_confirms_only_the_development_record_it_was_read_for()
    {
        var family = Family(ExpressionFamilies.OptionBuy, Record("dev-new", "KEEP", "O"), Record("hold", "KEEP", "O", holdOut: true, developmentRecord: "dev-old"));

        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? family : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("hold-out read for record dev-old, not for the newest development record dev-new");
        var named = Family(ExpressionFamilies.OptionBuy, Record("dev-new", "KEEP", "O"), Record("hold", "KEEP", "O", holdOut: true, developmentRecord: "dev-new"));
        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? named : Dropped(f)), NoCut).Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public void A_comparison_keep_needs_a_stored_net_and_a_comparison_drop_stands_without_one()
    {
        const string Unknown = "unavailable (protocol X may be gross)";
        var gross = Family(IvFeatureCatalog.Family, Record("dev", "KEEP", "X", basis: Unknown), Record("hold", "KEEP", "X", holdOut: true, basis: Unknown, developmentRecord: "dev"));
        var net = Family(IvFeatureCatalog.Family, Record("dev", "KEEP", "X", basis: "net"), Record("hold", "KEEP", "X", holdOut: true, basis: "net", developmentRecord: "dev"));
        var drop = Family(IvFeatureCatalog.Family, Record("d", "DROP", "X", basis: Unknown));

        OptionsPhaseCheck.Evaluate(AllDropped(f => f == IvFeatureCatalog.Family ? gross : Dropped(f)), NoCut).Open.ShouldHaveSingleItem().ShouldContain("net is required for a KEEP");
        OptionsPhaseCheck.Evaluate(AllDropped(f => f == IvFeatureCatalog.Family ? net : Dropped(f)), NoCut).Verdict.ShouldBe(PhaseVerdicts.Met);
        var dropped = OptionsPhaseCheck.Evaluate(AllDropped(f => f == IvFeatureCatalog.Family ? drop : Dropped(f)), NoCut);
        dropped.Verdict.ShouldBe(PhaseVerdicts.Met);
        dropped.ToText().ShouldContain("costs unavailable (protocol X may be gross)");
    }

    [Theory]
    [InlineData("KEEP", "gross")]
    [InlineData("DROP", "gross")]
    [InlineData("DROP", "mixed")]
    [InlineData("KEEP", "none")]
    public void A_record_on_a_gross_or_mixed_basis_is_never_resolved_for_the_expression_protocols(string word, string basis)
    {
        var family = Family(ExpressionFamilies.OptionBuy, Record("g", word, "O", basis: basis), Record("h", "KEEP", "O", holdOut: true, basis: basis, developmentRecord: "g"));

        var report = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? family : Dropped(f)), NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain($"costs are {basis}, net is required");
    }

    [Fact]
    public void A_gross_pairs_drop_is_valid_and_a_gross_pairs_keep_is_not()
    {
        var drop = OptionsPhaseCheck.Evaluate(AllDropped(f => f == PairFamilies.Intraday ? Family(f, Record("s", "DROP", "S", basis: "gross")) : Dropped(f)), NoCut);
        var keep = OptionsPhaseCheck.Evaluate(AllDropped(f => f == PairFamilies.Intraday ? Family(f, Record("s", "KEEP", "S", basis: "gross"), Record("h", "KEEP", "S", holdOut: true, basis: "gross", developmentRecord: "s")) : Dropped(f)), NoCut);

        drop.Verdict.ShouldBe(PhaseVerdicts.Met);
        keep.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public void A_drop_with_missing_quantities_or_no_rows_is_thin_and_open()
    {
        var thinRows = Family(ExpressionFamilies.OptionBuy, Record("t", "DROP", "O", rows: 0));
        var thinMissing = Family(ExpressionFamilies.OptionBuy, Record("t", "DROP", "O", missing: ["latest epoch n 12 of 30"]));

        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? thinRows : Dropped(f)), NoCut).Open.ShouldHaveSingleItem().ShouldContain("no rows behind it");
        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? thinMissing : Dropped(f)), NoCut).Open.ShouldHaveSingleItem().ShouldContain("latest epoch n 12 of 30");
    }

    [Fact]
    public void An_inconclusive_or_unrecognised_word_is_open()
    {
        var inconclusive = Family(ExpressionFamilies.DebitSpread, Record("i", "INCONCLUSIVE", "O"));
        var odd = Family(ExpressionFamilies.DebitSpread, Record("o", "MAYBE", "O"));

        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.DebitSpread ? inconclusive : Dropped(f)), NoCut).Open.ShouldHaveSingleItem().ShouldContain("(INCONCLUSIVE)");
        OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.DebitSpread ? odd : Dropped(f)), NoCut).Open.ShouldHaveSingleItem().ShouldContain("unrecognised verdict word");
    }

    [Fact]
    public void An_unreadable_family_is_open_and_never_met()
    {
        var facts = AllDropped(f => f == ExpressionFamilies.OptionBuy ? new OptionsFamilyFacts(f, false, []) : Dropped(f));

        var report = OptionsPhaseCheck.Evaluate(facts, NoCut);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Families[0].Status.ShouldBe(OptionsPhaseCheck.StatusUnreadable);
    }

    [Fact]
    public void Cut_families_are_listed_as_cut_and_left_out_and_without_the_flag_nothing_is_cut()
    {
        var cut = OptionsPhaseCheck.Evaluate(AllDropped(), [IvFeatureCatalog.Family, ExpressionFamilies.IronCondor]);
        var none = OptionsPhaseCheck.Evaluate(AllDropped(), []);

        cut.Cut.ShouldBe([ExpressionFamilies.IronCondor, IvFeatureCatalog.Family]);
        cut.Families.Where(f => f.Cut).Select(f => f.Family).ShouldBe([ExpressionFamilies.IronCondor, IvFeatureCatalog.Family]);
        cut.ToText().ShouldContain("cut: expr:iron_condor, ext:iv-1");
        none.Cut.ShouldBeEmpty();
        none.ToText().ShouldContain("cut: none");
    }

    [Fact]
    public void Every_family_cut_and_every_expression_family_cut_are_not_evaluable()
    {
        var all = OptionsPhaseCheck.Evaluate(AllDropped(), OptionsPhaseCheck.Families.ToList());
        var expressions = OptionsPhaseCheck.Evaluate(AllDropped(), [ExpressionFamilies.OptionBuy, ExpressionFamilies.DebitSpread, ExpressionFamilies.CreditSpread, ExpressionFamilies.IronCondor]);

        all.Summary.ShouldContain("every family is cut");
        expressions.Summary.ShouldContain("every expression family is cut");
        (all.Verdict, expressions.Verdict).ShouldBe((PhaseVerdicts.NotEvaluable, PhaseVerdicts.NotEvaluable));
    }

    [Fact]
    public void The_limits_block_and_the_archive_figures_are_always_printed()
    {
        var text = OptionsPhaseCheck.Evaluate(AllDropped(), NoCut).ToText();

        text.ShouldContain("limits:");
        text.ShouldContain("Survivorship (ISS-22)");
        text.ShouldContain("years of underlying bars held: 2.1 of 5 needed");
        text.ShouldContain("NIFTY: usable chain sessions 12 of 60 needed; backfill keys done 40, given up 2");
        text.ShouldContain("chain recorder switch: ON; backfill switch: OFF");
        text.ShouldContain("costs net; hold-out not read; expiry derived 25.0 %; model-price fallback 4.0 %");
        text.ShouldContain("stale after: 90 days");
    }

    [Fact]
    public void A_figure_with_no_source_is_the_word_unavailable_never_a_digit()
    {
        var facts = Facts(OptionsPhaseCheck.Families.Select(f => Family(f))) with { Archive = new OptionsArchiveFacts(null, null, null, null) };

        var report = OptionsPhaseCheck.Evaluate(facts, []);
        var text = report.ToText();

        text.ShouldContain("years of underlying bars held: unavailable of 5 needed");
        text.ShouldContain("usable chain sessions and backfill progress: unavailable");
        text.ShouldContain("chain recorder switch: unavailable; backfill switch: unavailable");
        text.ShouldContain("record unavailable; N unavailable; costs unavailable; hold-out unavailable; expiry derived unavailable; model-price fallback unavailable");
        report.YearsHeld.ShouldBeNull();
        using var json = JsonDocument.Parse(report.ToJson());
        json.RootElement.GetProperty("years_held").GetString().ShouldBe("unavailable");
        json.RootElement.GetProperty("underlyings").GetString().ShouldBe("unavailable");
        json.RootElement.GetProperty("families")[0].GetProperty("trials").GetString().ShouldBe("unavailable");
        json.RootElement.GetProperty("families")[0].GetProperty("expiry_derived_share").GetString().ShouldBe("unavailable");
    }

    [Fact]
    public void A_non_finite_share_is_unavailable()
    {
        var odd = Record("n", "DROP", "O") with { DerivedExpiryShare = double.NaN, FallbackShare = double.PositiveInfinity };

        var text = OptionsPhaseCheck.Evaluate(AllDropped(f => f == ExpressionFamilies.OptionBuy ? Family(f, odd) : Dropped(f)), NoCut).ToText();

        text.ShouldContain("expr:option_buy: DROP (resolved); record n; N 3; costs net; hold-out not read; expiry derived unavailable; model-price fallback unavailable");
    }

    [Fact]
    public void Json_has_the_same_fields_as_the_text()
    {
        var report = OptionsPhaseCheck.Evaluate(AllDropped(), NoCut);

        using var json = JsonDocument.Parse(report.ToJson());
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        names.ShouldBe(["verdict", "summary", "open", "families", "violations", "violations_truncated", "years_held", "years_needed", "underlyings", "sessions_needed", "chain_recorder_switch", "backfill_switch", "archive_note", "cut", "stale_after_days", "limits"]);
        json.RootElement.GetProperty("verdict").GetString().ShouldBe("MET");
        json.RootElement.GetProperty("families").GetArrayLength().ShouldBe(OptionsPhaseCheck.Families.Count);
        json.RootElement.GetProperty("families")[0].EnumerateObject().Select(p => p.Name).ShouldBe(["family", "status", "resolved", "cut", "record", "trials", "costs", "hold_out", "expiry_derived_share", "model_price_fallback_share", "detail"]);
        json.RootElement.GetProperty("limits").GetArrayLength().ShouldBe(OptionsPhaseCheck.Limits.Count);
        json.RootElement.GetProperty("years_held").GetDouble().ShouldBe(2.1);
    }

    [Fact]
    public void The_same_facts_give_identical_text_and_json()
    {
        var a = OptionsPhaseCheck.Evaluate(AllDropped(), NoCut);
        var b = OptionsPhaseCheck.Evaluate(AllDropped(), NoCut);

        (a.ToText(), a.ToJson()).ShouldBe((b.ToText(), b.ToJson()));
    }

    [Fact]
    public void Violation_rows_are_listed_up_to_the_bound_and_say_so()
    {
        var rows = Enumerable.Range(1, OptionsPhaseCheck.MaxViolationRows + 5).Select(i => new OptionsCellRow("trading.plan_items", i)).ToList();

        var report = OptionsPhaseCheck.Evaluate(Facts(OptionsPhaseCheck.Families.Select(Dropped), rows), []);

        report.Violations.Count.ShouldBe(OptionsPhaseCheck.MaxViolationRows);
        report.CellsTruncated.ShouldBeTrue();
        report.ToText().ShouldContain("more rows exist");
    }

    [Theory]
    [InlineData(0, 365.25, 1.0)]
    [InlineData(0, 767.025, 2.1)]
    public void Years_are_the_span_of_the_bars_in_365_point_25_day_years(double fromDays, double toDays, double expected)
    {
        var from = Now.AddDays(fromDays);

        OptionsPhaseCheck.YearsBetween(from, Now.AddDays(toDays)).ShouldNotBeNull().ShouldBe(expected, 0.001);
        OptionsPhaseCheck.YearsBetween(null, Now).ShouldBeNull();
        OptionsPhaseCheck.YearsBetween(Now, Now.AddDays(-1)).ShouldBeNull();
    }
}
