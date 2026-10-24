#pragma warning disable CA2012 // NSubstitute setup calls on ValueTask members are not awaited
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

    [Fact]
    public async Task A_run_before_09_08_reads_nothing_and_arms_nothing()
    {
        var f = new PreOpenFixture(at: new DateTimeOffset(2026, 10, 26, 3, 37, 59, TimeSpan.Zero));
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Error.Code.ShouldBe("plans.preopen_early");
        f.Moves.ShouldBeEmpty();
        await f.Source.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, Ct);
    }

    [Fact]
    public async Task A_run_at_09_08_is_allowed()
    {
        var f = new PreOpenFixture(at: new DateTimeOffset(2026, 10, 26, 3, 38, 0, TimeSpan.Zero));
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Value.Armed.ShouldBe(1);
    }

    [Fact]
    public async Task A_batch_where_almost_every_price_equals_the_previous_close_is_stale_and_invalidates_those_tickets()
    {
        var f = new PreOpenFixture();
        f.Add(1, "NSE:AAA");
        f.Add(2, "NSE:BBB");
        f.Add(3, "NSE:CCC");
        f.Quotes(("NSE:AAA", 100.0, 100.0, 4.0), ("NSE:BBB", 50.0, 50.0, 2.0), ("NSE:CCC", 20.0, 20.0, 1.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Value.Armed.ShouldBe(0);
        result.Value.Invalidated.ShouldBe(3);
        f.Moves.ShouldAllBe(m => m.To == TicketState.Invalidated && m.Reason.Contains(PreOpenCodes.StaleQuote));
        result.Value.Summary.ShouldContain("Price equals previous close for 3 of 3: probably no pre-open price yet.");
    }

    [Fact]
    public async Task One_zero_gap_ticket_in_a_batch_that_is_not_stale_still_arms()
    {
        var f = new PreOpenFixture();
        f.Add(1, "NSE:AAA");
        f.Add(2, "NSE:BBB");
        f.Add(3, "NSE:CCC");
        f.Add(4, "NSE:DDD");
        f.Quotes(("NSE:AAA", 100.0, 100.0, 4.0), ("NSE:BBB", 50.5, 50.0, 2.0), ("NSE:CCC", 20.2, 20.0, 1.0), ("NSE:DDD", 30.1, 30.0, 1.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.Value.Armed.ShouldBe(4);
        f.StateOf(1).ShouldBe(TicketState.Armed);
    }

    [Fact]
    public async Task A_price_from_yesterday_or_with_no_trade_time_invalidates()
    {
        var yesterday = new PreOpenFixture();
        yesterday.Add(5, "NSE:AAA");
        yesterday.QuotesAt(new DateTimeOffset(2026, 10, 23, 15, 29, 0, TimeSpan.FromMinutes(330)), ("NSE:AAA", 101.0, 100.0, 4.0));
        var untimed = new PreOpenFixture();
        untimed.Add(5, "NSE:AAA");
        untimed.QuotesAt(null, ("NSE:AAA", 101.0, 100.0, 4.0));

        await yesterday.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);
        await untimed.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        yesterday.StateOf(5).ShouldBe(TicketState.Invalidated);
        yesterday.Moves[0].Reason.ShouldContain(PreOpenCodes.UnavailableQuote);
        untimed.StateOf(5).ShouldBe(TicketState.Invalidated);
    }

    [Fact]
    public async Task The_arming_reason_and_the_summary_carry_the_caveats_and_the_no_alerts_sentence()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        var reason = f.Moves.Single().Reason;
        reason.ShouldStartWith(PlanWording.Armed);
        reason.ShouldContain("Last price may be the previous close before the open.");
        reason.ShouldContain("Ban list, surveillance and price band were not re-checked.");
        reason.ShouldContain("Price alerts on trigger and stop are not built yet; only the live price is shown.");
        reason.Length.ShouldBeLessThan(500);
        result.Value.Summary.ShouldContain(PlanWording.PreOpenPriceCaveat);
        result.Value.Summary.ShouldContain(PlanWording.AlertsNotBuilt);
    }

    [Fact]
    public async Task A_failed_inbox_write_does_not_stop_the_other_tickets_or_fail_the_run()
    {
        var f = new PreOpenFixture();
        f.Add(1, "NSE:AAA");
        f.Add(2, "NSE:BBB");
        f.Add(3, "NSE:CCC");
        f.Quotes(("NSE:AAA", 110.0, 100.0, 4.0), ("NSE:BBB", 120.0, 100.0, 4.0), ("NSE:CCC", 100.5, 100.0, 4.0));
        f.Inbox.InsertAsync(Arg.Any<long>(), Arg.Any<IReadOnlyList<NewSuggestion>>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns<ValueTask<int>>(_ => throw new InvalidOperationException("db"));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.IsSuccess.ShouldBeTrue();
        f.StateOf(1).ShouldBe(TicketState.Invalidated);
        f.StateOf(2).ShouldBe(TicketState.Invalidated);
        f.StateOf(3).ShouldBe(TicketState.Armed);
        f.PlanState.ShouldBe(PlanState.Armed);
    }

    [Fact]
    public async Task A_failed_notify_does_not_fail_the_run()
    {
        var f = new PreOpenFixture();
        f.Add(5, "NSE:AAA");
        f.Quotes(("NSE:AAA", 101.0, 100.0, 4.0));
        f.Notifier.ActiveSetChangedAsync(Arg.Any<CancellationToken>()).Returns<ValueTask>(_ => throw new InvalidOperationException("db"));

        var result = await f.PreOpen().HandleAsync(new RunPreOpenCheckCommand(), Ct);

        result.IsSuccess.ShouldBeTrue();
        f.StateOf(5).ShouldBe(TicketState.Armed);
    }

    [Fact]
    public async Task When_one_ticket_fails_to_expire_the_plan_stays_open_and_the_next_run_finishes_it()
    {
        var f = new PreOpenFixture(PlanState.Armed);
        f.Add(1, "NSE:AAA", TicketState.Armed);
        f.Add(2, "NSE:BBB", TicketState.Armed);
        f.Time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 10, 10, 0, TimeSpan.Zero));
        f.Tickets.HandleAsync(Arg.Is<MoveTicketCommand>(m => m.ItemId == 2), Arg.Any<CancellationToken>())
            .Returns(Domain.Common.Result.Failure<TicketMoveOutcome>(Domain.Common.Error.Conflict("plans.stale", "another move won")));

        var first = await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);

        first.IsFailure.ShouldBeTrue();
        f.StateOf(1).ShouldBe(TicketState.Expired);
        f.StateOf(2).ShouldBe(TicketState.Armed);
        f.PlanState.ShouldBe(PlanState.Armed);
        f.PlanMoves.ShouldBeEmpty();

        f.Tickets.HandleAsync(Arg.Is<MoveTicketCommand>(m => m.ItemId == 2), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var move = call.Arg<MoveTicketCommand>();
                f.Moves.Add(move);
                f.SetState(move.ItemId, move.To);
                return Domain.Common.Result.Success(new TicketMoveOutcome(move.ItemId, move.To.Code()));
            });
        var second = await f.Expiry().HandleAsync(new ExpireTicketsCommand(), Ct);

        second.IsSuccess.ShouldBeTrue();
        second.Value.Plans.ShouldBe(1);
        f.StateOf(2).ShouldBe(TicketState.Expired);
        f.PlanState.ShouldBe(PlanState.Expired);
    }
}
