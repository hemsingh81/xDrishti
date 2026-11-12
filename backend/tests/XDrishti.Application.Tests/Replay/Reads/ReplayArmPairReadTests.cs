#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Tests.Replay.Reads;

/// <summary>SR-02, AC 3: the pair read of a selection run and a baseline run answers selectionVsBaseline, with the baseline label and the realised-only shortcut, and no answer while a run is not complete.</summary>
public sealed class ReplayArmPairReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Pair = Guid.NewGuid();
    private static readonly DateOnly Start = new(2026, 10, 5);

    private static ReplayRunRow Run(string arm, string state)
    {
        var p = new ReplayParams(Start, Start.AddDays(40), new ReplayScope([Guid.NewGuid()], []), 1_000_000m, ReplayRiskProfile.Standard, null, ReplayVersionRule.Start, ReplayMode.Frozen, Arm: arm == "baseline" ? ReplayArm.Baseline : ReplayArm.Selection);
        return new ReplayRunRow(Guid.NewGuid(), Pair, p.From, p.To, "frozen", ReplayJson.Canonical(p), "{}", "{}", null, new string('a', 64), new string('b', 64), state == "completed" ? new string('c', 64) : null, state, null, 0, 40, 1, DateTimeOffset.UnixEpoch, null, null, Arm: arm);
    }

    private static List<ReplayEquityRow> Curve(Func<int, decimal> dailyReturn)
    {
        var rows = new List<ReplayEquityRow>();
        var equity = 1_000_000m;
        for (var i = 0; i < 30; i++)
        {
            equity *= 1 + dailyReturn(i);
            rows.Add(new ReplayEquityRow(Start.AddDays(i), equity, 0m, 0));
        }

        return rows;
    }

    private static IReplayReads Reads(ReplayRunRow selection, ReplayRunRow baseline, List<ReplayEquityRow> a, List<ReplayEquityRow> b)
    {
        var reads = Substitute.For<IReplayReads>();
        reads.GetPairAsync(Pair, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ReplayRunRow>>([selection, baseline]));
        reads.LabelsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<string>>([]));
        reads.EquityAsync(selection.Id, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ReplayEquityRow>>(a));
        reads.EquityAsync(baseline.Id, null, null, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<ReplayEquityRow>>(b));
        return reads;
    }

    [Fact]
    public async Task Two_complete_arms_are_compared_and_the_view_carries_the_label_and_the_shortcut()
    {
        var (s, b) = (Run("selection", "completed"), Run("baseline", "completed"));
        var reads = Reads(s, b, Curve(i => (((i % 5) - 2) * 0.001m) + 0.002m + ((i % 3) * 0.0001m)), Curve(i => ((i % 5) - 2) * 0.001m));

        var result = await new GetReplayPairHandler(reads).HandleAsync(new GetReplayPairQuery(Pair), Ct);

        var v = result.Value;
        (v.Frozen, v.Learning, v.Answer).ShouldBe((null, null, null));
        var c = v.SelectionVsBaseline!;
        c.Answer.ShouldBe("selection_better");
        (c.Selection.Arm, c.Baseline.Arm).ShouldBe(("selection", "baseline"));
        c.Days.ShouldBe(30);
        c.Label.ShouldContain("not random stocks");
        c.Shortcut.ShouldContain("realised");
        v.Hindsight.Select(l => l.Code).ShouldContain(HindsightLabels.BaselineArm);
    }

    [Fact]
    public async Task While_an_arm_is_running_there_is_no_answer_and_the_reason_names_the_states()
    {
        var (s, b) = (Run("selection", "completed"), Run("baseline", "running"));
        var reads = Reads(s, b, [], []);

        var result = await new GetReplayPairHandler(reads).HandleAsync(new GetReplayPairQuery(Pair), Ct);

        var c = result.Value.SelectionVsBaseline!;
        c.Answer.ShouldBeNull();
        c.Reason.ShouldNotBeNull().ShouldContain("baseline running");
    }
}
