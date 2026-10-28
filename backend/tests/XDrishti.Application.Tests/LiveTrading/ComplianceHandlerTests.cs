using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.LiveTrading;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Tests.LiveTrading;

/// <summary>Spec P11-05 criteria 2, 5 to 7 at the use-case level, on fakes: the gate, the owner, the rules, the audit without the text, the latest per kind and the overdue flag.</summary>
public sealed class ComplianceHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Store : IComplianceStore
    {
        public List<StoredCompliance> Rows { get; } = [];

        public ValueTask<StoredCompliance> AppendAsync(ComplianceRecord record, string actor, CancellationToken cancellationToken)
        {
            var row = new StoredCompliance(Rows.Count + 1, record, actor, DateTimeOffset.UnixEpoch);
            Rows.Add(row);
            return ValueTask.FromResult(row);
        }

        public ValueTask<IReadOnlyList<StoredCompliance>> ListAsync(string? kind, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredCompliance>>([.. Rows.Where(r => kind is null || r.Record.Kind == kind).OrderByDescending(r => r.Id).Take(limit)]);

        public ValueTask<IReadOnlyList<StoredCompliance>> LatestPerKindAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<StoredCompliance>>([.. Rows.GroupBy(r => r.Record.Kind).Select(g => g.OrderByDescending(r => r.Id).First())]);
    }

    private sealed class Gate(bool on) : ILiveWritesGate
    {
        public bool Enabled { get; set; } = on;
    }

    private sealed class Events : IAccountEventLog
    {
        public List<AccountEvent> Recorded { get; } = [];

        public void Record(AccountEvent accountEvent) => Recorded.Add(accountEvent);

        public ValueTask<IReadOnlyList<AccountEvent>> ListForAccountAsync(Guid accountId, int take, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<AccountEvent>>([]);
    }

    private sealed class Unit(bool fail = false) : IAccountUnitOfWork
    {
        public ValueTask<bool> TrySaveChangesAsync(CancellationToken cancellationToken) =>
            fail ? throw new InvalidOperationException("audit store away") : ValueTask.FromResult(true);

        public ValueTask<XDrishti.Domain.Common.Result> RunSerializedAsync(Func<CancellationToken, ValueTask<XDrishti.Domain.Common.Result>> work, CancellationToken cancellationToken) => work(cancellationToken);
    }

    // 2026-10-24 12:00 in India.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 24, 6, 30, 0, TimeSpan.Zero));
    private readonly Store _store = new();
    private readonly Gate _gate = new(true);
    private readonly Events _events = new();

    private RecordComplianceHandler Writer(bool failAudit = false) => new(_store, _gate, _events, new Unit(failAudit), _time, NullLogger<RecordComplianceHandler>.Instance);

    private GetComplianceHandler Reader() => new(_store, _gate, _time);

    private static RecordComplianceCommand Command(string actor = Owner, string kind = "sebi_recheck", string outcome = "rechecked_no_change", string summary = "Read the SEBI circular; the threshold is unchanged.", DateOnly? next = null) =>
        new(actor, kind, outcome, new DateOnly(2026, 10, 24), summary, ["NSE/INVG/73992"], null, null, null, null, null, next ?? new DateOnly(2027, 1, 22));

    [Fact]
    public async Task The_owner_writes_one_row_and_one_audit_event_with_the_kind_the_outcome_and_the_date_and_never_the_text()
    {
        var result = await Writer().HandleAsync(Command(summary: "a private note about the isp call"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Actor.ShouldBe(Owner);
        var audit = _events.Recorded.ShouldHaveSingleItem();
        audit.Event.ShouldBe(AccountEventType.ComplianceRecorded);
        audit.Detail.ShouldBe("record=1 kind=sebi_recheck outcome=rechecked_no_change decided=2026-10-24");
        audit.Actor.ShouldBe(Owner);
        audit.Detail.ShouldNotBeNull().ShouldNotContain("private");
    }

    [Fact]
    public async Task A_closed_gate_refuses_and_writes_nothing()
    {
        _gate.Enabled = false;

        var result = await Writer().HandleAsync(Command(), Ct);

        result.Error.Code.ShouldBe("live.capability_off");
        _store.Rows.ShouldBeEmpty();
        _events.Recorded.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("cli:xd")]
    [InlineData("owner:")]
    public async Task Only_an_owner_actor_writes(string actor)
    {
        var result = await Writer().HandleAsync(Command(actor), Ct);

        result.Error.Code.ShouldBe("live.owner_only");
        _store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_refused_record_is_not_stored_or_audited()
    {
        var result = await Writer().HandleAsync(Command(summary: "the address is 192.168.1.10"), Ct);

        result.Error.Code.ShouldBe("live.invalid");
        result.Error.Message.ShouldStartWith("summary ");
        _store.Rows.ShouldBeEmpty();
        _events.Recorded.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_failing_audit_write_does_not_undo_the_row()
    {
        var result = await Writer(failAudit: true).HandleAsync(Command(), Ct);

        result.IsSuccess.ShouldBeTrue();
        _store.Rows.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_server_derives_the_lock_end_for_a_whitelisted_record()
    {
        var command = Command(kind: "static_ip", outcome: "whitelisted") with { StaticIpState = "whitelisted", WhitelistedAt = new DateOnly(2026, 10, 20), Isp = "ACT Broadband", MonthlyCost = 350m, IpVersion = "v4" };

        var result = await Writer().HandleAsync(command, Ct);

        result.Value.LockEndsOn.ShouldBe(new DateOnly(2026, 10, 27));
    }

    [Fact]
    public async Task Nothing_recorded_reads_as_empty_for_every_kind_and_shows_the_gate()
    {
        var view = (await Reader().HandleAsync(new GetComplianceQuery(null, 50), Ct)).Value;

        (view.Latest.StaticIp, view.Latest.SebiRecheck, view.Latest.DhanIpReadPolicy).ShouldBe((null, null, null));
        view.Items.ShouldBeEmpty();
        view.WritesEnabled.ShouldBeTrue();
        _gate.Enabled = false;
        (await Reader().HandleAsync(new GetComplianceQuery(null, 50), Ct)).Value.WritesEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task The_list_is_newest_first_and_latest_is_the_newest_per_kind()
    {
        await Writer().HandleAsync(Command(outcome: "rechecked_no_change"), Ct);
        await Writer().HandleAsync(Command(kind: "dhan_ip_read_policy", outcome: "unanswered"), Ct);
        await Writer().HandleAsync(Command(outcome: "rechecked_changed"), Ct);

        var view = (await Reader().HandleAsync(new GetComplianceQuery(null, 50), Ct)).Value;
        var onlySebi = (await Reader().HandleAsync(new GetComplianceQuery("sebi_recheck", 1), Ct)).Value;

        view.Items.Select(i => i.Id).ShouldBe([3, 2, 1]);
        view.Latest.SebiRecheck!.Outcome.ShouldBe("rechecked_changed");
        view.Latest.DhanIpReadPolicy!.Id.ShouldBe(2);
        view.Latest.StaticIp.ShouldBeNull();
        onlySebi.Items.Count.ShouldBe(1);
        onlySebi.Latest.DhanIpReadPolicy.ShouldNotBeNull("latest ignores the kind filter and the limit");
    }

    [Theory]
    [InlineData("nope", 50)]
    [InlineData(null, 0)]
    [InlineData(null, 201)]
    public async Task A_bad_filter_is_live_invalid(string? kind, int limit)
    {
        var result = await Reader().HandleAsync(new GetComplianceQuery(kind, limit), Ct);

        result.Error.Code.ShouldBe("live.invalid");
    }

    [Fact]
    public async Task Overdue_is_on_the_newest_record_of_a_kind_only_after_the_review_date_and_a_newer_record_resolves_it()
    {
        await Writer().HandleAsync(Command(next: new DateOnly(2026, 10, 25)), Ct);
        (await Reader().HandleAsync(new GetComplianceQuery(null, 50), Ct)).Value.Latest.SebiRecheck!.Overdue.ShouldBeFalse();

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 26, 6, 30, 0, TimeSpan.Zero));
        var late = (await Reader().HandleAsync(new GetComplianceQuery(null, 50), Ct)).Value;
        late.Latest.SebiRecheck!.Overdue.ShouldBeTrue();

        await Writer().HandleAsync(Command(next: new DateOnly(2027, 1, 22)) with { DecidedOn = new DateOnly(2026, 10, 26) }, Ct);
        var after = (await Reader().HandleAsync(new GetComplianceQuery(null, 50), Ct)).Value;
        after.Latest.SebiRecheck!.Overdue.ShouldBeFalse();
        after.Items.Single(i => i.Id == 1).Overdue.ShouldBeFalse("a superseded record is not flagged");
    }
}
