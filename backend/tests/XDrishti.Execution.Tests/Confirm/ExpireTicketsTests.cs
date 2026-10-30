using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Tests.Halts;

namespace XDrishti.Execution.Tests.Confirm;

/// <summary>
/// Spec P12-08 criteria 5 and 11 at the closer: an unanswered ticket is closed <c>expired</c> by <c>system:confirm-expiry</c> once its expiry has passed (the loop runs every 2 seconds, inside the 5 the spec allows), a decided one is
/// never touched; the API's <c>ticket_refuse</c> command closes a ticket <c>refused</c> only for a price or Soft-set refusal from the owner.
/// </summary>
public sealed class ExpireTicketsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_unanswered_ticket_is_closed_expired_by_the_system_at_its_expiry_and_not_before()
    {
        var scene = new TicketScene();
        var ticket = (await scene.Issuer().IssueAsync(7, Ct)).Value;
        var closer = new TicketCloser(scene);

        scene.Clock.SetUtcNow(ticket.ExpiresAt.AddMilliseconds(-1));
        (await closer.ExpireDueAsync(Ct)).ShouldBe(0);
        scene.Clock.SetUtcNow(ticket.ExpiresAt);
        (await closer.ExpireDueAsync(Ct)).ShouldBe(1);
        (await closer.ExpireDueAsync(Ct)).ShouldBe(0);

        var closed = scene.Tickets.ShouldHaveSingleItem();
        (closed.Outcome, closed.OutcomeReason).ShouldBe((TicketOutcomes.Expired, TicketOutcomes.TtlReason));
        scene.Expired.ShouldBe([TicketOutcomes.ExpiryActor]);
        TicketCloser.Interval.ShouldBeLessThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task A_confirmed_or_skipped_ticket_is_never_expired()
    {
        var scene = new TicketScene();
        var ticket = (await scene.Issuer().IssueAsync(7, Ct)).Value;
        scene.Decide(ticket.Id, TicketOutcomes.Confirmed, TicketOutcomes.TapReason);
        scene.Clock.Advance(TimeSpan.FromMinutes(5));

        (await new TicketCloser(scene).ExpireDueAsync(Ct)).ShouldBe(0);

        scene.Tickets.ShouldHaveSingleItem().Outcome.ShouldBe(TicketOutcomes.Confirmed);
    }

    private static ExecCommand Refuse(Guid ticket, string code, string actor = "owner:hem@example.test") =>
        new(Guid.NewGuid(), ExecCommandProcessor.TicketRefuse, TicketScene.Account, $$"""{"ticketId":"{{ticket}}","code":"{{code}}"}""", actor, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task The_owners_refusal_command_closes_the_ticket_refused_once_and_only_for_a_price_or_soft_set_refusal()
    {
        var scene = new TicketScene();
        var ticket = (await scene.Issuer().IssueAsync(7, Ct)).Value;
        var commands = new MemoryCommands();
        var processor = new ExecCommandProcessor(commands, tickets: new TicketCloser(scene));
        commands.Add(Refuse(ticket.Id, ConfirmCodes.ModeChanged));
        commands.Add(Refuse(ticket.Id, ConfirmCodes.PriceMoved, actor: "system:x"));
        commands.Add(Refuse(Guid.NewGuid(), ConfirmCodes.PriceMoved));
        commands.Add(new ExecCommand(Guid.NewGuid(), ExecCommandProcessor.TicketRefuse, null, "not json", "owner:hem@example.test", DateTimeOffset.UnixEpoch));
        commands.Add(Refuse(ticket.Id, ConfirmCodes.PriceMoved));
        commands.Add(Refuse(ticket.Id, ConfirmCodes.SoftSetChanged));

        await processor.RunOnceAsync(Ct);

        commands.Answers.ShouldBe(
        [
            ("refused", ExecCommandProcessor.TicketRefuseInvalidCode),
            ("refused", ExecCommandProcessor.TicketRefuseInvalidCode),
            ("refused", ExecCommandProcessor.TicketRefuseInvalidCode),
            ("refused", ExecCommandProcessor.TicketRefuseInvalidCode),
            ("done", "execution.ticket_refused"),
            ("done", "execution.ticket_decided"),
        ]);
        var closed = scene.Tickets.ShouldHaveSingleItem();
        (closed.Outcome, closed.OutcomeReason).ShouldBe((TicketOutcomes.Refused, "price_moved"));
    }

    [Fact]
    public async Task Without_a_closer_the_refusal_command_is_an_unknown_kind()
    {
        var commands = new MemoryCommands();
        commands.Add(Refuse(Guid.NewGuid(), ConfirmCodes.PriceMoved));

        await new ExecCommandProcessor(commands).RunOnceAsync(Ct);

        commands.Answers.ShouldBe([("refused", ExecCommandProcessor.UnknownCode)]);
    }
}
