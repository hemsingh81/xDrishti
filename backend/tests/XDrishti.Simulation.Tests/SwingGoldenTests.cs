using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;
using XDrishti.Simulation.Walking;
using static XDrishti.Simulation.Tests.Support.TestCalendars;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// The golden text of the swing rows (P3-03d, criterion 13 and 16): the trades M1 to M7b as the walker renders them, fixture R through the simulator with its trace (the same candidate, the same trade, the same counters) and the replay
/// record of the flagged boundary Q1. A new file next to the five regenerated ones; see <see cref="GoldenFile"/> (first line the engine version, <c>XD_UPDATE_GOLDEN=1</c> rewrites it).
/// </summary>
public sealed class SwingGoldenTests
{
    [Fact]
    public void The_golden_file_equals_the_rendering_of_the_swing_rows_fixture_R_and_the_flagged_boundary()
    {
        GoldenFile.Check("swing", RenderAll());
    }

    [Fact]
    public void The_rendering_is_the_same_twice()
    {
        RenderAll().ShouldBe(RenderAll());
    }

    private static string RenderAll()
    {
        var text = new StringBuilder();
        var total = WalkCounters.Empty;
        foreach (var swingCase in SwingCases.All)
        {
            var hand = swingCase.Hand;
            var result = TradeWalker.Walk(hand.Candidate(), hand.Bars, hand.Rules(FillRules.Default));
            text.Append(hand.Name).Append(' ').Append(OutcomeFormatter.Render(result)).Append('\n');
            total = total.Add(result.Value.ToCounters());
        }

        text.Append("total ").Append(OutcomeFormatter.Render(total)).Append('\n');

        var run = Sim.Run(TestStrategies.Swing1h(), FixtureR.Bars(Fri), Timeframe.H1, calendar: DefaultWeek());
        text.Append("fixture R\n").Append(Render.Result(run));

        var replay = Rep.Replay(Rep.Prepare(TestStrategies.Swing1h(), Timeframe.H1, FillRules.Default), FixtureR.CrashBars(Wed, "09:15 60.00/60.00/60.00/60.00"), Mon, Wed, calendar: DefaultWeek());
        text.Append("flagged boundary Q1\n").Append(RunText.Replay(replay));
        return text.ToString();
    }
}
