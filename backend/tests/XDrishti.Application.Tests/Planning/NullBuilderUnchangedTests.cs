using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>With <c>Planning:Builder:UseSelection</c> off (the default) the nightly plan is exactly what it was before P7-09 (spec P7-09 criterion 3): these literals were taken from the skeleton before the wiring was written.</summary>
public sealed class NullBuilderUnchangedTests
{
    [Fact]
    public async Task The_skeleton_plan_is_byte_for_byte_the_one_it_always_was()
    {
        var friday = new DateOnly(2026, 10, 23);
        var monday = new DateOnly(2026, 10, 26);
        var now = new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero);

        var plan = (await new NullPlanBuilder().BuildAsync(new PlanBuildRequest(friday, monday, NightlyRules.ReviewCutoff(monday), now), TestContext.Current.CancellationToken)).Value;

        plan.PlanKey.ShouldBe("nightly:2026-10-26:skeleton");
        plan.Basis.ShouldBe("skeleton: no tickets are built yet (the real builder is P7-04/P7-09)");
        plan.InputHash.ShouldBe("f70a2b9dcd1a2953b47b492b24133063c6ed88f0d4e852bdcf0a4f693ca8debf");
        plan.Items.ShouldBeEmpty();
        plan.Provisional.ShouldBeTrue();
        plan.CreatedBy.ShouldBe("system:nightly-skeleton");
        plan.Version.ShouldBe(1);
    }
}
