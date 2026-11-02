using System.Text.Json;
using XDrishti.Application.Learning;
using static XDrishti.Application.Tests.Learning.EvaluationKit;

namespace XDrishti.Application.Tests.Learning;

/// <summary>Protocol F through the use case (P15-10): criteria 2 (the unavailable reasons), 4 (ledger first, re-run), 5 (hold-out once), 6 (the auto rule is called for each new verdict), 7 (determinism).</summary>
public sealed class FilterEvaluationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_trial_is_appended_to_the_ledger_before_the_first_verdict_is_stored_and_a_rerun_appends_nothing_and_yields_the_same_ids()
    {
        var rig = NewRig();
        var evaluation = Filter(rig, PlantedTail());

        var first = (await evaluation.RunAsync(false, Ct)).Value;
        var ids = rig.Store.Records.Select(r => r.EvaluationId).ToList();
        rig.Log.Clear();
        var second = (await evaluation.RunAsync(false, Ct)).Value;

        rig.Log.Take(3).ShouldBe(["ledger.append", "ledger.append", "ledger.append"]);
        rig.Ledger.Rows.Count.ShouldBe(3);
        rig.Ledger.Rows.Select(r => r.StrategyHash).Distinct().Count().ShouldBe(3);
        rig.Ledger.Rows.ShouldAllBe(r => r.StrategyId == "filter:results" && r.Method == "filter-compare");
        first.Records.Select(r => r.VersionKey).ShouldBe(["evf-1:N=1", "evf-1:N=2", "evf-1:N=3"]);
        first.Records.ShouldAllBe(r => r.Stored);
        second.Records.ShouldAllBe(r => !r.Stored);
        second.Records.Select(r => r.EvaluationId).ShouldBe(first.Records.Select(r => r.EvaluationId));
        rig.Store.Records.Select(r => r.EvaluationId).ShouldBe(ids);
        rig.Store.Records.ShouldAllBe(r => r.Trials == 3);
    }

    [Fact]
    public async Task A_ledger_that_cannot_be_written_stops_the_evaluation_before_anything_is_stored()
    {
        var rig = NewRig(failingLedger: true);

        await Should.ThrowAsync<InvalidOperationException>(async () => await Filter(rig, PlantedTail()).RunAsync(false, Ct));

        rig.Store.Records.ShouldBeEmpty();
        rig.Log.ShouldNotContain("store.append");
    }

    [Fact]
    public async Task The_planted_tail_is_a_keep_for_every_window_and_the_auto_rule_runs_once_after_all_the_new_verdicts_and_its_answer_is_returned()
    {
        var rig = NewRig();

        var outcome = (await Filter(rig, PlantedTail()).RunAsync(false, Ct)).Value;

        outcome.Records.Select(r => r.Verdict).ShouldBe(["KEEP", "KEEP", "KEEP"]);
        rig.Auto.Calls.ShouldBe(1);
        outcome.Auto.ShouldNotBeNull().Reason.ShouldBe("approval_manual");
        await Filter(rig, PlantedTail()).RunAsync(false, Ct);
        rig.Auto.Calls.ShouldBe(2);   // a run that stores nothing new still runs the idempotent rule once
    }

    [Fact]
    public async Task The_auto_rule_runs_once_even_when_the_hold_out_adds_records()
    {
        var rig = NewRig();

        await Filter(rig, PlantedTail()).RunAsync(true, Ct);

        rig.Store.Records.Count.ShouldBe(6);
        rig.Auto.Calls.ShouldBe(1);
        rig.Log.Last().ShouldBe("store.append");
    }

    [Fact]
    public async Task The_hold_out_record_names_the_development_record_it_confirms()
    {
        var rig = NewRig();

        await Filter(rig, PlantedTail()).RunAsync(true, Ct);

        foreach (var hold in rig.Store.Records.Where(r => r.HoldOutRead))
        {
            var development = rig.Store.Records.Single(r => !r.HoldOutRead && r.VersionKey == hold.VersionKey);
            hold.InputsJson.ShouldContain("\"development_record\":\"" + development.EvaluationId + "\"");
        }
    }

    [Fact]
    public async Task A_calendar_that_mostly_cannot_say_prints_each_reason_counts_them_as_removed_and_is_a_drop()
    {
        // Per season: 25 skipped, 100 taken, 125 unavailable (no_upcoming_entry): removed 150 of 250 = 60 %.
        var rig = NewRig();

        var outcome = (await Filter(rig, PlantedTail(noUpcomingPerSeason: 125)).RunAsync(false, Ct)).Value;

        outcome.Records.ShouldAllBe(r => r.Verdict == "DROP");
        var record = rig.Store.Records[1];
        using var estimates = JsonDocument.Parse(record.EstimatesJson);
        estimates.RootElement.GetProperty("share_lost_to_no_upcoming_entry").GetDouble().ShouldBe(0.5, 1e-9);
        estimates.RootElement.GetProperty("removed_share").GetDouble().ShouldBe(0.6, 1e-9);
        estimates.RootElement.GetProperty("unavailable_by_reason").GetProperty("no_upcoming_entry").GetProperty("trades").GetInt32().ShouldBe(125 * 8);
    }

    [Fact]
    public async Task Today_with_no_stored_unfiltered_runs_each_window_is_not_evaluable_with_the_missing_quantity_and_nothing_is_counted()
    {
        var rig = NewRig();

        var outcome = (await Filter(rig, null).RunAsync(false, Ct)).Value;

        outcome.Records.ShouldAllBe(r => r.Verdict == "NOT_EVALUABLE");
        outcome.Records[0].Missing[0].ShouldStartWith("seasons 0 of 5");
        rig.Ledger.Rows.ShouldBeEmpty();
        rig.Store.Records.ShouldAllBe(r => r.TrialHash == null && r.Trials == 1);
    }

    [Fact]
    public async Task The_order_of_the_observations_does_not_change_the_records()
    {
        var a = NewRig();
        var b = NewRig();
        var population = PlantedTail();
        var shuffled = population with { Observations = [.. population.Observations.Reverse()] };

        await Filter(a, population).RunAsync(false, Ct);
        await Filter(b, shuffled).RunAsync(false, Ct);

        b.Store.Records.Select(r => r.EvaluationId).ShouldBe(a.Store.Records.Select(r => r.EvaluationId));
    }

    [Fact]
    public async Task The_reserved_seasons_are_not_read_without_the_flag_and_with_it_once_per_window_then_refused()
    {
        var rig = NewRig();
        var evaluation = Filter(rig, PlantedTail());

        await evaluation.RunAsync(false, Ct);
        rig.Store.Records.ShouldAllBe(r => !r.HoldOutRead);

        var read = (await evaluation.RunAsync(true, Ct)).Value;

        read.Records.Count(r => r.HoldOutRead).ShouldBe(3);
        rig.Store.Records.Count(r => r.HoldOutRead).ShouldBe(3);
        read.Records.Where(r => r.HoldOutRead).ShouldAllBe(r => r.Verdict == "KEEP");

        var again = await evaluation.RunAsync(true, Ct);

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("extensions.holdout_used");
        rig.Store.Records.Count(r => r.HoldOutRead).ShouldBe(3);
    }

    [Fact]
    public async Task A_refused_second_hold_out_writes_nothing_and_does_not_run_the_auto_rule()
    {
        var rig = NewRig();
        var evaluation = Filter(rig, PlantedTail());
        await evaluation.RunAsync(true, Ct);
        var records = rig.Store.Records.Count;
        var autoCalls = rig.Auto.Calls;
        rig.Log.Clear();

        var again = await evaluation.RunAsync(true, Ct);

        again.Error.Code.ShouldBe("extensions.holdout_used");
        rig.Store.Records.Count.ShouldBe(records);
        rig.Auto.Calls.ShouldBe(autoCalls);
        rig.Log.ShouldNotContain("store.append");
    }

    [Fact]
    public async Task A_refused_hold_out_with_new_data_counts_no_trial()
    {
        var rig = NewRig();
        await Filter(rig, PlantedTail()).RunAsync(true, Ct);
        var rows = rig.Ledger.Rows.Count;
        rig.Log.Clear();

        var again = await Filter(rig, PlantedTail(13)).RunAsync(true, Ct);   // a new season: a new data window, so new ledger run ids

        again.Error.Code.ShouldBe("extensions.holdout_used");
        rig.Ledger.Rows.Count.ShouldBe(rows);
        rig.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_window_spending_the_hold_out_spends_it_for_every_window()
    {
        var rig = NewRig();
        await Filter(rig, PlantedTail()).RunAsync(true, Ct);
        rig.Store.Records.RemoveAll(r => r.HoldOutRead && r.VersionKey != "evf-1:N=2");   // only the window N=2 has a stored reading

        var again = await Filter(rig, PlantedTail()).RunAsync(true, Ct);

        again.Error.Code.ShouldBe("extensions.holdout_used");
    }

    [Fact]
    public async Task The_flag_reads_nothing_when_no_window_has_a_development_keep()
    {
        var rig = NewRig();

        var outcome = (await Filter(rig, null).RunAsync(true, Ct)).Value;

        outcome.Records.ShouldAllBe(r => !r.HoldOutRead);
        outcome.Note.ShouldNotBeNull().ShouldContain("no window has a development KEEP");
    }

    [Fact]
    public async Task The_verb_refuses_an_unknown_family()
    {
        var handler = new EvaluateExtensionHandler(Filter(NewRig(), null), null!, null!, null!);

        var result = await handler.HandleAsync(new EvaluateExtensionCommand("ext:md-1", false), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("extensions.invalid_query");
    }
}
