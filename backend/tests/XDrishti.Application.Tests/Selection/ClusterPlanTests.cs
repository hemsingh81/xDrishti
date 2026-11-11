#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Application.Tests.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Selection;

/// <summary>
/// BK-08 criteria 3, 5 and 6 through the real selection builder on the seeded P7-09 fixture. Correlations of the fixture: AAA-BBB 0.3, AAA-CCC 0.2, BBB-CCC 0.4, AAA-DDD 0.9, BBB-DDD 0.1, CCC-DDD 0.1; FFF has none.
/// At a cut of 0.35 the clusters over the symbols that reach the fill are {AAA}, {BBB, CCC} (0.4), and FFF is unknown (DDD is dropped by the correlation-aware selection before the fill). Each ticket risks 0.5 % of capital. With the cap OFF the plan, its hash included, is what it was.
/// </summary>
public sealed class ClusterPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<NewPlan> BuildAsync(FakeBasketSettingsStore? settings)
    {
        var found = WiredPlanFixture.Candidates();
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
        var builder = new SelectionPlanBuilder(source, context, NullLogger<SelectionPlanBuilder>.Instance, WiredPlanFixture.Loose, basketSettings: settings);
        return (await builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;
    }

    private static NewPlanItem Item(NewPlan plan, string symbol) => plan.Items.First(i => i.Symbol == symbol);

    private static FakeBasketSettingsStore On(double cap, double rho = 0.35) => new(BasketSettings.Defaults with { MaxClusterRiskPct = cap, ClusterRho = rho });

    [Fact]
    public async Task With_the_cap_off_the_plan_and_its_hash_are_identical_to_a_builder_without_the_port_even_with_other_settings_stored()
    {
        var without = await BuildAsync(null);
        var defaults = await BuildAsync(new FakeBasketSettingsStore());
        var otherKeys = await BuildAsync(new FakeBasketSettingsStore(BasketSettings.Defaults with { ClusterRho = 0.9, BreadthSource = BasketSettings.SourceOf(Guid.NewGuid()) }));

        defaults.InputHash.ShouldBe(without.InputHash);
        otherKeys.InputHash.ShouldBe(without.InputHash);
        defaults.Items.Select(i => (i.Group, i.Symbol, string.Join(',', i.Reasons), i.Allocations.Sum(a => a.Quantity))).ShouldBe(without.Items.Select(i => (i.Group, i.Symbol, string.Join(',', i.Reasons), i.Allocations.Sum(a => a.Quantity))));
        defaults.Items.SelectMany(i => i.Reasons).ShouldNotContain(r => r.StartsWith("cluster", StringComparison.Ordinal));
        defaults.Items.SelectMany(i => i.Reasons).ShouldNotContain(r => r.StartsWith("cluster_cap", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_cap_on_taken_tickets_say_their_cluster_and_open_risk_against_the_cap()
    {
        var plan = await BuildAsync(On(0.8));

        Item(plan, "AAA").Group.ShouldBe(TicketGroup.Primary);
        Item(plan, "AAA").Reasons.ShouldContain("cluster {AAA}: open risk 0.5% of cap 0.8%"); // DDD never reached the fill (the correlation-aware selection dropped it), so only the plan-day symbols are clustered
        Item(plan, "BBB").Group.ShouldBe(TicketGroup.Primary);
        Item(plan, "BBB").Reasons.ShouldContain("cluster {BBB, CCC}: open risk 0.5% of cap 0.8%");
    }

    [Fact]
    public async Task A_candidate_that_breaks_a_cap_with_the_picks_goes_to_the_reserve_and_the_reasons_keep_the_order_of_the_limits()
    {
        var plan = await BuildAsync(On(0.8));

        var ccc = Item(plan, "CCC");
        ccc.Group.ShouldBe(TicketGroup.Reserve);
        ccc.Reasons.ShouldContain("cluster_cap:fail");
        ccc.Reasons.ToList().IndexOf("sector:fail").ShouldBeLessThan(ccc.Reasons.ToList().IndexOf("cluster_cap:fail")); // the sector count comes before the cluster cap
        ccc.Reasons.ShouldContain("cluster {BBB, CCC}: open risk 1% of cap 0.8%");
    }

    [Fact]
    public async Task An_unknown_correlation_blocks_the_ticket_and_the_text_says_so()
    {
        var plan = await BuildAsync(On(0.8));

        var fff = Item(plan, "FFF");
        fff.Group.ShouldNotBe(TicketGroup.Primary);
        fff.Reasons.ShouldContain("cluster_cap:unavailable");
        fff.Reasons.ShouldContain(r => r.Contains("unknown correlation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_checklist_check_is_added_by_the_cluster_cap()
    {
        var off = await BuildAsync(null);
        var on = await BuildAsync(On(5.0));

        on.Items.Where(i => i.Group == TicketGroup.Primary).SelectMany(i => i.Evidence.Unavailable).Where(u => u.StartsWith("checklist:", StringComparison.Ordinal)).Order().ShouldBe(off.Items.Where(i => i.Group == TicketGroup.Primary).SelectMany(i => i.Evidence.Unavailable).Where(u => u.StartsWith("checklist:", StringComparison.Ordinal)).Order());
    }

    [Fact]
    public async Task A_cap_large_enough_to_bind_nothing_takes_the_same_primaries_as_off()
    {
        var off = await BuildAsync(null);
        var on = await BuildAsync(On(5.0));

        on.Items.Where(i => i.Group == TicketGroup.Primary).Select(i => i.Symbol).ShouldBe(off.Items.Where(i => i.Group == TicketGroup.Primary).Select(i => i.Symbol));
    }
}
