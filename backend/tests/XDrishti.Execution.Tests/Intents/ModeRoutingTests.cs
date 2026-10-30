using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Tests.Intents;

/// <summary>
/// Spec P12-07 criterion 12 and P12-08 criterion 13: the routing table is total over every mode (and no answer) and every action class, and pinned cell by cell; entries are BUY only; the three risk-adding classes and outside-plan actions are
/// refused in every mode with their codes. Also the crossing test of <see cref="TriggerWatcher"/> and the void rule.
/// </summary>
public sealed class ModeRoutingTests
{
    // Columns: Entry, StopTighten, StopLoosen, ExitReduce, CancelPending, AddToPosition, ReEntry, OutsidePlan.
    private static readonly Dictionary<string, RouteKind[]> Table = new()
    {
        ["none"] = [RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.Refused, RouteKind.Refused],
        [nameof(ExecMode.Paper)] = [RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.Refused, RouteKind.Refused],
        [nameof(ExecMode.ManualLive)] = [RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.Refused, RouteKind.Refused],
        [nameof(ExecMode.Assisted)] = [RouteKind.PrepareSheet, RouteKind.None, RouteKind.Refused, RouteKind.None, RouteKind.None, RouteKind.Refused, RouteKind.Refused, RouteKind.Refused],
        [nameof(ExecMode.SemiAuto)] = [RouteKind.AwaitConfirm, RouteKind.Automatic, RouteKind.Refused, RouteKind.Automatic, RouteKind.Automatic, RouteKind.Refused, RouteKind.Refused, RouteKind.Refused],
        [nameof(ExecMode.AutoLive)] = [RouteKind.Automatic, RouteKind.Automatic, RouteKind.Refused, RouteKind.Automatic, RouteKind.Automatic, RouteKind.Refused, RouteKind.Refused, RouteKind.Refused],
    };

    public static TheoryData<string, ActionClass> Cells()
    {
        var data = new TheoryData<string, ActionClass>();
        foreach (var mode in Table.Keys)
        {
            foreach (var action in Enum.GetValues<ActionClass>())
            {
                data.Add(mode, action);
            }
        }

        return data;
    }

    [Fact]
    public void The_table_covers_every_mode_and_every_action_class()
    {
        Table.Keys.Where(k => k != "none").Order().ShouldBe(Enum.GetNames<ExecMode>().Order());
        Table.Values.ShouldAllBe(row => row.Length == Enum.GetValues<ActionClass>().Length);
        Enum.GetValues<ActionClass>().Length.ShouldBe(8);
    }

    [Theory]
    [MemberData(nameof(Cells))]
    public void Every_cell_routes_as_the_table_says(string mode, ActionClass action)
    {
        ExecMode? m = mode == "none" ? null : Enum.Parse<ExecMode>(mode);

        ModeRouting.Route(m, action).ShouldBe(Table[mode][(int)action]);
    }

    [Theory]
    [InlineData(ActionClass.StopLoosen, ModeRouting.NotSupported)]
    [InlineData(ActionClass.AddToPosition, ModeRouting.NotSupported)]
    [InlineData(ActionClass.ReEntry, ModeRouting.NotSupported)]
    [InlineData(ActionClass.OutsidePlan, ModeRouting.OutsidePlanCode)]
    public void A_refused_class_carries_its_code_in_every_mode(ActionClass action, string code)
    {
        foreach (ExecMode? mode in new ExecMode?[] { null, ExecMode.Paper, ExecMode.ManualLive, ExecMode.Assisted, ExecMode.SemiAuto, ExecMode.AutoLive })
        {
            ModeRouting.Decide(mode, action, ExecSide.Buy).ShouldBe(new RouteDecision(RouteKind.Refused, code));
        }
    }

