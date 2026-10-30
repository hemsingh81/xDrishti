using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Tests.Limits;

/// <summary>Spec P12-09 criterion 8: each of doc 19 §4's four always-ask items maps to at least one enforcement point, and every point's probe holds in this build (a loosened rule breaks the list).</summary>
public sealed class AlwaysAskListTests
{
    [Fact]
    public void The_list_has_exactly_the_four_items_of_doc_19()
    {
        AlwaysAskList.Items.Select(i => i.Code).ShouldBe([AlwaysAskList.RiskIncrease, AlwaysAskList.NewStrategyLive, AlwaysAskList.EnableExecution, AlwaysAskList.AccountChanges]);
    }

    public static TheoryData<string> Items() => [.. AlwaysAskList.Items.Select(i => i.Code)];

    [Theory]
    [MemberData(nameof(Items))]
    public void Every_item_has_an_enforcement_point_and_every_point_holds(string code)
    {
        var item = AlwaysAskList.Items.Single(i => i.Code == code);

        item.Points.ShouldNotBeEmpty();
        foreach (var point in item.Points)
        {
            point.Where.ShouldNotBeNullOrWhiteSpace();
            point.Holds().ShouldBeTrue(point.Where);
        }
    }

    [Fact]
    public void Stop_loosening_adding_and_re_entry_are_refused_in_auto_live_and_semi_auto()
    {
        foreach (var mode in new[] { ExecMode.AutoLive, ExecMode.SemiAuto })
        {
            foreach (var action in new[] { ActionClass.StopLoosen, ActionClass.AddToPosition, ActionClass.ReEntry })
            {
                ModeRouting.Decide(mode, action, ExecSide.Buy).ShouldBe(new RouteDecision(RouteKind.Refused, ModeRouting.NotSupported));
            }
        }
    }
}
