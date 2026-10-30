using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Tests.Confirm;

/// <summary>
/// Spec P12-08 criteria 1, 2, 5, 9 and 13 at the issuer: a checking <c>semi_auto</c> entry whose newest full run passed every Hard check gets exactly one ticket with the hand example's text, hash, client order id, reference price, notional,
/// worst case and a 90-second life; a second hand-over returns it; every other mode, state, check or price stores nothing; a refused ticket is followed once by a new one inside the window; a fourth open ticket is stored refused.
/// </summary>
public sealed class IssueConfirmTicketTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string HandText =
        "BUY 120 TATAPOWER at LIMIT Rs 412.35 (NSE, intraday), valid today. Stop-loss Rs 405.10 and target Rs 425.80 are placed at the broker with the order (one super order). Notional Rs 49,482.00. Worst case at the stop Rs 870.00 (120 x 7.25). " +
        "Account: Dhan · Main. Plan ticket approved by you on 23 Oct at 19:41. This card expires at 09:47:30.";

    [Fact]
    public async Task A_checking_semi_auto_entry_with_a_cleared_run_gets_exactly_one_ticket_with_the_hand_example_and_a_second_hand_over_returns_it()
    {
        var scene = new TicketScene();

        var first = await scene.Issuer().IssueAsync(7, Ct);
        var second = await scene.Issuer().IssueAsync(7, Ct);

        first.IsSuccess.ShouldBeTrue();
        second.Value.Id.ShouldBe(first.Value.Id);
        var ticket = scene.Tickets.ShouldHaveSingleItem();
        ticket.Text.ShouldBe(HandText);
        ticket.TextHash.ShouldBe(ConfirmText.Hash(HandText));
        (ticket.Attempt, ticket.Outcome).ShouldBe((1, (string?)null));
        ticket.IssuedAt.ShouldBe(new DateTimeOffset(2026, 10, 27, 9, 46, 0, TicketScene.Ist));
        (ticket.ExpiresAt - ticket.IssuedAt).ShouldBe(TimeSpan.FromSeconds(90));
        var draft = scene.Drafts.ShouldHaveSingleItem();
        draft.ClientOrderId.ShouldBe(new ClientOrderId(TicketScene.Session, 7, LegKind.Entry, 1).ToString());
        draft.ClientOrderId.Length.ShouldBe(16);
        draft.RefPrice.ShouldBe(412.30m);
        (draft.Params.Notional, draft.Params.RiskAtStop).ShouldBe((49_482.00m, 870.00m));
        (draft.Password.Required, draft.Password.Reasons.Single()).ShouldBe((true, PasswordRule.NotionalOverStepUp), "capital Rs 400,000: the threshold is Rs 40,000");
        (draft.SoftCodes.Count, draft.CheckRunId, draft.RefuseAs).ShouldBe((0, 91L, (string?)null));
    }

    [Fact]
    public async Task With_capital_of_600000_no_password_is_needed_and_without_capital_it_is()
    {
        var rich = new TicketScene { Context = new TicketScene().Context! with { Capital = 600_000m } };
        var unknown = new TicketScene { Context = new TicketScene().Context! with { Capital = null } };

        await rich.Issuer().IssueAsync(7, Ct);
        await unknown.Issuer().IssueAsync(7, Ct);

        rich.Drafts.ShouldHaveSingleItem().Password.Required.ShouldBeFalse();
        unknown.Drafts.ShouldHaveSingleItem().Password.Reasons.ShouldBe([PasswordRule.CapitalUnavailable]);
    }

    [Fact]
    public async Task A_run_rejected_only_by_soft_checks_is_confirmable_with_its_codes_and_the_password_and_a_hard_non_pass_issues_nothing()
    {
        var soft = new TicketScene { Context = new TicketScene().Context! with { Capital = 600_000m, Run = new TicketRun(92, 0, ["adv_share", "fno_ban"]) } };
        var hard = new TicketScene { Context = new TicketScene().Context! with { Run = new TicketRun(93, 1, ["adv_share"]) } };
        var none = new TicketScene { Context = new TicketScene().Context! with { Run = null } };

        (await soft.Issuer().IssueAsync(7, Ct)).IsSuccess.ShouldBeTrue();
        (await hard.Issuer().IssueAsync(7, Ct)).Error.Code.ShouldBe(IssueRefusals.CheckRejected);
        (await none.Issuer().IssueAsync(7, Ct)).Error.Code.ShouldBe(IssueRefusals.CheckMissing);

        var draft = soft.Drafts.ShouldHaveSingleItem();
        draft.SoftCodes.ShouldBe(["adv_share", "fno_ban"]);
        (draft.Password.Required, draft.Password.Reasons.Single()).ShouldBe((true, PasswordRule.SoftChecks));
        (hard.Tickets.Count, none.Tickets.Count).ShouldBe((0, 0));
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "paper", ConfirmCodes.ModeChanged },
        { "manual_live", ConfirmCodes.ModeChanged },
        { "assisted", ConfirmCodes.ModeChanged },
        { "switch_off", ConfirmCodes.ModeChanged },
        { "no_answer", ConfirmCodes.ModeChanged },
        { "auto_live", IssueRefusals.NoTapNeeded },
        { "armed", IssueRefusals.NotChecking },
        { "sell", "execution.not_supported" },
        { "stale_price", ConfirmCodes.PriceUnavailable },
        { "no_price", ConfirmCodes.PriceUnavailable },
        { "after_window", IssueRefusals.Window },
        { "no_settings", IssueRefusals.SettingsUnavailable },
        { "no_approval", IssueRefusals.ApprovalMissing },
        { "no_context", IssueRefusals.ContextUnavailable },
        { "canary_120", IssueRefusals.CanaryQuantity },
        { "long_text", IssueRefusals.TextTooLong },
        { "environment_none", ConfirmCodes.ModeChanged },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Anything_but_a_fresh_checking_semi_auto_buy_entry_inside_the_window_stores_no_ticket(string change, string code)
    {
        var scene = new TicketScene();
        var environment = ExecEnvironment.Sandbox;
        switch (change)
        {
            case "paper": scene.Modes[TicketScene.Account] = new ModeAnswer(ExecMode.Paper, true); break;
            case "manual_live": scene.Modes[TicketScene.Account] = new ModeAnswer(ExecMode.ManualLive, true); break;
            case "assisted": scene.Modes[TicketScene.Account] = new ModeAnswer(ExecMode.Assisted, true); break;
            case "switch_off": scene.Modes[TicketScene.Account] = new ModeAnswer(ExecMode.SemiAuto, false); break;
            case "no_answer": scene.Modes[TicketScene.Account] = null; break;
            case "auto_live": scene.Modes[TicketScene.Account] = new ModeAnswer(ExecMode.AutoLive, true); break;
            case "armed": scene.Intent = scene.Intent! with { State = ExecState.Armed }; break;
            case "sell": scene.Intent = scene.Intent! with { Side = ExecSide.Sell }; break;
            case "stale_price": scene.Price = new LivePrice(412.30m, scene.Clock.GetUtcNow().AddSeconds(-6)); break;
            case "no_price": scene.Price = null; break;
            case "after_window": scene.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 15, 0, 1, TicketScene.Ist)); scene.PriceNow(412.30m); break;
            case "no_settings": scene.Confirm = null; break;
            case "no_approval": scene.Context = scene.Context! with { ApprovedAt = null }; break;
            case "no_context": scene.Context = null; break;
            case "canary_120": scene.Window = scene.Window! with { CanaryPending = true }; break;
            case "long_text": scene.Context = scene.Context! with { AccountName = new string('m', 2000) }; break;
            default: environment = ExecEnvironment.None; break;
        }

        var result = await scene.Issuer(environment).IssueAsync(7, Ct);

        result.Error.Code.ShouldBe(code);
        scene.Tickets.ShouldBeEmpty();
    }

    [Fact]
    public async Task While_the_canary_is_pending_a_one_share_intent_gets_a_ticket_that_says_so()
    {
        var scene = new TicketScene { Window = new TicketScene().Window! with { CanaryPending = true } };
        scene.Intent = scene.Intent! with { Quantity = 1 };

        var ticket = (await scene.Issuer().IssueAsync(7, Ct)).Value;

        ticket.Text.ShouldStartWith("BUY 1 TATAPOWER at LIMIT Rs 412.35 (NSE, intraday, canary: 1 share), valid today.");
    }

    [Fact]
    public async Task A_refused_ticket_is_followed_once_by_a_new_ticket_with_new_text_and_never_a_third()
    {
        var scene = new TicketScene();
        var first = (await scene.Issuer().IssueAsync(7, Ct)).Value;
        scene.Decide(first.Id, TicketOutcomes.Refused, "price_moved");
        scene.Clock.Advance(TimeSpan.FromSeconds(20));
        scene.PriceNow(413.90m);

        var second = (await scene.Issuer().IssueAsync(7, Ct)).Value;
        scene.Decide(second.Id, TicketOutcomes.Refused, "price_moved");
        var third = (await scene.Issuer().IssueAsync(7, Ct)).Value;

        second.Attempt.ShouldBe(2);
        second.Text.ShouldNotBe(first.Text);
        scene.Drafts[1].RefPrice.ShouldBe(413.90m);
        third.Id.ShouldBe(second.Id);
        scene.Tickets.Count.ShouldBe(2);
    }

    [Fact]
    public async Task No_new_ticket_follows_a_refusal_once_the_window_closed_or_after_a_skip_or_an_expiry()
    {
        var late = new TicketScene();
        var refused = (await late.Issuer().IssueAsync(7, Ct)).Value;
        late.Decide(refused.Id, TicketOutcomes.Refused, "price_moved");
        late.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 27, 15, 0, 1, TicketScene.Ist));
        late.PriceNow(412.30m);

        (await late.Issuer().IssueAsync(7, Ct)).Value.Id.ShouldBe(refused.Id);

        foreach (var outcome in new[] { TicketOutcomes.Skipped, TicketOutcomes.Expired, TicketOutcomes.Confirmed })
        {
            var scene = new TicketScene();
            var ticket = (await scene.Issuer().IssueAsync(7, Ct)).Value;
            scene.Decide(ticket.Id, outcome, "x");
            (await scene.Issuer().IssueAsync(7, Ct)).Value.Id.ShouldBe(ticket.Id, outcome);
            scene.Tickets.Count.ShouldBe(1, outcome);
        }

        late.Tickets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_fourth_open_ticket_of_an_account_is_stored_closed_too_many_open_and_never_reaches_the_owner()
    {
        var scene = new TicketScene();
        scene.OtherOpenTicket();
        scene.OtherOpenTicket();
        scene.OtherOpenTicket();

        var ticket = (await scene.Issuer().IssueAsync(7, Ct)).Value;

        (ticket.Outcome, ticket.OutcomeReason).ShouldBe((TicketOutcomes.Refused, "too_many_open"));
        scene.Drafts.ShouldHaveSingleItem().RefuseAs.ShouldBe("too_many_open");
    }

    [Fact]
    public async Task A_refusal_of_the_database_stores_nothing_and_is_reported()
    {
        var scene = new TicketScene { RefuseNextInsert = "execution.ticket_refused: a ticket is issued only for an intent in checking" };

        (await scene.Issuer().IssueAsync(7, Ct)).Error.Code.ShouldBe(IssueRefusals.StoreRefused);
        scene.Tickets.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unknown_intent_is_not_found()
    {
        (await new TicketScene().Issuer().IssueAsync(8, Ct)).Error.Code.ShouldBe(IssueRefusals.IntentUnknown);
    }
}
