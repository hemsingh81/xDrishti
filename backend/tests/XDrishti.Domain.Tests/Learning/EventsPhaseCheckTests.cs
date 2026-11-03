using XDrishti.Domain.Execution.PhaseCheck;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>Spec P15-12 criteria 1, 2, 4 and 5: the verdict table, the cut families, honest output (no digit for a non-value, the limits always, the JSON with the same fields) and determinism.</summary>
public sealed class EventsPhaseCheckTests
{
    private static readonly IReadOnlyDictionary<string, string> Rule = new Dictionary<string, string> { ["rule_version"] = "evf-1", ["lag_minutes"] = "0" };

    private static readonly IReadOnlyDictionary<string, string> Catalog = new Dictionary<string, string> { ["catalog"] = "ev-1", ["lag_minutes"] = "0" };

    private static EventsRecordFact Record(string id, string verdict, string protocol = "X", IReadOnlyDictionary<string, string>? versions = null, IReadOnlyList<string>? missing = null, int rows = 500, bool holdOut = false) =>
        new(id, protocol, verdict, holdOut, 3, rows, missing ?? [], versions ?? Catalog);

    private static EventsFamilyFacts Family(string family, params EventsRecordFact[] records) => new(family, true, records, CurrentOf(family));

    private static IReadOnlyDictionary<string, string> CurrentOf(string family) => family switch
    {
        ExtensionFamilies.Filter => Rule,
        ExtensionFamilies.Pead => new Dictionary<string, string> { ["engine"] = "sim-9" },
        _ => Catalog,
    };

    private static IReadOnlyDictionary<string, string> VersionsOf(string family) => CurrentOf(family);

    private static EventsFamilyFacts Resolved(string family, string verdict = "KEEP") =>
        Family(family, Record("rec-" + family, verdict, family switch { ExtensionFamilies.Filter => "F", ExtensionFamilies.Pead => "P", _ => "X" }, VersionsOf(family)));

    private static EventsPhaseFacts Facts(IEnumerable<EventsFamilyFacts> families, bool filterOn = false, int? seasons = 24) =>
        new([.. families], new EventsFilterFact(true, filterOn, 2, filterOn ? 41 : null, filterOn ? "owner" : null), seasons, [new EventsKindFact("results", 120, 8, 4)], 200, 150, 0.125, "rec-filter:results");

    private static EventsPhaseFacts AllResolved(bool filterOn = false) => Facts(ExtensionFamilies.All.Select(f => Resolved(f, f == ExtensionFamilies.Pead ? "DROP" : "KEEP")), filterOn);

    [Fact]
    public void All_five_families_resolved_and_the_filter_off_is_met()
    {
        var report = EventsPhaseCheck.Evaluate(AllResolved(), []);

        report.Verdict.ShouldBe(PhaseVerdicts.Met);
        report.Open.ShouldBeEmpty();
        report.Violations.ShouldBeEmpty();
        report.Families.ShouldAllBe(f => f.Resolved);
    }

