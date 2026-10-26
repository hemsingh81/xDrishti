using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>P8-00 D2: the default run set is the newest run of each strategy and universe, costed the way the newest run of all is; every run left out has a stated reason.</summary>
public sealed class RunSelectionTests
{
    private static ReportRunInfo Run(char key, string strategy, string universe, int day, string? sheet = null) =>
        new(ReportWorld.Key(key), strategy, "basket", "members as of today", universe, ReportWorld.Day0, ReportWorld.Day0.AddDays(100), sheet, "intraday", "stored strategy", null, null, 10, 0, ReportWorld.T0.AddDays(day));

    [Fact]
    public void No_stored_run_gives_an_empty_set()
    {
        var (chosen, notes) = RunSelection.Default([]);

        chosen.ShouldBeEmpty();
        notes.ShouldBeEmpty();
    }

    [Fact]
    public void The_newest_run_of_each_strategy_and_universe_is_taken_and_the_older_one_says_why_not()
    {
        var older = Run('a', "ORB", "basket:b1", 0);
        var newer = Run('b', "ORB", "basket:b1", 1);
        var other = Run('c', "ORB", "basket:b2", 0);
        var third = Run('d', "VWAP", "basket:b1", 0);

        var (chosen, notes) = RunSelection.Default([older, newer, other, third]);

        chosen.Select(r => r.RunKey).ShouldBe([newer.RunKey, other.RunKey, third.RunKey], ignoreOrder: true);
        notes.Keys.ShouldBe([older.RunKey]);
        notes[older.RunKey].ShouldContain("older run of the same strategy and universe");
    }

    [Fact]
    public void Gross_and_costed_runs_are_never_mixed_the_newest_run_of_all_decides()
    {
        var gross = Run('a', "ORB", "basket:b1", 0);
        var costed = Run('b', "VWAP", "basket:b1", 1, sheet: "sheet-1");

        var (chosen, notes) = RunSelection.Default([gross, costed]);

        chosen.Select(r => r.RunKey).ShouldBe([costed.RunKey]);
        notes[gross.RunKey].ShouldContain("never mixes gross and net");
    }

    [Fact]
    public void A_newer_gross_run_beats_an_older_costed_run_of_the_same_strategy_and_universe()
    {
        var costed = Run('a', "ORB", "basket:b1", 0, sheet: "sheet-1");
        var gross = Run('b', "ORB", "basket:b1", 1);

        var (chosen, notes) = RunSelection.Default([costed, gross]);

        chosen.Select(r => r.RunKey).ShouldBe([gross.RunKey]);
        notes[costed.RunKey].ShouldContain("costed run");
    }

    [Fact]
    public void The_choice_does_not_depend_on_the_order_of_the_list()
    {
        var runs = new[] { Run('a', "ORB", "basket:b1", 0), Run('b', "ORB", "basket:b1", 1), Run('c', "VWAP", "basket:b2", 0), Run('d', "VWAP", "basket:b2", 0) };

        var forward = RunSelection.Default(runs).Chosen.Select(r => r.RunKey).ToList();
        var backward = RunSelection.Default([.. runs.Reverse()]).Chosen.Select(r => r.RunKey).ToList();

        forward.ShouldBe(backward);
        forward.Count.ShouldBe(2);
    }

    [Fact]
    public void A_run_stored_by_a_replay_job_never_joins_the_default_set_even_when_it_is_the_newest_and_says_it_is_an_experiment_candidate()
    {
        var baseline = Run('a', "ORB", "basket:b1", 0);
        var candidate = Run('b', "ORB", "basket:b1", 5) with { IsExperimentCandidate = true };
        var costedCandidate = Run('c', "ORB", "basket:b1", 9, sheet: "sheet-1") with { IsExperimentCandidate = true };

        var (chosen, notes) = RunSelection.Default([baseline, candidate, costedCandidate]);

        chosen.Select(r => r.RunKey).ShouldBe([baseline.RunKey]);
        notes[candidate.RunKey].ShouldContain("experiment candidate");
        notes[costedCandidate.RunKey].ShouldContain("experiment candidate"); // the newest run of all is not a candidate, so it cannot decide gross against net either
        RunSelection.Default([candidate]).Chosen.ShouldBeEmpty();
    }
}
