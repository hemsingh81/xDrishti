#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Market.Lists;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Planning;

/// <summary>
/// DATA-01 (ADR 0274) criteria 1, 5 and 6 through the real selection builder with fake ports: OFF is the plan of a builder with no filter and the port is not read; ON feeds OnFnoBan and UnderSurveillance (ban date = the plan's date, surveillance date = the build session);
/// a date with no row stays unavailable and holds, a failing read is unavailable too, never a pass.
/// </summary>
public sealed class ListFilterPlanTests
{
    private static readonly SelectionRules BanRequired = new(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (SelectionPlanBuilder Builder, IListFlagsAsOf Port, IReadOnlyList<TicketCandidate> Found) Build(bool enabled, Func<IReadOnlyList<TicketCandidate>, ListFlags>? flags = null, bool throws = false, bool withFilter = true)
    {
        var found = WiredPlanFixture.Candidates();
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
        var port = Substitute.For<IListFlagsAsOf>();
        if (throws)
        {
            port.ForAsync(default!, default, default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("away"));
        }
        else
        {
            port.ForAsync(default!, default, default, default).ReturnsForAnyArgs(_ => ValueTask.FromResult(flags?.Invoke(found) ?? ListFlags.None));
        }

        var settings = Substitute.For<IListFilterSettings>();
        settings.Enabled.Returns(enabled);
        var filter = withFilter ? new PlanListFilter(settings, port, NullLogger<PlanListFilter>.Instance) : null;
        return (new SelectionPlanBuilder(source, context, NullLogger<SelectionPlanBuilder>.Instance, BanRequired, null, null, null, null, null, null, filter), port, found);
    }

    private static ListFlags Flags(bool ban, bool surveillance, IEnumerable<Guid>? banned = null, IEnumerable<Guid>? surveilled = null) =>
        new(ban, surveillance, new HashSet<Guid>(banned ?? []), new HashSet<Guid>(surveilled ?? []));

    [Fact]
    public async Task With_the_switch_off_the_plan_is_the_plan_of_a_builder_with_no_filter_and_the_port_is_not_read()
    {
        var off = Build(enabled: false, _ => Flags(true, true));
        var none = Build(enabled: false, withFilter: false);

        var a = (await off.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;
        var b = (await none.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        a.InputHash.ShouldBe(b.InputHash);
        a.Basis.ShouldBe(b.Basis);
        a.Items.SelectMany(i => i.Evidence.Unavailable).ShouldContain("fno_ban");
        await off.Port.DidNotReceiveWithAnyArgs().ForAsync(default!, default, default, default);
    }

    [Fact]
    public async Task With_the_switch_off_the_plan_hash_and_basis_are_the_literal_values_of_the_builder_before_the_change()
    {
        var plan = (await Build(enabled: false, _ => Flags(true, true)).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        // computed on main b8d060f9, the builder without any list filter
        plan.InputHash.ShouldBe("0e5cbaae83e18db124003392934e1f0fdc82fe3f26d80fa42331816a6611b171");
        plan.Basis.ShouldBe("Proposed, provisional: no order exists, nothing here is approved. Cell statistics only (shrunk replayed-history estimates); tickets are conditional on tomorrow's bars; sizes use the close as entry. Open book: assumed empty by owner setting (positions held elsewhere are not counted). market gate unavailable (vix, breadth, events not known): the plan is NOT treated as a normal day; judge the market yourself equity throttle inactive: 0 real taken trades closed before the decision date, 20 needed");
    }

    [Fact]
    public async Task With_no_candidates_the_filters_are_not_reported_as_applied()
    {
        var built = Build(true, _ => Flags(true, true));
        var none = Substitute.For<ITicketSource>();
        none.ReadAsync(WiredPlanFixture.Session, WiredPlanFixture.ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult([], 0, 0, []))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
        var settings = Substitute.For<IListFilterSettings>();
        settings.Enabled.Returns(true);
        var builder = new SelectionPlanBuilder(none, context, NullLogger<SelectionPlanBuilder>.Instance, BanRequired, null, null, null, null, null, null, new PlanListFilter(settings, built.Port, NullLogger<PlanListFilter>.Instance));

        var plan = (await builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.ShouldBeEmpty();
        var withCandidates = (await Build(true, _ => Flags(true, true)).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;
        withCandidates.Items.SelectMany(i => i.Evidence.Unavailable).ShouldNotContain("fno_ban", "every candidate was answered, so it is applied");
        var unanswered = (await Build(true, _ => Flags(false, false)).Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;
        unanswered.Items.SelectMany(i => i.Evidence.Unavailable).ShouldContain("fno_ban");
        unanswered.Items.SelectMany(i => i.Evidence.Unavailable).ShouldContain("surveillance");
    }

    [Fact]
    public async Task With_the_switch_on_a_banned_share_is_rejected_with_fno_ban_and_the_others_are_not()
    {
        var built = Build(true, found => Flags(true, true, banned: [found.First(c => c.Draft.Symbol == "AAA").InstrumentId!.Value]));

        var plan = (await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.Single(i => i.Symbol == "AAA").Reasons.ShouldContain("fno_ban:fail");
        plan.Items.Single(i => i.Symbol == "AAA").Group.ShouldBe(TicketGroup.Rejected);
        plan.Items.Where(i => i.Symbol != "AAA").SelectMany(i => i.Reasons).ShouldNotContain("fno_ban:fail");
        plan.Items.Where(i => i.Group == TicketGroup.Primary).ShouldNotBeEmpty("the day the ban ends the share is no longer rejected by the filter, and the others pass it");
        await built.Port.Received().ForAsync(Arg.Any<IReadOnlyCollection<Guid>>(), WiredPlanFixture.ForDate, WiredPlanFixture.Session, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_share_under_surveillance_is_rejected_with_surveillance()
    {
        var built = Build(true, found => Flags(true, true, surveilled: [found.First(c => c.Draft.Symbol == "BBB").InstrumentId!.Value]));

        var plan = (await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.Single(i => i.Symbol == "BBB").Reasons.ShouldContain("surveillance:fail");
    }

    [Fact]
    public async Task A_date_with_no_stored_row_reads_unavailable_and_holds_every_candidate_never_a_pass()
    {
        var built = Build(true, _ => Flags(false, false));

        var plan = (await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.Where(i => i.Group == TicketGroup.Primary).ShouldBeEmpty();
        plan.Items.SelectMany(i => i.Reasons).ShouldContain("fno_ban:unavailable");
        plan.Items.SelectMany(i => i.Reasons).ShouldNotContain("fno_ban:fail");
    }

    [Fact]
    public async Task A_port_that_cannot_be_read_makes_both_lists_unavailable_instead_of_throwing_into_the_plan()
    {
        var built = Build(true, throws: true);

        var plan = (await built.Builder.BuildAsync(WiredPlanFixture.Request, Ct)).Value;

        plan.Items.Where(i => i.Group == TicketGroup.Primary).ShouldBeEmpty();
        plan.Items.SelectMany(i => i.Reasons).ShouldContain("surveillance:unavailable");
    }

    [Fact]
    public void The_flags_answer_null_for_a_list_that_is_not_covered_and_false_only_for_a_covered_one()
    {
        var id = Guid.NewGuid();

        Flags(false, false).OnBan(id).ShouldBeNull();
        Flags(false, false).UnderSurveillance(id).ShouldBeNull();
        Flags(true, true).OnBan(id).ShouldBe(false);
        Flags(true, true, banned: [id]).OnBan(id).ShouldBe(true);
        Flags(true, false).UnderSurveillance(id).ShouldBeNull();
    }
}
