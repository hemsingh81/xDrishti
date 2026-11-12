using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The pure rules of the scheduled cell-statistics refresh (spec LC-01, ADR 0277).</summary>
public sealed class CellsRefreshRulesTests
{
    [Fact]
    public void The_refresh_end_is_the_block_start_less_one_day_and_the_embargo_when_that_is_earlier()
    {
        var end = CellsRefreshRules.RefreshEnd(new DateOnly(2026, 11, 12), new DateOnly(2026, 5, 5), 30);

        end.ShouldBe(new DateOnly(2026, 4, 4)); // 2026-05-05 - 1 - 30
    }

    [Fact]
    public void The_refresh_end_is_the_last_closed_session_when_that_is_earlier_than_the_cut()
    {
        var end = CellsRefreshRules.RefreshEnd(new DateOnly(2026, 3, 2), new DateOnly(2026, 5, 5), 30);

        end.ShouldBe(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void With_no_block_there_is_no_end_and_the_refresh_fails_closed()
    {
        CellsRefreshRules.RefreshEnd(new DateOnly(2026, 11, 12), null, 30).ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-04-04", "2026-04-04", true, RefreshSourceAction.UpToDate)]
    [InlineData("2026-04-04", "2026-04-04", false, RefreshSourceAction.UpToDate)]
    [InlineData("2026-03-31", "2026-04-04", false, RefreshSourceAction.NotRegistered)]
    [InlineData("2026-03-31", "2026-04-04", true, RefreshSourceAction.Extend)]
    public void A_source_is_extended_only_when_it_ends_before_the_end_and_its_version_is_registered(string runTo, string end, bool registered, RefreshSourceAction expected)
    {
        CellsRefreshRules.Decide(DateOnly.Parse(runTo, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), registered).ShouldBe(expected);
    }

    [Theory]
    [InlineData(2, 3, false)]
    [InlineData(3, 3, true)]
    [InlineData(5, 3, true)]
    [InlineData(1, 1, true)]
    public void The_bell_speaks_at_the_set_number_of_sessions_or_more(int age, int threshold, bool expected)
    {
        CellsRefreshRules.ShouldNotify(age, threshold).ShouldBe(expected);
    }

    [Fact]
    public void The_notice_text_names_the_age_the_gate_limit_and_the_last_refresh()
    {
        CellsRefreshRules.StaleText(3, 5, "paused").ShouldBe("Cell statistics are 3 sessions old; the nightly plan fails at 6. Last refresh: paused.");
    }

    [Fact]
    public void The_drift_note_names_the_end_and_the_hold_out_release()
    {
        CellsRefreshRules.DriftNote(new DateOnly(2026, 4, 4)).ShouldBe("data through 2026-04-04; no new trades can enter until a hold-out block is released (HO-01)");
        CellsRefreshRules.DriftNote(null).ShouldContain("no new trades can enter until a hold-out block is released (HO-01)");
    }

    [Fact]
    public void A_refresh_row_is_one_look_in_the_total_and_not_a_variant()
    {
        var counts = new TrialCounts(2, 1, 0, 0, 0, CellsRefreshes: 3);

        counts.Total.ShouldBe(6);
        counts.DistinctHashes.ShouldBe(3);
    }
}
