using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Tests.Planning;

/// <summary>Spec P7-11 over fakes: the pre-open check arms or invalidates approved tickets only, never on a missing input, never from the reserve, and the expiry ends the day.</summary>
public sealed class PreOpenCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_approved_ticket_with_a_small_gap_is_armed_by_the_service_and_the_plan_follows()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Value.Armed.ShouldBe(1);
        f.StateOf(5).ShouldBe(TicketState.Armed);
        f.Moves.ShouldHaveSingleItem().Actor.ShouldBe("system:preopen");
        f.Moves[0].Reason.ShouldStartWith(PlanWording.Armed);
        f.PlanState.ShouldBe(PlanState.Armed);
        await f.Notifier.Received(1).ActiveSetChangedAsync(Ct);
    }

    [Fact]
    public async Task A_gap_invalidates_the_ticket_with_the_numbers_and_the_owner_gets_an_inbox_row()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 106.0, 100.0, 4.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Value.Invalidated.ShouldBe(1);
        f.StateOf(5).ShouldBe(TicketState.Invalidated);
        f.Moves[0].Reason.ShouldContain("gap_too_large");
        f.Moves[0].Reason.ShouldContain("limit 2.00");
        await f.Inbox.Received(1).InsertAsync(1, Arg.Is<IReadOnlyList<NewSuggestion>>(s => s.Count == 1 && s[0].Kind == "ticket_invalidated_preopen" && s[0].Text.Contains("No order was placed")), Arg.Any<DateTimeOffset>(), Ct);
        f.PlanState.ShouldBe(PlanState.Expired);
    }

    [Theory]
    [InlineData(null, 100.0, 4.0, "unavailable_quote")]
    [InlineData(101.0, null, 4.0, "unavailable_previous_close")]
    [InlineData(101.0, 100.0, null, "unavailable_atr")]
    public async Task A_missing_input_invalidates_and_never_arms(double? last, double? previous, double? atr, string code)
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", last, previous, atr));

        await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        f.StateOf(5).ShouldBe(TicketState.Invalidated);
        f.Moves[0].Reason.ShouldContain(code);
    }

    [Fact]
    public async Task A_symbol_the_source_knows_nothing_about_is_invalidated()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes();

        await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        f.StateOf(5).ShouldBe(TicketState.Invalidated);
    }

    [Fact]
    public async Task A_failed_price_read_invalidates_everything_and_says_so()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Source.ReadAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateOnly>(), Ct).Returns(new PreOpenSnapshot(new Dictionary<string, PreOpenReading>(), "Dhan did not answer."));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        f.StateOf(5).ShouldBe(TicketState.Invalidated);
        result.Value.Summary.ShouldContain("Dhan did not answer.");
    }

    [Fact]
    public async Task Only_approved_tickets_are_looked_at_nothing_is_approved_and_the_reserve_is_never_promoted()
    {
        var f = new PreOpenFixture();
        f.Add(1, "NSE:AAA");
        f.Add(2, "NSE:BBB", TicketState.Proposed);
        f.Add(3, "NSE:CCC", TicketState.Lapsed, TicketGroup.Reserve);
        f.Add(4, "NSE:DDD", TicketState.Proposed, TicketGroup.Reserve);
        f.Add(6, "NSE:EEE", TicketState.Rejected);
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0), ("NSE:BBB", 101.0, 100.0, 4.0), ("NSE:CCC", 101.0, 100.0, 4.0), ("NSE:DDD", 101.0, 100.0, 4.0));

        await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        f.Moves.Select(m => m.ItemId).ShouldBe([1]);
        f.StateOf(2).ShouldBe(TicketState.Proposed);
        f.StateOf(3).ShouldBe(TicketState.Lapsed);
        f.StateOf(4).ShouldBe(TicketState.Proposed);
        f.Moves.ShouldAllBe(m => m.To == TicketState.Armed || m.To == TicketState.Invalidated);
        await f.Source.Received(1).ReadAsync(Arg.Is<IReadOnlyCollection<string>>(s => s.Count == 1 && s.Contains("NSE:AAA")), Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Add(6, "NSE:BBB");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0), ("NSE:BBB", 110.0, 100.0, 4.0));
        await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);
        var moves = f.Moves.Count;
        var planMoves = f.PlanMoves.Count;

        var again = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        again.Value.Armed.ShouldBe(0);
        f.Moves.Count.ShouldBe(moves);
        f.PlanMoves.Count.ShouldBe(planMoves);
        f.StateOf(5).ShouldBe(TicketState.Armed);
        f.StateOf(6).ShouldBe(TicketState.Invalidated);
    }

    [Fact]
    public async Task More_than_the_cap_are_refused_with_the_reason_lowest_rank_last_and_the_others_stay_armed()
    {
        var f = new PreOpenFixture();
        var quotes = new List<(string, double?, double?, double?)>();
        for (var id = 1; id <= PreOpenRules.MaxArmedTickets + 2; id++)
        {
            f.Add(id, $"NSE:S{id}");
            quotes.Add(($"NSE:S{id}", 101.0, 100.0, 4.0));
        }

        f.Quotes([.. quotes]);

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Value.Armed.ShouldBe(PreOpenRules.MaxArmedTickets);
        result.Value.Invalidated.ShouldBe(2);
        f.Moves.Where(m => m.To == TicketState.Invalidated).Select(m => m.ItemId).ShouldBe([51, 52]);
        f.Moves.Where(m => m.To == TicketState.Invalidated).ShouldAllBe(m => m.Reason.Contains(PreOpenCodes.CapExceeded));
        f.Moves.Count(m => m.To == TicketState.Armed).ShouldBe(PreOpenRules.MaxArmedTickets);
    }

    [Fact]
    public async Task After_the_open_nothing_is_armed_or_read()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0));
        f.Time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 3, 45, 0, TimeSpan.Zero));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("plans.preopen_late");
        f.Moves.ShouldBeEmpty();
        await f.Source.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, Ct);
    }

    [Fact]
    public async Task A_plan_of_another_day_or_one_not_approved_is_left_alone()
    {
        var other = new PreOpenFixture(PlanState.Approved, new DateOnly(2026, 10, 27));
        other.Add(5, "NSE:AAA");
        var lapsed = new PreOpenFixture(PlanState.Lapsed);
        lapsed.Add(5, "NSE:AAA");

        await other.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);
        await lapsed.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        other.Moves.ShouldBeEmpty();
        lapsed.Moves.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_expiry_moves_armed_and_never_armed_approved_tickets_and_the_plan_after_the_close()
    {
        var f = new PreOpenFixture(PlanState.Armed);
        f.Add(1, "NSE:AAA", TicketState.Armed);
        f.Add(2, "NSE:BBB");
        f.Add(3, "NSE:CCC", TicketState.Triggered);
        f.Add(4, "NSE:DDD", TicketState.Invalidated);
        f.Time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 10, 10, 0, TimeSpan.Zero));

        var result = await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);

        result.Value.Tickets.ShouldBe(2);
        f.StateOf(1).ShouldBe(TicketState.Expired);
        f.StateOf(2).ShouldBe(TicketState.Expired);
        f.StateOf(3).ShouldBe(TicketState.Triggered);
        f.StateOf(4).ShouldBe(TicketState.Invalidated);
        f.PlanState.ShouldBe(PlanState.Expired);
        f.Moves.ShouldAllBe(m => m.Actor == "system:expiry");
        await f.Notifier.Received(1).ActiveSetChangedAsync(Ct);
    }

    [Fact]
    public async Task The_expiry_waits_for_the_close_of_the_same_day_and_a_second_run_changes_nothing()
    {
        var f = new PreOpenFixture(PlanState.Armed);
        f.Add(1, "NSE:AAA", TicketState.Armed);
        f.Time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero));

        await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);
        f.Moves.ShouldBeEmpty();

        f.Time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 10, 10, 0, TimeSpan.Zero));
        await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);
        var moves = f.Moves.Count;
        await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);

        f.StateOf(1).ShouldBe(TicketState.Expired);
        f.Moves.Count.ShouldBe(moves);
    }

    [Fact]
    public async Task The_expiry_closes_an_earlier_plan_that_was_left_open()
    {
        var f = new PreOpenFixture(PlanState.Armed, new DateOnly(2026, 10, 23));
        f.Add(1, "NSE:AAA", TicketState.Armed);

        await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);

        f.StateOf(1).ShouldBe(TicketState.Expired);
        f.PlanState.ShouldBe(PlanState.Expired);
    }
}
