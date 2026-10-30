using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Execution;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Execution;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Execution;

/// <summary>P12-07 criteria 2 to 6 and 11 (the modes part) at the use-case level, on fakes: request, cooling-off, expiry, a newer request, the gate again at confirm, demotion without a password, and every refusal leaving nothing written.</summary>
public sealed class ExecutionModeHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static readonly Guid Account = Guid.NewGuid();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Store(TradingMode configured = TradingMode.ManualLive) : IExecutionModeStore
    {
        public List<ModeTrailRow> Trail { get; } = [];

        public List<NewModeRequest> Requests { get; } = [];

        public string? Refusal { get; set; }

        public Task<IReadOnlyList<ModeAccountRow>> AccountsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModeAccountRow>>([new ModeAccountRow(Account, "Main", configured)]);

        public Task<IReadOnlyList<ModeTrailRow>> TrailAsync(Guid accountId, int take, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ModeTrailRow>>([.. Trail.OrderByDescending(t => t.Id).Take(take)]);

        public Task<ModeRequestRow?> NewestRequestAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Requests.Count == 0 ? null : Row(Requests[^1]));

        public Task<ModeRequestRow?> FindRequestAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Requests.Where(r => r.Id == id).Select(Row).FirstOrDefault());

        public Task<string?> InsertRequestAsync(NewModeRequest request, CancellationToken cancellationToken)
        {
            if (Refusal is null)
            {
                Requests.Add(request);
            }

            return Task.FromResult(Refusal);
        }

        public Task<string?> InsertTrailAsync(NewModeTrailRow row, CancellationToken cancellationToken)
        {
            if (Refusal is null)
            {
                Trail.Add(new ModeTrailRow(Trail.Count + 1, row.AccountId, row.From, row.To, row.Kind, row.Actor, row.Reason, row.RequestId, row.EffectiveFrom, DateTimeOffset.UnixEpoch));
            }

            return Task.FromResult(Refusal);
        }

        private static ModeRequestRow Row(NewModeRequest r) => new(r.Id, r.AccountId, r.From, r.To, DateTimeOffset.UnixEpoch, r.EligibleFrom, r.ExpiresAt, r.EvidenceHash);
    }

    private sealed class Facts(ModeGateFacts facts) : IExecutionModeFacts
    {
        public ModeGateFacts Current { get; set; } = facts;

        public Task<bool?> ExecutionEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(Current.ExecutionEnabled);

        public Task<ModeGateFacts> ReadAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Current);
    }

    private sealed class Phase(string? verdict) : IQueryHandler<LivePhaseCheckQuery, Result<LivePhaseCheckView>>
    {
        public ValueTask<Result<LivePhaseCheckView>> HandleAsync(LivePhaseCheckQuery query, CancellationToken cancellationToken) =>
            ValueTask.FromResult(verdict is null
                ? Result.Failure<LivePhaseCheckView>(Error.Validation("x", "no"))
                : Result.Success(new LivePhaseCheckView("value", new LivePhaseReport(new DateOnly(2026, 10, 26), null, 0, 0, verdict, string.Empty, [], [], [], [], [], string.Empty, []), string.Empty)));
    }

    private sealed class Calendar : ITradingCalendar
    {
        public ValueTask<TradingCalendar> GetAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new TradingCalendar());

        public void Invalidate()
        {
        }
    }

    private static readonly string[] Reasons = ["execution enabled", "small-live phase check", "sandbox suite result", "static-IP attestation"];

    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 5, 0, 0, TimeSpan.Zero);

    private sealed class Rig
    {
        public Store Store { get; }

        public Facts Facts { get; }

        public FakeTimeProvider Time { get; } = new(Monday);

        private readonly ModeEvidence _evidence;

        public Rig(string? verdict = "MET", ModeGateFacts? facts = null, TradingMode configured = TradingMode.ManualLive)
        {
            Store = new Store(configured);
            Facts = new Facts(facts ?? Good(Monday));
            _evidence = new ModeEvidence(Facts, new Phase(verdict));
        }

        public RequestPromotionHandler Request => new(Store, _evidence, new Calendar(), Time, NullLogger<RequestPromotionHandler>.Instance);

        public ConfirmPromotionHandler Confirm => new(Store, _evidence, new Calendar(), Time, NullLogger<ConfirmPromotionHandler>.Instance);

        public DemoteModeHandler Demote => new(Store, Time, NullLogger<DemoteModeHandler>.Instance);

        public GetExecutionModesHandler Get => new(Store, Facts, _evidence, Time);
    }

    private static ModeGateFacts Good(DateTimeOffset now) => new(true, true, true, null, now.AddDays(-2), now.AddDays(-2));

    [Fact]
    public async Task A_request_with_every_fact_good_stores_the_evidence_a_window_and_grants_nothing()
    {
        var rig = new Rig();

        var result = await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "ready to try the sheet"), Ct);

        result.IsSuccess.ShouldBeTrue();
        var stored = rig.Store.Requests.Single();
        (stored.From, stored.To, stored.By).ShouldBe(("manual_live", "assisted", Owner));
        stored.EligibleFrom.ShouldBe(new DateTimeOffset(2026, 10, 28, 15, 30, 0, TimeSpan.FromMinutes(330)));
        stored.ExpiresAt.ShouldBe(new DateTimeOffset(2026, 11, 2, 15, 30, 0, TimeSpan.FromMinutes(330)));
        stored.EvidenceHash.Length.ShouldBe(64);
        stored.EvidenceJson.ShouldContain("live_phase_check");
        rig.Store.Trail.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("semi_auto")]
    [InlineData("auto_live")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public async Task The_automated_modes_and_unknown_names_are_values_the_request_refuses(string? to)
    {
        var rig = new Rig();

        var result = await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, to, "why"), Ct);

        result.Error.Code.ShouldBe(ExecutionModeErrors.ValuesInvalid);
        rig.Store.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_skip_a_non_owner_a_bad_reason_and_an_unknown_account_are_refused_and_nothing_is_written()
    {
        var rig = new Rig(configured: TradingMode.Paper);
        var live = new Rig();

        (await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "why"), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.NotAdjacent);
        (await live.Request.HandleAsync(new RequestPromotionCommand("system:x", Account, "assisted", "why"), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.OwnerOnly);
        (await live.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "   "), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.ValuesInvalid);
        (await live.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "my password is x"), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.ValuesInvalid);
        (await live.Request.HandleAsync(new RequestPromotionCommand(Owner, Guid.NewGuid(), "assisted", "why"), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.UnknownAccount);
        rig.Store.Requests.Concat(live.Store.Requests).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_unavailable_fact_is_a_refusal_that_lists_every_reason_and_stores_nothing()
    {
        var rig = new Rig(verdict: null, facts: new ModeGateFacts(null, true, true, null, null, null));

        var result = await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "why"), Ct);

        result.Error.Code.ShouldBe(ExecutionModeErrors.GateFailed);
        Reasons.ShouldAllBe(w => result.Error.Message.Contains(w, StringComparison.Ordinal));
        rig.Store.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_phase_check_that_is_not_met_closes_the_gate()
    {
        var rig = new Rig(verdict: "NOT MET");

        (await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "why"), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.GateFailed);
    }

    private static async Task<(Rig Rig, Guid Id)> RequestedAsync(Rig? given = null)
    {
        var rig = given ?? new Rig();
        (await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "why"), Ct)).IsSuccess.ShouldBeTrue();
        return (rig, rig.Store.Requests[^1].Id);
    }

    [Fact]
    public async Task Before_the_cooling_off_ends_the_confirm_says_when_and_after_the_window_it_says_expired()
    {
        var (rig, id) = await RequestedAsync();

        rig.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 9, 59, 0, TimeSpan.Zero)); // Wednesday 15:29 IST: one minute short
        var early = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, id), Ct);
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 11, 2, 10, 1, 0, TimeSpan.Zero)); // Monday 15:31 IST: one minute late
        var late = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, id), Ct);

        early.Error.Code.ShouldBe(ExecutionModeErrors.CoolingOff);
        late.Error.Code.ShouldBe(ExecutionModeErrors.Expired);
        rig.Store.Trail.ShouldBeEmpty();
    }

    [Fact]
    public async Task Inside_the_window_with_the_evidence_still_good_the_confirm_writes_one_promotion_row_that_takes_effect_at_the_next_open()
    {
        var (rig, id) = await RequestedAsync();
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 10, 1, 0, TimeSpan.Zero)); // Wednesday 15:31 IST

        var result = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, id), Ct);

        result.IsSuccess.ShouldBeTrue();
        var row = rig.Store.Trail.Single();
        (row.From, row.To, row.Kind, row.Actor, row.RequestId).ShouldBe(("manual_live", "assisted", "promotion", Owner, id));
        row.EffectiveFrom.ShouldBe(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.FromMinutes(330)));
    }

    [Fact]
    public async Task The_gate_is_evaluated_again_at_the_confirm_and_a_fact_that_went_bad_in_between_stops_it()
    {
        var (rig, id) = await RequestedAsync();
        rig.Facts.Current = Good(Monday) with { StaticIpAttestedAt = null };
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 10, 1, 0, TimeSpan.Zero));

        var result = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, id), Ct);

        result.Error.Code.ShouldBe(ExecutionModeErrors.GateFailed);
        rig.Store.Trail.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_newer_request_supersedes_the_older_one_and_a_request_of_another_account_is_unknown()
    {
        var (rig, first) = await RequestedAsync();
        (await rig.Request.HandleAsync(new RequestPromotionCommand(Owner, Account, "assisted", "again"), Ct)).IsSuccess.ShouldBeTrue();
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 10, 1, 0, TimeSpan.Zero));

        var old = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, first), Ct);
        var unknown = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, Guid.NewGuid()), Ct);

        old.Error.Code.ShouldBe(ExecutionModeErrors.Stale);
        unknown.Error.Code.ShouldBe(ExecutionModeErrors.UnknownRequest);
        rig.Store.Trail.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_database_refusal_comes_back_as_a_conflict_and_a_non_owner_cannot_confirm()
    {
        var (rig, id) = await RequestedAsync();
        rig.Time.SetUtcNow(new DateTimeOffset(2026, 10, 28, 10, 1, 0, TimeSpan.Zero));

        (await rig.Confirm.HandleAsync(new ConfirmPromotionCommand("system:x", Account, id), Ct)).Error.Code.ShouldBe(ExecutionModeErrors.OwnerOnly);
        rig.Store.Refusal = "the global execution switch is OFF";
        var refused = await rig.Confirm.HandleAsync(new ConfirmPromotionCommand(Owner, Account, id), Ct);

        refused.Error.Code.ShouldBe(ExecutionModeErrors.GateFailed);
        refused.Error.Message.ShouldContain("switch is OFF");
    }

    [Fact]
    public async Task A_demotion_needs_no_password_goes_to_a_lower_mode_only_and_may_skip_steps()
    {
        var rig = new Rig();
        rig.Store.Trail.Add(new ModeTrailRow(1, Account, "manual_live", "assisted", "promotion", Owner, "r", Guid.NewGuid(), Monday.AddDays(-1), Monday.AddDays(-2)));

        var same = await rig.Demote.HandleAsync(new DemoteModeCommand(Owner, Account, "assisted", "x"), Ct);
        var up = await rig.Demote.HandleAsync(new DemoteModeCommand(Owner, Account, "semi_auto", "x"), Ct);
        var down = await rig.Demote.HandleAsync(new DemoteModeCommand(Owner, Account, "paper", "gap down day"), Ct);

        same.Error.Code.ShouldBe(ExecutionModeErrors.NotLower);
        up.Error.Code.ShouldBe(ExecutionModeErrors.ValuesInvalid);
        down.IsSuccess.ShouldBeTrue();
        var row = rig.Store.Trail.OrderByDescending(t => t.Id).First();
        (row.From, row.To, row.Kind, row.Actor).ShouldBe(("assisted", "paper", "owner_demotion", Owner));
    }

    [Fact]
    public async Task The_modes_view_shows_the_effective_mode_the_pending_change_the_evidence_and_the_open_request()
    {
        var rig = new Rig();
        var empty = await rig.Get.HandleAsync(new GetExecutionModesQuery(), Ct);
        var (_, id) = await RequestedAsync(rig);
        rig.Store.Trail.Add(new ModeTrailRow(1, Account, "manual_live", "assisted", "promotion", Owner, "r", id, Monday.AddDays(1), Monday));

        var view = await rig.Get.HandleAsync(new GetExecutionModesQuery(), Ct);

        var before = empty.Accounts.Single();
        (before.ConfiguredMode, before.EffectiveMode, before.Pending).ShouldBe(("manual_live", "manual_live", null));
        before.Next!.Verdict.ShouldBe("eligible");
        before.Next.Checks.Select(c => c.Outcome).ShouldAllBe(o => o == "pass");
        var after = view.Accounts.Single();
        (after.EffectiveMode, after.Pending!.To, after.Request).ShouldBe(("manual_live", "assisted", null)); // the request is used up by its promotion
        view.GatePolicyVersion.ShouldBe(ModeGate.PolicyVersion);
        view.ExecutionEnabled.ShouldBe(true);
    }
}
