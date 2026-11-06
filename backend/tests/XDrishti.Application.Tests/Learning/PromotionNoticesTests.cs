using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;

namespace XDrishti.Application.Tests.Learning;

/// <summary>CF-10 part 10d-1 review (ADR 0220): an automatic promotion is never silent. It stays on the bell for three days as a resolved notice; an owner's designation, a demotion and an older promotion are not shown.</summary>
public sealed class PromotionNoticesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset At = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private static NewChangelogEntry Designation(long model, string to, string actor, DateTimeOffset at, string kind = "designation") =>
        new(kind, "model", $"model:{model}", to == "champion" ? "challenger" : "champion", to, "why", actor, [$"model:{model}"], null, $"model:{model}", null, null, at);

    [Fact]
    public async Task An_automatic_promotion_is_shown_on_day_two_as_a_resolved_notice_and_is_gone_on_day_four()
    {
        var log = new ChangelogHandlersTests.FakeChangelog();
        log.Add(Designation(7, "champion", MlActors.Auto, At));
        var notices = new PromotionNotices(log);

        var day2 = await notices.ListAsync(At.AddDays(2), Ct);
        var day4 = await notices.ListAsync(At.AddDays(4), Ct);

        var notice = day2.Single();
        (notice.Kind, notice.Severity, notice.Resolved).ShouldBe(("promotion_applied_auto", "info", true));
        notice.Id.ShouldBe("promotion-auto:1");
        notice.Text.ShouldContain("model 7");
        day4.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_owners_designation_a_demotion_and_other_kinds_of_rows_are_not_shown()
    {
        var log = new ChangelogHandlersTests.FakeChangelog();
        log.Add(Designation(6, "champion", "owner:me@x.test", At));
        log.Add(Designation(7, "challenger", MlActors.Auto, At)); // a rollback's demotion has its own notice
        log.Add(Designation(8, "champion", "cli:xd", At));
        log.Add(Designation(9, "champion", MlActors.Auto, At, kind: "transition"));

        (await new PromotionNotices(log).ListAsync(At.AddHours(1), Ct)).ShouldBeEmpty();
    }
}