    [Theory]
    [InlineData(ExecMode.SemiAuto)]
    [InlineData(ExecMode.AutoLive)]
    [InlineData(ExecMode.Assisted)]
    [InlineData(ExecMode.Paper)]
    public void A_sell_entry_is_refused_not_supported_in_every_mode(ExecMode mode)
    {
        ModeRouting.Decide(mode, ActionClass.Entry, ExecSide.Sell).ShouldBe(new RouteDecision(RouteKind.Refused, ModeRouting.NotSupported));
    }

    [Fact]
    public void A_buy_entry_in_semi_auto_waits_for_the_tap_and_a_reducing_action_runs_by_itself()
    {
        ModeRouting.Decide(ExecMode.SemiAuto, ActionClass.Entry, ExecSide.Buy).ShouldBe(new RouteDecision(RouteKind.AwaitConfirm, null));
        ModeRouting.Decide(ExecMode.SemiAuto, ActionClass.StopTighten, ExecSide.Sell).ShouldBe(new RouteDecision(RouteKind.Automatic, null));
    }

    [Fact]
    public void An_unknown_mode_routes_nothing_and_an_unknown_class_is_refused()
    {
        ModeRouting.Route((ExecMode)99, ActionClass.Entry).ShouldBe(RouteKind.None);
        ModeRouting.Route((ExecMode)99, ActionClass.ExitReduce).ShouldBe(RouteKind.None);
        ModeRouting.Decide(ExecMode.SemiAuto, (ActionClass)99, ExecSide.Buy).ShouldBe(new RouteDecision(RouteKind.Refused, ModeRouting.OutsidePlanCode));
    }

    [Theory]
    [InlineData(ExecSide.Buy, 412.35, 412.34, false)]
    [InlineData(ExecSide.Buy, 412.35, 412.35, true)]
    [InlineData(ExecSide.Buy, 412.35, 450, true)]
    [InlineData(ExecSide.Sell, 412.35, 412.36, false)]
    [InlineData(ExecSide.Sell, 412.35, 412.35, true)]
    [InlineData(ExecSide.Buy, 0, 412.35, false)]
    [InlineData(ExecSide.Buy, 412.35, 0, false)]
    public void The_trigger_is_crossed_at_or_beyond_its_level_on_the_side_of_the_entry(ExecSide side, double trigger, double last, bool crossed)
    {
        TriggerWatcher.TriggerCrossed(side, (decimal)trigger, (decimal)last).ShouldBe(crossed);
    }

    [Theory]
    [InlineData(ExecSide.Buy, 100, 102, false)] // 1.96 % of the last price
    [InlineData(ExecSide.Buy, 100, 102.05, true)] // 2.009 %
    [InlineData(ExecSide.Buy, 100, 90, false)] // not crossed at all
    [InlineData(ExecSide.Sell, 100, 97.9, true)] // 2.15 %
    public void A_trigger_is_passed_beyond_only_on_its_side_and_by_more_than_the_collar(ExecSide side, double trigger, double last, bool beyond)
    {
        TriggerWatcher.PassedBeyond(side, (decimal)trigger, (decimal)last, 2.0m).ShouldBe(beyond);
    }

    [Theory]
    [InlineData(120, false, 120)]
    [InlineData(120, true, 1)]
    [InlineData(1, true, 1)]
    public void The_canary_caps_the_quantity_at_one_share(int allocation, bool canary, int expected)
    {
        IntentRules.Quantity(allocation, canary).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, true, true, true, IntentReasons.Halted)]
    [InlineData(true, false, false, false, IntentReasons.Halted)]
    [InlineData(false, false, false, false, IntentReasons.ModeChanged)]
    [InlineData(false, true, false, false, IntentReasons.MissedNotReady)]
    [InlineData(false, true, true, false, IntentReasons.Window)]
    [InlineData(false, true, true, true, null)]
    public void On_a_crossing_the_strongest_cause_names_the_cancellation(bool halted, bool mayAct, bool ready, bool inWindow, string? reason)
    {
        IntentRules.OnCrossing(halted, mayAct, ready, inWindow).ShouldBe(reason);
    }
}
