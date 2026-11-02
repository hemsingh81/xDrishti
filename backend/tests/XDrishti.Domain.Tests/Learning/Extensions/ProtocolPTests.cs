using System.Text.Json;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Protocol P of P15-10: criterion 1 (placebo and planted effect, one season fewer), criterion 3 (thresholds with the missing quantity named), criterion 5 (the hold-out is not read by the development verdict), criterion 7.</summary>
public sealed class ProtocolPTests
{
    private static readonly ProtocolPOptions Fast = new() { Draws = 500 };

    [Fact]
    public void A_planted_effect_over_twenty_seasons_is_a_keep()
    {
        var result = ProtocolP.Evaluate(PeadKit.Events(20, 0.5, 1), 4, Fast).Value;

        result.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep);
        result.ChosenVariant.ShouldBe(PeadKit.Variant);
        result.Outcome.FoldsTotal.ShouldBe(8);
        result.TestGainR.ShouldNotBeNull();
        result.TestGainR.Value.ShouldBeInRange(0.4, 0.6);
    }

    [Fact]
    public void One_season_fewer_is_not_evaluable_and_names_the_seasons()
    {
        var outcome = ProtocolP.Evaluate(PeadKit.Events(19, 0.5, 1), 4, Fast).Value.Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(["seasons 19 of 20"]);
        using var estimates = JsonDocument.Parse(outcome.EstimatesJson);
        estimates.RootElement.GetProperty("status").GetString().ShouldBe("unavailable");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 16)]
    [InlineData(3, 4)]
    public void Random_labels_are_never_a_keep(int seed, int variants)
    {
        ProtocolP.Evaluate(PeadKit.Events(20, 0.0, seed), variants, Fast).Value.Outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void A_weak_planted_effect_is_not_a_keep_when_the_ledger_count_widens_the_margin()
    {
        // Effect 0.17 clears 0.05 x Factor(1) but not 0.05 x Factor(16) = 0.1.
        ProtocolP.Evaluate(PeadKit.Events(20, 0.17, 4), 1, new ProtocolPOptions { Draws = 500, MinTopEvents = 250 }).Value.Outcome.Verdict.ShouldBe(ExtensionVerdict.Keep);
        ProtocolP.Evaluate(PeadKit.Events(20, 0.17, 4), 16, Fast).Value.Outcome.Verdict.ShouldNotBe(ExtensionVerdict.Keep);
    }

    [Fact]
    public void Fewer_than_eight_test_seasons_with_events_names_the_count()
    {
        var events = PeadKit.Events(20, 0.5, 1, skipVariantSeason: 10);

        var outcome = ProtocolP.Evaluate(events, 4, Fast).Value.Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldContain("test seasons 7 of 8");
    }

    [Fact]
    public void Fewer_than_two_hundred_fifty_top_events_names_the_count()
    {
        var events = PeadKit.Events(20, 0.5, 1, datesPerSeason: 25);   // 8 test seasons x 25 = 200

        var outcome = ProtocolP.Evaluate(events, 4, Fast).Value.Outcome;

        outcome.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
        outcome.Missing.ShouldBe(["top-bucket events 200 of 250"]);
    }

    [Fact]
    public void The_development_record_does_not_depend_on_the_returns_of_the_hold_out_seasons()
    {
        var events = PeadKit.Events(20, 0.5, 1);
        var changed = events.Select(e => e.EntrySession >= 1600 ? e with { R = e.R + 9 } : e).ToList();

        var a = ProtocolP.Evaluate(events, 4, Fast).Value.Outcome;
        var b = ProtocolP.Evaluate(changed, 4, Fast).Value.Outcome;

        b.EstimatesJson.ShouldBe(a.EstimatesJson);
        b.EvidenceJson.ShouldBe(a.EvidenceJson);
    }

    [Fact]
    public void The_order_of_the_events_does_not_change_the_record()
    {
        var events = PeadKit.Events(20, 0.5, 1);

        var a = ProtocolP.Evaluate(events, 4, Fast).Value.Outcome;
        var b = ProtocolP.Evaluate([.. events.AsEnumerable().Reverse()], 4, Fast).Value.Outcome;

        b.EstimatesJson.ShouldBe(a.EstimatesJson);
        b.EvidenceJson.ShouldBe(a.EvidenceJson);
    }

    [Fact]
    public void The_variant_with_the_best_gain_on_the_first_eight_seasons_is_chosen_and_frozen()
    {
        var events = PeadKit.Events(20, 0.2, 1).Concat(PeadKit.Events(20, 0.6, 2, variant: "results_car3/20")).ToList();

        var result = ProtocolP.Evaluate(events, 4, Fast).Value;

        result.ChosenVariant.ShouldBe("results_car3/20");
    }

    [Fact]
    public void The_hold_out_confirms_the_sign_and_does_not_confirm_the_opposite_sign()
    {
        var good = PeadKit.Events(20, 0.5, 1);
        var bad = good.Select(e => e.EntrySession >= 1600 && e.Bucket == PeadBucket.Top ? e with { R = e.R - 3 } : e).ToList();

        ProtocolP.HoldOut(good, PeadKit.Variant, 0.5, Fast).Value.Verdict.ShouldBe(ExtensionVerdict.Keep);
        ProtocolP.HoldOut(bad, PeadKit.Variant, 0.5, Fast).Value.Verdict.ShouldBe(ExtensionVerdict.Inconclusive);
        ProtocolP.HoldOut(PeadKit.Events(20, 0.5, 1, datesPerSeason: 5), PeadKit.Variant, 0.5, Fast).Value.Verdict.ShouldBe(ExtensionVerdict.NotEvaluable);
    }

    [Fact]
    public void An_event_that_runs_into_the_next_phase_is_purged_with_the_embargo()
    {
        var events = PeadKit.Events(20, 0.5, 1).ToList();
        var last = events.Where(e => e.Bucket == PeadBucket.Top && e.EntrySession is >= 1100 and < 1200).Max(e => e.EntrySession);
        var runs = events.Select(e => e.EntrySession == last ? e with { ExitSession = 1600 } : e).ToList();

        var a = ProtocolP.Evaluate(events, 4, Fast).Value.Outcome;
        var b = ProtocolP.Evaluate(runs, 4, Fast).Value.Outcome;

        b.Rows.ShouldBe(a.Rows - 1);
    }

    [Fact]
    public void Events_outside_the_four_variants_are_refused()
    {
        ProtocolP.Evaluate([PeadKit.Event("x", 0, 0, 0, PeadBucket.Top, "other/3", 1)], 4, Fast).IsFailure.ShouldBeTrue();
    }
}
