using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.LiveTrading;
using XDrishti.Application.Tests.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-06 at the use-case level, on in-memory ports: the refusals, the empty window, an unreadable paper record and that nothing is written. Today is 2026-11-03 at noon, so the last closed session is 2026-11-02.</summary>
public sealed class EvaluateLivePhaseCheckHandlerTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromMinutes(330));
    private static readonly DateTimeOffset StepSet = new(2026, 10, 1, 20, 0, 0, TimeSpan.FromMinutes(330));

    private readonly AccountsFixture _fx = new();
    private readonly InMemoryRampStore _ramp;
    private readonly InMemoryLiveStore _capture = new();
    private readonly InMemoryExecutionStore _executions = new();
    private readonly InMemoryHaltStore _halts = new();
    private readonly IComplianceStore _compliance = Substitute.For<IComplianceStore>();
    private readonly IQueryHandler<LiveVsPaperQuery, Result<LiveVsPaperView>> _vsPaper = Substitute.For<IQueryHandler<LiveVsPaperQuery, Result<LiveVsPaperView>>>();

    public EvaluateLivePhaseCheckHandlerTests()
    {
        _fx.Clock.SetUtcNow(Noon);
        _ramp = new InMemoryRampStore(_fx.Clock);
        _compliance.LatestPerKindAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<StoredCompliance>>([]);
        _vsPaper.HandleAsync(Arg.Any<LiveVsPaperQuery>(), Arg.Any<CancellationToken>()).Returns(Result.Failure<LiveVsPaperView>(Error.Validation("live.paper_account_required", "several paper accounts")));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private EvaluateLivePhaseCheckHandler Handler()
    {
        var calendar = Substitute.For<ITradingCalendar>();
        calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        var plans = Substitute.For<IPlanStore>();
        plans.ListAsync(Arg.Any<DateOnly?>(), Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns<IReadOnlyList<PlanRow>>([]);
        var allow = Substitute.For<IBrokerAllowList>();
        allow.Entries().Returns(XDrishti.Domain.Planning.PlanPhaseCheck.PinnedAllowList);
        var schedules = Substitute.For<IServiceScheduleReader>();
        var capture = Substitute.For<ILiveCaptureSettings>();
        var settings = Substitute.For<ILiveSettingsStore>();
        settings.CurrentAsync(Arg.Any<CancellationToken>()).Returns(LiveSettings.Defaults);
        var rampSettings = Substitute.For<ILiveRampSettings>();
        rampSettings.StepFactors.Returns(RampPolicy.DefaultFactors);
        return new EvaluateLivePhaseCheckHandler(_fx.Data, _ramp, _capture, _executions, _halts, _compliance, new LiveTicketSource(plans), calendar, _vsPaper, allow, schedules, capture, settings, rampSettings, _fx.Clock, NullLogger<EvaluateLivePhaseCheckHandler>.Instance);
    }

    private async Task<Domain.Accounts.BrokerAccount> LiveAccountAsync(bool step = true, string name = "Example One", string? clientId = AccountsFixture.ClientId)
    {
        var account = await _fx.CreatedAsync(Inputs.Account(name, clientId, trading: Inputs.Trading(_fx.Standard.Id, mode: "manual_live", amount: 100_000m), roles: Inputs.Roles(true, false)));
        if (step)
        {
            _ramp.Seed(account.Id, RampEventKinds.SetStep, null, 1, false, StepSet);
        }

        return account;
    }

    private void Fill(Guid account, DateOnly date) =>
        _capture.Fills.Add(new LiveFill(Guid.NewGuid(), account, "T" + date.DayNumber, "O1", "ABC", Side.Buy, "INTRADAY", 10, 100m, date, new TimeOnly(10, 0)));

    [Fact]
    public async Task No_live_account_is_not_found()
    {
        var result = await Handler().HandleAsync(new LivePhaseCheckQuery(null, null), Ct);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LiveErrors.NotFound);
    }

    [Fact]
    public async Task Several_live_accounts_without_an_id_are_refused_and_the_exit_report_is_not_met()
    {
        await LiveAccountAsync();
        await LiveAccountAsync(true, "Second Live", "1000000099");
        var handler = Handler();
        var result = await handler.HandleAsync(new LivePhaseCheckQuery(null, null), Ct);
        result.Error.Code.ShouldBe(LiveErrors.Invalid);
        (await new PhaseCheckExitReport(handler).CheckAsync(Ct)).Met.ShouldBeFalse();
    }

    [Fact]
    public async Task A_date_after_the_last_closed_session_is_refused()
    {
        await LiveAccountAsync();
        var result = await Handler().HandleAsync(new LivePhaseCheckQuery(null, new DateOnly(2026, 11, 10)), Ct);
        result.Error.Code.ShouldBe(LiveErrors.Invalid);
    }

    [Fact]
    public async Task A_date_before_the_first_live_session_is_refused()
    {
        var account = await LiveAccountAsync();
        Fill(account.Id, new DateOnly(2026, 10, 12));
        var result = await Handler().HandleAsync(new LivePhaseCheckQuery(null, new DateOnly(2026, 10, 5)), Ct);
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LiveErrors.Invalid);
    }

    [Fact]
    public async Task No_step_or_no_fill_is_an_empty_window_that_is_not_evaluable_and_never_met()
    {
        await LiveAccountAsync(step: false);
        var none = await Handler().HandleAsync(new LivePhaseCheckQuery(null, null), Ct);
        none.Value.State.ShouldBe("empty");
        none.Value.Report.ExitVerdict.ShouldBe("NOT EVALUABLE");

        _ramp.Seed((await _fx.Data.ListActiveAsync(Ct))[0].Id, RampEventKinds.SetStep, null, 1, false, StepSet);
        var stepOnly = await Handler().HandleAsync(new LivePhaseCheckQuery(null, null), Ct);
        stepOnly.Value.State.ShouldBe("empty");
        (await new PhaseCheckExitReport(Handler()).CheckAsync(Ct)).Met.ShouldBeFalse();
    }

    [Fact]
    public async Task An_unreadable_paper_record_leaves_pairs_unknown_and_nothing_is_written()
    {
        var account = await LiveAccountAsync();
        Fill(account.Id, new DateOnly(2026, 10, 12));
        var rampRows = _ramp.Rows.Count;
        var fills = _capture.Fills.Count;
        var result = await Handler().HandleAsync(new LivePhaseCheckQuery(null, null), Ct);
        result.IsSuccess.ShouldBeTrue();
        result.Value.State.ShouldBe("value");
        result.Value.Report.Criteria.Single(c => c.Code == "C4").Verdict.ShouldBe("NOT EVALUABLE");
        result.Value.Report.ExitVerdict.ShouldNotBe("MET");
        _ramp.Rows.Count.ShouldBe(rampRows);
        _capture.Fills.Count.ShouldBe(fills);
        _capture.Runs.ShouldBeEmpty();
        _capture.Outcomes.ShouldBeEmpty();
        await _compliance.DidNotReceiveWithAnyArgs().AppendAsync(default!, default!, Ct);
    }
}
