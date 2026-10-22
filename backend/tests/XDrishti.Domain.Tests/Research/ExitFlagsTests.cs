using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>Criterion 2 of P4-05: the flags <c>--exit</c> and <c>--time-stop</c>, every refusal by name and the expansion of lists into candidates.</summary>
public sealed class ExitFlagsTests
{
    private static (ExitChoice? Choice, List<string> Errors) Parse(string? timeStop, params string[] exits)
    {
        var errors = new List<string>();
        return (ExitFlags.Parse(exits, timeStop, errors), errors);
    }

    [Fact]
    public void A_list_of_candidate_values_expands_to_one_candidate_each_with_a_stable_id()
    {
        var (choice, errors) = Parse(null, "FIXED_RR:rr=1.5|2|2.5");

        errors.ShouldBeEmpty();
        choice!.Candidates.Select(c => c.Id).ShouldBe(["FIXED_RR{rr=1.5}", "FIXED_RR{rr=2}", "FIXED_RR{rr=2.5}"]);
    }

    [Fact]
    public void Two_lists_expand_to_their_product_in_name_order()
    {
        var (choice, errors) = Parse(null, "PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2|3", "ATR_TRAIL:mult=2|3,activate_at_rr=0.5");

        errors.ShouldBeEmpty();
        choice!.Candidates.Select(c => c.Id).ShouldBe(
            ["PARTIAL_BE{book_pct=50,t1_rr=1,t2_rr=2}", "PARTIAL_BE{book_pct=50,t1_rr=1,t2_rr=3}", "ATR_TRAIL{activate_at_rr=0.5,mult=2}", "ATR_TRAIL{activate_at_rr=0.5,mult=3}"]);
    }

    [Fact]
    public void A_pyramid_ladder_is_one_candidate_and_is_not_expanded()
    {
        var (choice, errors) = Parse(null, "PYRAMID:initial_pct=50,add_at_rr=1|2,add_pct=25|25,max_adds=2,trail_atr_mult=2");

        errors.ShouldBeEmpty();
        choice!.Candidates.Count.ShouldBe(1);
        choice.Candidates[0].Id.ShouldBe("PYRAMID{add_at_rr=1|2,add_pct=25|25,initial_pct=50,max_adds=2,trail_atr_mult=2}");
    }

    [Fact]
    public void The_time_stop_is_an_overlay_for_every_candidate_and_shows_in_the_id()
    {
        var (choice, errors) = Parse("min_progress_rr=0.5,intraday_bars=6", "FIXED_RR:rr=2", "FIXED_RR:rr=3");

        errors.ShouldBeEmpty();
        choice!.TimeStop.ShouldBe(new TimeStopChoice(0.5, 6, 0));
        choice.Candidates.Select(choice.IdOf).ShouldBe(["FIXED_RR{rr=2}+time_stop", "FIXED_RR{rr=3}+time_stop"]);
    }

    [Theory]
    [InlineData("FIXED_RRR:rr=2", "'--exit FIXED_RRR:rr=2'", "not an exit policy")]
    [InlineData("FIXED_RR:risk=2", "'--exit FIXED_RR:risk=2'", "not a parameter of FIXED_RR")]
    [InlineData("FIXED_RR:rr=abc", "'--exit FIXED_RR:rr=abc'", "'abc' is not")]
    [InlineData("FIXED_RR:rr=2,rr=3", "'--exit FIXED_RR:rr=2,rr=3'", "more than once")]
    [InlineData("PARTIAL_BE:book_pct=50|60,t1_rr=1,t2_rr=2", "'--exit PARTIAL_BE", "'book_pct' takes one value")]
    [InlineData("EMA_TRAIL:length=5|9", "'--exit EMA_TRAIL:length=5|9'", "takes one value")]
    [InlineData("EVENT_EXIT:days_before_results=1", "'--exit EVENT_EXIT", "not run yet")]
    [InlineData("TIME_STOP:min_progress_rr=1", "'--exit TIME_STOP", "use --time-stop")]
    [InlineData("FIXED_RR:rr=NaN", "'--exit FIXED_RR:rr=NaN'", "'NaN' is not")]
    public void A_refused_exit_names_the_flag_and_the_value(string spec, string flag, string reason)
    {
        var (choice, errors) = Parse(null, spec);

        choice.ShouldBeNull();
        errors.ShouldHaveSingleItem().ShouldContain(flag);
        errors[0].ShouldContain(reason);
    }

    [Fact]
    public void Two_candidates_with_identical_values_are_refused()
    {
        var (choice, errors) = Parse(null, "FIXED_RR:rr=2", "FIXED_RR:rr=1|2");

        choice.ShouldBeNull();
        errors.ShouldHaveSingleItem().ShouldContain("FIXED_RR{rr=2} twice");
    }

    [Theory]
    [InlineData("min_progress_rr=0.5", "give 'intraday_bars'")]
    [InlineData("intraday_bars=6", "'min_progress_rr' is required")]
    [InlineData("min_progress_rr=0.5,intraday_bars=6,swing_sessions=2", "give 'intraday_bars'")]
    [InlineData("min_progress_rr=0.5,intraday_bars=2.5", "whole number")]
    [InlineData("event_exit=1", "event_exit is not run yet")]
    public void A_refused_time_stop_names_the_flag_and_the_value(string text, string reason)
    {
        var (choice, errors) = Parse(text, "FIXED_RR:rr=2");

        choice.ShouldBeNull();
        errors.ShouldNotBeEmpty();
        errors.ShouldAllBe(e => e.Contains("'--time-stop " + text + "'", StringComparison.Ordinal));
        string.Join(" ", errors).ShouldContain(reason);
    }

    [Fact]
    public void A_time_stop_alone_and_an_expansion_past_the_most_are_refused()
    {
        Parse("min_progress_rr=0.5,intraday_bars=6").Errors.ShouldHaveSingleItem().ShouldContain("needs at least one '--exit'");
        var (choice, errors) = Parse(null, "ATR_TRAIL:mult=1|2|3|4|5,activate_at_rr=0.5", "FIXED_RR:rr=1|2|3|4|5|6", "PARTIAL_BE:book_pct=50,t1_rr=1,t2_rr=2|3|4|5|6|7");

        choice.ShouldBeNull();
        errors.ShouldHaveSingleItem().ShouldContain("expands to 17 candidates; the most is 16");
    }

    [Fact]
    public void The_catalog_lists_every_policy_with_its_parameters()
    {
        ExitFlags.Catalog.Count.ShouldBe(7);
        ExitFlags.Catalog.ShouldContain(l => l.StartsWith("FIXED_RR: rr (candidate lists", StringComparison.Ordinal));
        ExitFlags.Catalog.ShouldContain(l => l.Contains("ladders with '|', not expanded: add_at_rr, add_pct", StringComparison.Ordinal));
    }
}
