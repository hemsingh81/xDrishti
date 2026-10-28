using NSubstitute;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.LiveTrading;
using XDrishti.Application.Planning;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-01 at the use-case level on fakes: the poll (criteria 3, 13), the settle of the fixture day (4, 5, 8, 9), the capture view (10) and the two services (registered paused).</summary>
public sealed class LiveCaptureHandlersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_same_trade_seen_by_ten_polls_is_one_row_and_a_second_run_writes_nothing_new()
    {
        var f = new LiveCaptureFixture();

        for (var i = 0; i < 10; i++)
        {
            await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        }

        f.Store.Fills.Count.ShouldBe(4);
        f.Store.Orders.Count.ShouldBe(3);
        f.Store.Runs.Count.ShouldBe(10);
        f.Store.Runs.ShouldAllBe(r => r.Outcome == OutcomeCodes.RunOk);
        f.Store.Runs.Skip(1).ShouldAllBe(r => r.FillsNew == 0 && r.OrdersNew == 0);
    }

    [Fact]
    public async Task An_order_whose_status_moves_has_three_revisions_and_the_newest_is_read()
    {
        var f = new LiveCaptureFixture();
        foreach (var (status, filled) in new[] { ("pending", 0L), ("part_traded", 20L), ("traded", 40L) })
        {
            f.Orders.ReadAsync(Arg.Any<XDrishti.Domain.Accounts.SecretValue>(), Ct).Returns(BrokerResponse.Ok(LiveCaptureFixture.OrderBook(status, filled)));
            await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        }

        f.Store.Orders.Count(o => o.BrokerOrderId == "o1").ShouldBe(3);
        (await f.Store.LatestOrdersAsync(f.Account.Id, LiveCaptureFixture.Day, Ct)).Single(o => o.BrokerOrderId == "o1").Status.ShouldBe("traded");
    }

    [Theory]
    [InlineData(2026, 11, 3, 16, 0)] // after 15:40
    [InlineData(2026, 11, 3, 9, 9)] // before 09:10
    [InlineData(2026, 11, 7, 11, 0)] // a Saturday
    public async Task No_poll_runs_outside_the_window_or_on_a_non_trading_day(int year, int month, int day, int hour, int minute)
    {
        var f = new LiveCaptureFixture(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromMinutes(330)));

        var result = await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);

        result.Value.Accounts.ShouldBe(0);
        await f.Trades.DidNotReceiveWithAnyArgs().ReadAsync(default!, Ct);
        f.Store.Runs.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_owner_may_poll_by_hand_outside_the_window()
    {
        var f = new LiveCaptureFixture(new DateTimeOffset(2026, 11, 3, 16, 0, 0, TimeSpan.FromMinutes(330)));

        (await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, true), Ct)).Value.Accounts.ShouldBe(1);
    }

    [Theory]
    [InlineData(BrokerFailureKind.Unauthorized, OutcomeCodes.RunTokenExpired)]
    [InlineData(BrokerFailureKind.RateLimited, OutcomeCodes.RunRateLimited)]
    [InlineData(BrokerFailureKind.Unavailable, OutcomeCodes.RunUnavailable)]
    public async Task A_failed_read_is_recorded_with_its_reason_and_stores_nothing_never_an_empty_day(BrokerFailureKind kind, string outcome)
    {
        var f = new LiveCaptureFixture();
        f.Trades.ReadAsync(Arg.Any<XDrishti.Domain.Accounts.SecretValue>(), Ct).Returns(BrokerResponse.Fail<LiveBookRead<LiveBrokerFill>>(kind));

        var result = await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);

        f.Store.Runs.Single().Outcome.ShouldBe(outcome);
        f.Store.Fills.ShouldBeEmpty();
        result.Value.Failed.ShouldBe(1);
    }

    [Fact]
    public async Task An_expired_token_is_token_expired_without_any_call_to_the_book()
    {
        var f = new LiveCaptureFixture();
        f.Access.Reason = BrokerFailureKind.Unauthorized;

        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);

        f.Store.Runs.Single().Outcome.ShouldBe(OutcomeCodes.RunTokenExpired);
        await f.Trades.DidNotReceiveWithAnyArgs().ReadAsync(default!, Ct);
    }

    [Fact]
    public async Task With_the_order_book_switch_off_the_order_book_is_never_read_and_the_run_is_partial()
    {
        var f = new LiveCaptureFixture { OrderBookOn = false };

        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);

        f.Store.Runs.Single().Outcome.ShouldBe(OutcomeCodes.RunPartial);
        f.Store.Orders.ShouldBeEmpty();
        await f.Orders.DidNotReceiveWithAnyArgs().ReadAsync(default!, Ct);
    }

    [Fact]
    public async Task The_settle_gives_the_fixture_day_its_four_outcomes_and_a_second_settle_writes_nothing()
    {
        var f = new LiveCaptureFixture();
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);

        var first = await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);
        var second = await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        first.Value.OutcomesWritten.ShouldBe(4);
        second.Value.OutcomesWritten.ShouldBe(0);
        var byItem = f.Store.Outcomes.ToDictionary(o => o.ItemId, o => o.Outcome);
        byItem[1].ShouldSatisfyAllConditions(
            o => o.Outcome.ShouldBe(OutcomeCodes.Filled),
            o => o.EntryQty.ShouldBe(40),
            o => o.Flags.ShouldBeEmpty(),
            o => o.Protection.ShouldBe(OutcomeCodes.Protected));
        byItem[2].Outcome.ShouldBe(OutcomeCodes.Rejected);
        byItem[2].RejectText.ShouldBe("insufficient margin");
        byItem[3].Outcome.ShouldBe(OutcomeCodes.MissedNoOrder);
        byItem[3].MissedR.ShouldBe(1.20);
        byItem[4].Outcome.ShouldBe(OutcomeCodes.NotTriggered);
        byItem[4].MissedR.ShouldBeNull();
    }

    [Fact]
    public async Task The_sbin_fill_is_discretionary_and_the_sale_is_an_exit_in_the_view()
    {
        var f = new LiveCaptureFixture();
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        var view = (await f.Get().HandleAsync(new LiveCaptureQuery(f.Account.Id, LiveCaptureFixture.Day), Ct)).Value;

        view.Discretionary.Single().Symbol.ShouldBe("SBIN");
        view.Unmatched.ShouldBeEmpty();
        view.Tickets.Single(t => t.ItemId == 1).ShouldSatisfyAllConditions(t => t.FilledQty.ShouldBe(40), t => t.AvgEntry.ShouldBe(1250.475m), t => t.OrderedQty.ShouldBe(40));
        view.Evidence.OrderEvidence.ShouldBe(OutcomeCodes.EvidencePresent);
        view.Evidence.Limits.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Without_order_evidence_every_order_dependent_field_is_unavailable_and_a_missed_ticket_is_missed_unknown()
    {
        var f = new LiveCaptureFixture { OrderBookOn = false };
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        var byItem = f.Store.Outcomes.ToDictionary(o => o.ItemId, o => o.Outcome);

        byItem[3].Outcome.ShouldBe(OutcomeCodes.MissedUnknown);
        byItem[2].Outcome.ShouldBe(OutcomeCodes.MissedUnknown);
        byItem[4].Outcome.ShouldBe(OutcomeCodes.NotTriggered);
        byItem[1].ShouldSatisfyAllConditions(o => o.OrderEvidence.ShouldBe(OutcomeCodes.EvidenceUnavailable), o => o.Protection.ShouldBe(OutcomeCodes.Unavailable), o => o.OrderedQty.ShouldBeNull());
        var view = (await f.Get().HandleAsync(new LiveCaptureQuery(f.Account.Id, LiveCaptureFixture.Day), Ct)).Value;
        view.Tickets.ShouldAllBe(t => t.OrderEvidence == OutcomeCodes.EvidenceUnavailable);
    }

    [Fact]
    public async Task A_pending_plan_grade_writes_nothing_and_after_five_sessions_the_ticket_is_unknown()
    {
        var f = new LiveCaptureFixture();
        f.StoredGrades.Clear();
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);

        var today = await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);
        f.Time.SetUtcNow(new DateTimeOffset(2026, 11, 10, 18, 0, 0, TimeSpan.FromMinutes(330))); // five trading sessions later
        var later = await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        today.Value.Pending.ShouldBe(3); // T1 has a fill and is written; T2, T3 and T4 wait
        later.Value.OutcomesWritten.ShouldBe(3);
        f.Store.Outcomes.Where(o => o.ItemId != 1).ShouldAllBe(o => o.Outcome.Outcome == OutcomeCodes.Unknown);
    }

    [Fact]
    public async Task A_settle_of_a_future_date_is_refused_and_a_finished_session_is_graded_first()
    {
        var f = new LiveCaptureFixture();

        (await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day.AddDays(1), null), Ct)).IsFailure.ShouldBeTrue();
        f.Time.SetUtcNow(new DateTimeOffset(2026, 11, 4, 18, 0, 0, TimeSpan.FromMinutes(330)));
        await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        await f.Grading.Received().HandleAsync(Arg.Is<GradePlansCommand>(c => c.Date == LiveCaptureFixture.Day), Ct);
    }

    [Fact]
    public async Task The_charges_of_a_delivery_fill_are_stored_from_the_history_read_and_the_agreement_is_reported()
    {
        var f = new LiveCaptureFixture();
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        var trade = new BrokerTrade("T-1", "o1", XDrishti.Domain.Portfolio.TradeSide.Buy, "RELIANCE", null, null, 25, 1250.40m, 12.5m, LiveCaptureFixture.Day, null);
        f.History.FetchPageAsync(Arg.Any<XDrishti.Domain.Accounts.SecretValue>(), LiveCaptureFixture.Day, LiveCaptureFixture.Day, 0, Ct).Returns(BrokerResponse.Ok(new BrokerTradePage([trade], 1, 0, new Dictionary<string, int>())));
        f.History.FetchPageAsync(Arg.Any<XDrishti.Domain.Accounts.SecretValue>(), LiveCaptureFixture.Day, LiveCaptureFixture.Day, 1, Ct).Returns(BrokerResponse.Ok(BrokerTradePage.Empty));

        var result = await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        result.Value.ChargesWritten.ShouldBe(1);
        f.Store.Charges.Values.ShouldBe([12.5m]);
        result.Value.Summary.ShouldContain("history misses 3 of 4");
    }

    [Fact]
    public async Task A_failed_history_read_is_a_partial_settle_and_never_stops_the_outcomes()
    {
        var f = new LiveCaptureFixture();
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        f.History.FetchPageAsync(Arg.Any<XDrishti.Domain.Accounts.SecretValue>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Ct).Returns(BrokerResponse.Fail<BrokerTradePage>(BrokerFailureKind.RateLimited));

        var result = await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        result.Value.OutcomesWritten.ShouldBe(4);
        f.Store.Runs.Last(r => r.Kind == OutcomeCodes.KindSettle).Outcome.ShouldBe(OutcomeCodes.RunPartial);
        f.Store.Charges.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_view_of_a_day_with_no_run_is_not_run_and_a_future_date_or_a_paper_account_is_refused()
    {
        var f = new LiveCaptureFixture();

        (await f.Get().HandleAsync(new LiveCaptureQuery(f.Account.Id, LiveCaptureFixture.Day), Ct)).Value.Health.ShouldBe(CaptureHealth.NotRun);
        (await f.Get().HandleAsync(new LiveCaptureQuery(f.Account.Id, LiveCaptureFixture.Day.AddDays(1)), Ct)).Error.Code.ShouldBe(LiveErrors.Invalid);
        (await f.Get().HandleAsync(new LiveCaptureQuery(Guid.NewGuid(), LiveCaptureFixture.Day), Ct)).Error.Code.ShouldBe(LiveErrors.NotFound);
    }

    [Theory]
    [InlineData(0, 50, 30)]
    [InlineData(1, 201, 30)]
    [InlineData(1, 0, 30)]
    [InlineData(1, 50, 93)]
    public async Task The_outcomes_list_refuses_a_bad_page_size_or_range(int page, int size, int days)
    {
        var f = new LiveCaptureFixture();
        var today = new DateOnly(2026, 11, 3);

        var result = await new ListLiveOutcomesHandler(f.Store, f.Time).HandleAsync(new LiveOutcomesQuery(null, today.AddDays(-days), today, page, size), Ct);

        result.Error.Code.ShouldBe(LiveErrors.Invalid);
    }

    [Fact]
    public async Task The_outcomes_list_pages_newest_session_first()
    {
        var f = new LiveCaptureFixture();
        await f.Capture().HandleAsync(new CaptureLiveSessionCommand(null, false), Ct);
        await f.Settle().HandleAsync(new SettleLiveSessionCommand(LiveCaptureFixture.Day, null), Ct);

        var page = (await new ListLiveOutcomesHandler(f.Store, f.Time).HandleAsync(new LiveOutcomesQuery(f.Account.Id, null, null, 1, 3), Ct)).Value;

        page.Total.ShouldBe(4);
        page.Items.Count.ShouldBe(3);
    }

    [Fact]
    public void Both_services_are_registered_paused_with_their_ids_and_read_only_wording()
    {
        LiveCaptureService.Definition.Defaults.Enabled.ShouldBeFalse();
        LiveSettleService.Definition.Defaults.Enabled.ShouldBeFalse();
        LiveCaptureService.Id.ShouldBe("live-capture");
        LiveSettleService.Id.ShouldBe("live-settle");
        LiveReadinessSource.CaptureId.ShouldBe(LiveCaptureService.Id);
        LiveReadinessSource.SettleId.ShouldBe(LiveSettleService.Id);
    }

    [Fact]
    public void The_poll_interval_comes_from_the_setting_in_whole_minutes()
    {
        var f = new LiveCaptureFixture();
        var service = new LiveCaptureService(null!, new FixedSettings(150));

        service.FirstStartSettings()!.Schedule.EveryMinutes.ShouldBe(3);
        service.FirstStartSettings()!.Enabled.ShouldBeFalse();
        f.OrderBookOn.ShouldBeTrue();
    }

    private sealed class FixedSettings(int seconds) : ILiveCaptureSettings
    {
        public bool Enabled => false;

        public bool OrderBookEnabled => false;

        public int PollSeconds => seconds;

        public MatchTolerances Tolerances => MatchTolerances.Default;

        public TimeSpan ProtectWithin => TimeSpan.FromSeconds(300);
    }
}
