using NSubstitute;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>BK-11b criteria 1 to 5 and 7 at the handler: the basis is opt-in, a dated run replays each day with that day's members, a basket without dated rows is refused and never labelled dated, and the structured fields and the key line are there only when used.</summary>
public sealed class RunSimulationDatedMembersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTimeOffset Start(DateOnly day) => new(day.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    /// <summary>{A, B} on D1, {A, C} from D2 (the hand case of the spec, in the days of the fixture).</summary>
    private static (RunFixture Fixture, Guid Basket, Guid A, Guid B, Guid C) HandCase()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var b = fixture.Add("NSE:BBB", RunFixture.ThreeDays());
        var c = fixture.Add("NSE:CCC", RunFixture.ThreeDays());
        var basket = Guid.CreateVersion7();
        fixture.SetStays(basket, "Nifty watch", null,
            (a, "NSE:AAA", 0, Start(RunFixture.D1).AddDays(-60), null),
            (b, "NSE:BBB", 1, Start(RunFixture.D1).AddDays(-60), Start(RunFixture.D2)),
            (c, "NSE:CCC", 2, Start(RunFixture.D2), null));
        fixture.SetBasket(basket, "Nifty watch", null, (a, "NSE:AAA", 0, 5m, false), (c, "NSE:CCC", 1, 5m, false));
        return (fixture, basket, a, b, c);
    }

    private static SimulationRequest Dated(Guid basket) => RunFixture.Basket(basket) with { MembersBasis = UniverseBasisRecord.Dated };

    [Fact]
    public async Task A_request_without_a_basis_is_what_it_was_no_structured_field_no_key_line_and_the_old_label()
    {
        var (fixture, basket, _, _, _) = HandCase();

        var run = (await fixture.RunAsync(RunFixture.Basket(basket), Ct)).Value;

        run.UniverseBasis.ShouldBeNull();
        run.Universe.Label.ShouldBe(UniverseLabels.Basket(basket));
        await fixture.Baskets.DidNotReceive().StaysAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        fixture.Engine.Replays.ShouldAllBe(r => r.MemberOn == null);
    }

    [Fact]
    public async Task A_dated_run_replays_each_instrument_with_its_member_days_and_says_so_in_structured_fields()
    {
        var (fixture, basket, a, b, c) = HandCase();

        var run = (await fixture.RunAsync(Dated(basket), Ct)).Value;

        run.Universe.Label.ShouldBe(UniverseLabels.Dated(basket, null));
        run.Universe.Members.Select(m => m.Id).ShouldBe([a, b, c], "every instrument that is a member on some day of the window, in the basket's order");
        var basis = run.UniverseBasis.ShouldNotBeNull();
        (basis.UniverseBasketId, basis.UniverseBasis, basis.MembersAsOf, basis.UniverseParamsHash).ShouldBe((basket, "dated", null, null));
        basis.MemberDaysHash.ShouldNotBeNull().Length.ShouldBe(64);
        var onB = fixture.Engine.Replays.Single(r => r.InstrumentId == b).MemberOn.ShouldNotBeNull();
        var onC = fixture.Engine.Replays.Single(r => r.InstrumentId == c).MemberOn.ShouldNotBeNull();
        onB(RunFixture.D1).ShouldBeTrue();
        onB(RunFixture.D2).ShouldBeFalse("a signal of B on the day it left is not taken");
        onC(RunFixture.D1).ShouldBeFalse("a signal of C before it joined is not taken");
        onC(RunFixture.D2).ShouldBeTrue();
        await fixture.Baskets.Received(1).StaysAsync(basket, Arg.Any<CancellationToken>());
        await fixture.Baskets.DidNotReceive().MembersAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset?>(), Arg.Any<DateOnly?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_key_line_makes_two_dated_runs_over_different_histories_different_runs_and_a_repeat_the_same()
    {
        var (fixture, basket, a, b, c) = HandCase();
        var first = (await fixture.RunAsync(Dated(basket), Ct)).Value;
        var again = (await fixture.RunAsync(Dated(basket), Ct)).Value;
        var plain = (await fixture.RunAsync(RunFixture.Basket(basket), Ct)).Value;

        fixture.SetStays(basket, "Nifty watch", null,
            (a, "NSE:AAA", 0, Start(RunFixture.D1).AddDays(-60), null),
            (b, "NSE:BBB", 1, Start(RunFixture.D1).AddDays(-60), Start(RunFixture.D3)),
            (c, "NSE:CCC", 2, Start(RunFixture.D3), null));
        var other = (await fixture.RunAsync(Dated(basket), Ct)).Value;

        again.RunKey.ShouldBe(first.RunKey);
        other.RunKey.ShouldNotBe(first.RunKey);
        plain.RunKey.ShouldNotBe(first.RunKey);
    }

    [Fact]
    public async Task BK_11b_the_basis_for_the_trade_cards_carries_the_membership_of_a_dated_run_and_none_for_another()
    {
        var (fixture, basket, _, b, _) = HandCase();
        var dated = new ExplainBasisSink();
        var plain = new ExplainBasisSink();

        (await fixture.Handler.HandleAsync(new RunSimulationQuery(Dated(basket), null, dated), Ct)).IsSuccess.ShouldBeTrue();
        (await fixture.Handler.HandleAsync(new RunSimulationQuery(RunFixture.Basket(basket), null, plain), Ct)).IsSuccess.ShouldBeTrue();

        dated.Basis!.Members.ShouldNotBeNull().IsMember(b, RunFixture.D2).ShouldBeFalse();
        plain.Basis!.Members.ShouldBeNull();
    }

    [Fact]
    public async Task A_snapshot_basis_stores_the_run_date_keeps_the_label_and_differs_from_a_run_that_did_not_say()
    {
        var (fixture, basket, _, _, _) = HandCase();

        var plain = (await fixture.RunAsync(RunFixture.Basket(basket), Ct)).Value;
        var run = (await fixture.RunAsync(RunFixture.Basket(basket) with { MembersBasis = UniverseBasisRecord.SingleSnapshot }, Ct)).Value;

        var basis = run.UniverseBasis.ShouldNotBeNull();
        (basis.UniverseBasis, basis.MemberDaysHash).ShouldBe(("single_snapshot", null));
        basis.MembersAsOf.ShouldBe(IndiaTime.Today(fixture.Time.GetUtcNow()));
        run.Universe.Label.ShouldBe(UniverseLabels.Basket(basket));
        run.RunKey.ShouldNotBe(plain.RunKey);
    }

    [Fact]
    public async Task Dated_members_on_a_basket_without_dated_rows_are_refused_with_the_first_dated_day_and_nothing_is_read()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("NSE:AAA", RunFixture.ThreeDays());
        var basket = Guid.CreateVersion7();
        fixture.SetStays(basket, "Fresh", null, (a, "NSE:AAA", 0, Start(RunFixture.D1).AddDays(-1), null));

        var result = await fixture.RunAsync(Dated(basket), Ct);

        result.Error.Code.ShouldBe(SimulationErrors.MembersNotDatedCode);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Message.ShouldContain(RunFixture.D1.AddDays(-1).ToString("yyyy-MM-dd"));
        result.Error.Message.ShouldContain("single_snapshot");
        fixture.Store.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task Dated_members_are_refused_when_the_history_starts_after_the_window_start()
    {
        var (fixture, basket, _, _, _) = HandCase();

        var result = await fixture.RunAsync(Dated(basket) with { From = RunFixture.D1.AddDays(-90) }, Ct);

        result.Error.Code.ShouldBe(SimulationErrors.MembersNotDatedCode);
    }

    [Fact]
    public async Task An_unknown_or_archived_basket_is_not_found_for_a_dated_run_too()
    {
        var fixture = new RunFixture();
        var a = fixture.Add("NSE:AAA");
        var archived = Guid.CreateVersion7();
        fixture.SetStays(archived, "Old", DateTimeOffset.UnixEpoch, (a, "NSE:AAA", 0, DateTimeOffset.UnixEpoch, null));

        (await fixture.RunAsync(Dated(Guid.CreateVersion7()), Ct)).Error.Code.ShouldBe(SimulationErrors.BasketNotFoundCode);
        (await fixture.RunAsync(Dated(archived), Ct)).Error.Code.ShouldBe(SimulationErrors.BasketNotFoundCode);
    }

    [Fact]
    public async Task A_basis_is_refused_for_a_list_and_an_unknown_basis_is_refused_for_a_basket()
    {
        var (fixture, basket, a, _, _) = HandCase();

        var list = await fixture.RunAsync(RunFixture.List([a]) with { MembersBasis = "dated" }, Ct);
        var unknown = await fixture.RunAsync(RunFixture.Basket(basket) with { MembersBasis = "weekly" }, Ct);

        list.Error.Code.ShouldBe(SimulationErrors.InvalidCode);
        list.Error.Fields.ShouldNotBeNull().ShouldContain(f => f.Field == "membersBasis");
        unknown.Error.Code.ShouldBe(SimulationErrors.InvalidCode);
    }
}
