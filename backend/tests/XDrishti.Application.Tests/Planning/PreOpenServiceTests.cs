using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Application.Planning;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P7-11: the two services start paused, sit at the right times and do not clash with the review cutoff or the data run.</summary>
public sealed class PreOpenServiceTests
{
    [Fact]
    public void The_pre_open_check_runs_at_09_10_on_trading_days_starts_paused_and_does_not_catch_up()
    {
        var d = PreOpenService.Definition;

        d.Id.ShouldBe("preopen");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(9, 10)));
        d.Defaults.CatchUp.ShouldBeFalse();
        d.Constraints.MustFollow.ShouldBe([ReviewCutoffService.Id]);
        d.Constraints.StartNotAfter.ShouldBe(new TimeOnly(9, 14));
    }

    [Fact]
    public void The_expiry_runs_after_the_close_and_starts_paused()
    {
        var d = PlanExpiryService.Definition;

        d.Id.ShouldBe("plan-expiry");
        d.Defaults.Enabled.ShouldBeFalse();
        d.Defaults.Schedule.ShouldBe(Schedule.Daily(new TimeOnly(15, 40)));
        d.Constraints.NotBeforeSessionEnd.ShouldBeTrue();
    }

    [Fact]
    public void Their_defaults_beside_the_review_cutoff_raise_no_schedule_issue()
    {
        var subjects = new[] { ReviewCutoffService.Definition, PreOpenService.Definition, PlanExpiryService.Definition, EodService.Definition }.Select(d => new RuleSubject(d, d.Defaults)).ToList();

        ScheduleRules.Evaluate(subjects).ShouldBeEmpty();
    }

    [Fact]
    public void The_descriptions_say_that_no_order_is_placed()
    {
        PreOpenService.Definition.Description.ShouldContain("No order is placed");
        PlanExpiryService.Definition.Description.ShouldContain("never places an order");
    }
}