    [Fact]
    public void One_not_evaluable_family_makes_the_check_not_evaluable_and_names_it_and_its_missing_quantity()
    {
        var families = ExtensionFamilies.All.Select(f => f == ExtensionFamilies.Pead
            ? Family(f, Record("p1", "NOT_EVALUABLE", "P", VersionsOf(f), ["seasons 8 of 20"], rows: 0))
            : Resolved(f));

        var report = EventsPhaseCheck.Evaluate(Facts(families), []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("pead");
        report.Open[0].ShouldContain("seasons 8 of 20");
        report.ToText().ShouldContain("seasons 8 of 20");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("auto")]
    public void The_filter_on_against_a_drop_is_not_met_and_the_violation_names_the_row_and_who_decided(string by)
    {
        var families = ExtensionFamilies.All.Select(f => f == ExtensionFamilies.Filter ? Resolved(f, "DROP") : Resolved(f));
        var facts = Facts(families, filterOn: true) with { Filter = new EventsFilterFact(true, true, 2, 77, by) };

        var report = EventsPhaseCheck.Evaluate(facts, []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotMet);
        var violation = report.Violations.ShouldHaveSingleItem();
        violation.DecisionId.ShouldBe(77);
        violation.DecidedBy.ShouldBe(by);
        report.ToText().ShouldContain("decision row 77");
        report.ToText().ShouldContain("decided by " + by);
    }

    [Fact]
    public void The_filter_on_with_a_current_keep_is_not_a_violation()
    {
        var report = EventsPhaseCheck.Evaluate(AllResolved(filterOn: true), []);

        report.Violations.ShouldBeEmpty();
        report.Verdict.ShouldBe(PhaseVerdicts.Met);
    }

    [Fact]
    public void The_filter_on_with_no_record_at_all_is_a_violation()
    {
        var facts = Facts(ExtensionFamilies.All.Select(f => f == ExtensionFamilies.Filter ? Family(f) : Resolved(f)), filterOn: true);

        EventsPhaseCheck.Evaluate(facts, []).Verdict.ShouldBe(PhaseVerdicts.NotMet);
    }

    [Fact]
    public void A_keep_of_an_older_catalog_version_is_stale_and_the_check_is_not_evaluable()
    {
        var old = new Dictionary<string, string> { ["catalog"] = "ev-0", ["lag_minutes"] = "0" };
        var families = ExtensionFamilies.All.Select(f => f == ExtensionFamilies.EventFeatures ? Family(f, Record("old", "KEEP", versions: old)) : Resolved(f));

        var report = EventsPhaseCheck.Evaluate(Facts(families), []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        var line = report.Families.Single(f => f.Family == ExtensionFamilies.EventFeatures);
        line.Status.ShouldBe(EventsPhaseCheck.StatusStale);
        line.Resolved.ShouldBeFalse();
        line.Detail.ShouldContain("catalog ev-0, current ev-1");
    }

    [Fact]
    public void A_newer_record_for_the_current_version_wins_over_a_newer_stale_one()
    {
        var old = new Dictionary<string, string> { ["catalog"] = "ev-0", ["lag_minutes"] = "0" };
        var family = Family(ExtensionFamilies.EventFeatures, Record("new-old", "DROP", versions: old), Record("cur", "KEEP"));

        var line = EventsPhaseCheck.Evaluate(Facts([family]), []).Families.Single(f => f.Family == ExtensionFamilies.EventFeatures);

        (line.Status, line.RecordId, line.Resolved).ShouldBe(("KEEP", "cur", true));
    }

    [Fact]
    public void A_version_that_is_not_recorded_is_stale_never_current()
    {
        var family = Family(ExtensionFamilies.EventFeatures, Record("x", "KEEP", versions: new Dictionary<string, string>()));

        EventsPhaseCheck.Evaluate(Facts([family]), []).Families.Single(f => f.Family == ExtensionFamilies.EventFeatures).Status.ShouldBe(EventsPhaseCheck.StatusStale);
    }

    [Fact]
    public void No_records_at_all_is_not_evaluable_and_says_no_evaluation_is_stored()
    {
        var facts = Facts(ExtensionFamilies.All.Select(f => Family(f)));

        var report = EventsPhaseCheck.Evaluate(facts, []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Summary.ShouldBe("no P15 evaluation stored.");
    }

    [Fact]
    public void An_unreadable_store_or_a_missing_family_is_not_evaluable_not_an_error()
    {
        var unreadable = new EventsFamilyFacts(ExtensionFamilies.Pead, false, [], CurrentOf(ExtensionFamilies.Pead));
        var families = ExtensionFamilies.All.Where(f => f != ExtensionFamilies.Flag && f != ExtensionFamilies.Pead).Select(f => Resolved(f)).Append(unreadable);

        var report = EventsPhaseCheck.Evaluate(Facts(families), []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Families.Single(f => f.Family == ExtensionFamilies.Pead).Status.ShouldBe(EventsPhaseCheck.StatusUnreadable);
        report.Families.Single(f => f.Family == ExtensionFamilies.Flag).Status.ShouldBe(EventsPhaseCheck.StatusUnreadable);
    }

    [Fact]
    public void A_keep_with_a_named_missing_quantity_or_no_rows_is_thin_and_never_resolved()
    {
        var missing = Family(ExtensionFamilies.EventFeatures, Record("a", "KEEP", missing: ["hold-out 0 of 30"]));
        var empty = Family(ExtensionFamilies.FundamentalFeatures, Record("b", "DROP", rows: 0));

        var report = EventsPhaseCheck.Evaluate(Facts([missing, empty]), []);

        report.Families.Single(f => f.Family == ExtensionFamilies.EventFeatures).Resolved.ShouldBeFalse();
        report.Families.Single(f => f.Family == ExtensionFamilies.EventFeatures).Detail.ShouldContain("thin evidence");
        report.Families.Single(f => f.Family == ExtensionFamilies.FundamentalFeatures).Resolved.ShouldBeFalse();
        report.Verdict.ShouldNotBe(PhaseVerdicts.Met);
    }

    [Theory]
    [InlineData("INCONCLUSIVE")]
    [InlineData("NOT_EVALUABLE")]
    [InlineData("MAYBE")]
    public void An_open_verdict_word_is_never_resolved(string word)
    {
        var families = ExtensionFamilies.All.Select(f => f == ExtensionFamilies.FundamentalFeatures ? Family(f, Record("o", word)) : Resolved(f));

        var report = EventsPhaseCheck.Evaluate(Facts(families), []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Families.Single(f => f.Family == ExtensionFamilies.FundamentalFeatures).Resolved.ShouldBeFalse();
    }

    [Fact]
    public void The_flag_family_is_resolved_by_a_drop_of_the_grade_or_by_the_comparison_not_by_a_passed_grade_alone()
    {
        var l = "llm-1";
        var versions = new Dictionary<string, string> { ["catalog"] = l };
        EventsFamilyFacts Flag(params EventsRecordFact[] records) => new(ExtensionFamilies.Flag, true, records, versions);
        EventsRecordFact R(string id, string protocol, string verdict, string[]? missing = null) => new(id, protocol, verdict, false, 1, 200, missing ?? [], versions);

        EventsFamilyLine LineOf(EventsFamilyFacts f) => EventsPhaseCheck.Evaluate(Facts([f]), []).Families.Single(x => x.Family == ExtensionFamilies.Flag);

        LineOf(Flag(R("x", "X", "NOT_EVALUABLE", ["no_extraction: rows not built"]), R("l", "L", "KEEP"))).Resolved.ShouldBeFalse();
        LineOf(Flag(R("l", "L", "KEEP"))).Resolved.ShouldBeFalse();
        LineOf(Flag(R("l", "L", "KEEP"))).Detail.ShouldContain("comparison (protocol X) is not stored");
        LineOf(Flag(R("x", "X", "NOT_EVALUABLE", ["protocol_L_not_passed"]), R("l", "L", "DROP"))).Resolved.ShouldBeTrue();
        LineOf(Flag(R("x", "X", "KEEP"), R("l", "L", "KEEP"))).Resolved.ShouldBeTrue();
    }

    [Fact]
    public void The_filter_on_with_an_unreadable_verdict_store_is_not_evaluable_naming_verdict_unreadable_not_not_met()
    {
        var unreadable = new EventsFamilyFacts(ExtensionFamilies.Filter, false, [], Rule);
        var facts = Facts(ExtensionFamilies.All.Where(f => f != ExtensionFamilies.Filter).Select(f => Resolved(f)).Append(unreadable), filterOn: true);

        var report = EventsPhaseCheck.Evaluate(facts, []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Violations.ShouldBeEmpty();
        report.Open.ShouldContain(o => o.Contains("verdict unreadable"));
    }

    [Fact]
    public void The_hold_out_column_counts_current_records_only()
    {
        var old = new Dictionary<string, string> { ["catalog"] = "ev-0", ["lag_minutes"] = "0" };
        var family = Family(ExtensionFamilies.EventFeatures, Record("cur", "KEEP"), Record("old", "KEEP", versions: old, holdOut: true));

        EventsPhaseCheck.Evaluate(Facts([family]), []).Families.Single(f => f.Family == ExtensionFamilies.EventFeatures).HoldOut.ShouldBe("not read");
    }

    [Fact]
    public void A_cut_family_is_listed_as_cut_and_left_out_of_the_overall()
    {
        var families = ExtensionFamilies.All.Select(f => f is ExtensionFamilies.Flag or ExtensionFamilies.Pead ? Family(f) : Resolved(f));
        var facts = Facts(families);

        var without = EventsPhaseCheck.Evaluate(facts, []);
        var with = EventsPhaseCheck.Evaluate(facts, [ExtensionFamilies.Flag, ExtensionFamilies.Pead]);

        without.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        without.Cut.ShouldBeEmpty();
        without.ToText().ShouldContain("cut: none");
        with.Verdict.ShouldBe(PhaseVerdicts.Met);
        with.Cut.ShouldBe([ExtensionFamilies.Flag, ExtensionFamilies.Pead]);
        with.Families.Where(f => f.Cut).Select(f => f.Family).ShouldBe([ExtensionFamilies.Flag, ExtensionFamilies.Pead]);
        with.ToText().ShouldContain("cut: ext:llm-1, pead");
    }

    [Fact]
    public void Cutting_every_family_judges_nothing_and_is_not_evaluable()
    {
        EventsPhaseCheck.Evaluate(AllResolved(), ExtensionFamilies.All.ToArray()).Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
    }

    [Fact]
    public void A_cut_filter_that_is_on_without_a_keep_is_still_a_violation()
    {
        var families = ExtensionFamilies.All.Select(f => f == ExtensionFamilies.Filter ? Family(f) : Resolved(f));

        EventsPhaseCheck.Evaluate(Facts(families, filterOn: true), [ExtensionFamilies.Filter]).Verdict.ShouldBe(PhaseVerdicts.NotMet);
    }

    [Fact]
    public void An_unreadable_decision_history_is_never_met_because_a_violation_cannot_be_ruled_out()
    {
        var facts = AllResolved() with { Filter = new EventsFilterFact(false, false, 2, null, null) };

        var report = EventsPhaseCheck.Evaluate(facts, []);

        report.Verdict.ShouldBe(PhaseVerdicts.NotEvaluable);
        report.Open.ShouldHaveSingleItem().ShouldContain("decision history could not be read");
    }

    [Fact]
    public void A_value_that_could_not_be_read_prints_as_unavailable_and_no_digit_stands_for_it()
    {
        var facts = Facts(ExtensionFamilies.All.Select(f => Family(f))) with { SeasonsHeld = null, Filings = null, TrackedInstruments = null, InstrumentsWithEntry = null, NoUpcomingShare = null, NoUpcomingRecord = null };

        var text = EventsPhaseCheck.Evaluate(facts, []).ToText();

        text.ShouldContain("seasons of bars held: unavailable of 20 needed");
        text.ShouldContain("filings coverage: unavailable");
        text.ShouldContain("calendar coverage: unavailable of unavailable");
        text.ShouldContain("(newest F record unavailable): unavailable");
    }

    [Fact]
    public void A_non_finite_share_is_unavailable()
    {
        var report = EventsPhaseCheck.Evaluate(AllResolved() with { NoUpcomingShare = double.NaN }, []);

        report.NoUpcomingShare.ShouldBeNull();
        report.ToJson().ShouldContain("\"share_lost_to_no_upcoming_entry\": \"unavailable\"");
    }

    [Fact]
    public void The_limits_the_seasons_and_the_share_are_always_printed()
    {
        var text = EventsPhaseCheck.Evaluate(AllResolved(), []).ToText();

        text.ShouldContain("limits:");
        text.ShouldContain("ISS-25");
        text.ShouldContain("ISS-22");
        text.ShouldContain("seasons of bars held: 24 of 20 needed");
        text.ShouldContain("share of entries lost to no_upcoming_entry (newest F record rec-filter:results): 12.5 %");
        text.ShouldContain("filings results: 120 stored in 8 quarters; 4 tracked instruments without");
        text.ShouldContain("calendar coverage: 150 of 200");
        text.ShouldContain("hold-out not read");
    }

    [Fact]
    public void The_hold_out_column_says_read_when_a_record_read_it()
    {
        var family = Family(ExtensionFamilies.EventFeatures, Record("h", "KEEP", holdOut: true), Record("d", "KEEP"));

        EventsPhaseCheck.Evaluate(Facts([family]), []).Families.Single(f => f.Family == ExtensionFamilies.EventFeatures).HoldOut.ShouldBe("read");
    }

    [Fact]
    public void The_json_carries_the_same_fields_as_the_text()
    {
        var json = EventsPhaseCheck.Evaluate(AllResolved(), []).ToJson();

        json.ShouldBe(
            """
            {
              "verdict": "MET",
              "summary": "all 5 counted families are resolved (KEEP or DROP) for the current versions and no family is in use without a KEEP.",
              "open": [],
              "families": [
                {
                  "family": "filter:results",
                  "status": "KEEP",
                  "resolved": true,
                  "cut": false,
                  "record": "rec-filter:results",
                  "trials": 3,
                  "hold_out": "not read",
                  "detail": "rows 500"
                },
                {
                  "family": "ext:ev-1",
                  "status": "KEEP",
                  "resolved": true,
                  "cut": false,
                  "record": "rec-ext:ev-1",
                  "trials": 3,
                  "hold_out": "not read",
                  "detail": "rows 500"
                },
                {
                  "family": "ext:fu-1",
                  "status": "KEEP",
                  "resolved": true,
                  "cut": false,
                  "record": "rec-ext:fu-1",
                  "trials": 3,
                  "hold_out": "not read",
                  "detail": "rows 500"
                },
                {
                  "family": "ext:llm-1",
                  "status": "KEEP",
                  "resolved": true,
                  "cut": false,
                  "record": "rec-ext:llm-1",
                  "trials": 3,
                  "hold_out": "not read",
                  "detail": "rows 500"
                },
                {
                  "family": "pead",
                  "status": "DROP",
                  "resolved": true,
                  "cut": false,
                  "record": "rec-pead",
                  "trials": 3,
                  "hold_out": "not read",
                  "detail": "rows 500"
                }
              ],
              "violations": [],
              "seasons_held": 24,
              "seasons_needed": 20,
              "filings": [
                {
                  "kind": "results",
                  "stored": 120,
                  "quarters": 8,
                  "instruments_without": 4
                }
              ],
              "tracked_instruments": 200,
              "instruments_with_entry": 150,
              "share_lost_to_no_upcoming_entry": 0.125,
              "share_record": "rec-filter:results",
              "feature_sets_in_scoring": "not built",
              "event_filter_on": false,
              "cut": [],
              "limits": [
                "Only stored verdicts are read; no evaluation is run and nothing is written, no setting changed and no order placed.",
                "R is gross unless the owner's checked charge sheet exists (ISS-25).",
                "Survivorship (ISS-22): the instrument lists hold today's names, so every verdict is read with that bias.",
                "A verdict for an older rule, catalog, parser, engine or lag version is stale and counts for nothing.",
                "A KEEP of the event filter is a record; the hold-out column says whether the reserved seasons were read.",
                "A violation (the filter ON without a current KEEP) is reported; the owner may keep it, nothing here switches it."
              ]
            }

            """.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_facts_give_the_same_text_and_json()
    {
        var a = EventsPhaseCheck.Evaluate(AllResolved(), [ExtensionFamilies.Pead]);
        var b = EventsPhaseCheck.Evaluate(AllResolved(), [ExtensionFamilies.Pead]);

        (a.ToText(), a.ToJson()).ShouldBe((b.ToText(), b.ToJson()));
    }
}
