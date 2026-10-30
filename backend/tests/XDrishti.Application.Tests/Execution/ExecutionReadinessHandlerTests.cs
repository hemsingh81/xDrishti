using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Execution;
using XDrishti.Domain.Execution;

namespace XDrishti.Application.Tests.Execution;

/// <summary>
/// P12-11 criteria 9, 10, 11 and 12 at the use-case level, on fakes: an attestation is validated, needs an owner and an existing account and never contacts the broker; the readiness view carries P11-05's re-check through a port and
/// the notice derives from it; check now leaves exactly one command row.
/// </summary>
public sealed class ExecutionReadinessHandlerTests
{
    private const string Owner = "owner:hem@example.invalid";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Guid Account = Guid.NewGuid();

    private sealed class Attestations(bool exists = true) : IExecutionIpAttestations
    {
        public List<(Guid Account, string Ip, bool Allowed, string Note, string Actor)> Rows { get; } = [];

        public Task<bool> AccountExistsAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(exists);

        public Task<ExecutionIpAttestationView> InsertAsync(Guid accountId, string ip, bool ordersAllowed, string note, string actor, CancellationToken cancellationToken)
        {
            Rows.Add((accountId, ip, ordersAllowed, note, actor));
            return Task.FromResult(new ExecutionIpAttestationView(1, accountId, ip, ordersAllowed, note, DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class Commands : IExecutionCommands
    {
        public List<(string Kind, Guid? Account, string Payload, string Actor)> Rows { get; } = [];

        public Task<Guid> EnqueueAsync(string kind, Guid? accountId, string payloadJson, string actor, CancellationToken cancellationToken)
        {
            Rows.Add((kind, accountId, payloadJson, actor));
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class Compliance(ExecutionRecheckView? latest) : IComplianceRecords
    {
        public ValueTask<ExecutionRecheckView?> LatestSebiRecheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(latest);
    }

    private sealed class Reader(params ExecutionReadinessAccount[] accounts) : IExecutionReadinessReader
    {
        public DateOnly? AskedFor { get; private set; }

        public Task<IReadOnlyList<ExecutionReadinessAccount>> ReadAsync(DateOnly date, CancellationToken cancellationToken)
        {
            AskedFor = date;
            return Task.FromResult<IReadOnlyList<ExecutionReadinessAccount>>(accounts);
        }
    }

    private static RecordIpAttestationHandler Handler(Attestations store) => new(store, NullLogger<RecordIpAttestationHandler>.Instance);

    private static RecordIpAttestationCommand Command(string? ip = "203.0.113.7", bool? allowed = true, string? note = "checked in Dhan web", Guid? account = null, string actor = Owner) => new(actor, account ?? Account, ip, allowed, note);

    [Fact]
    public async Task A_valid_attestation_is_stored_in_normal_form_with_the_owner_actor_and_the_trimmed_note()
    {
        var store = new Attestations();

        var result = await Handler(store).HandleAsync(Command(ip: "2001:0db8:0000:0000:0000:0000:0000:0001", note: "  seen  "), Ct);

        result.IsSuccess.ShouldBeTrue();
        store.Rows.ShouldBe([(Account, "2001:db8::1", true, "seen", Owner)]);
    }

    [Fact]
    public async Task Orders_allowed_false_is_recorded_too()
    {
        var store = new Attestations();

        (await Handler(store).HandleAsync(Command(allowed: false), Ct)).IsSuccess.ShouldBeTrue();

        store.Rows.Single().Allowed.ShouldBeFalse();
    }

    [Theory]
    [InlineData("not an ip", true, "ok")]
    [InlineData("203.0.113.7:80", true, "ok")]
    [InlineData(null, true, "ok")]
    [InlineData("203.0.113.7", null, "ok")]
    [InlineData("203.0.113.7", true, "<script>")]
    [InlineData("203.0.113.7", true, "line\nbreak")]
    public async Task A_bad_address_a_missing_flag_or_a_note_that_is_not_plain_text_is_a_400_and_stores_nothing(string? ip, bool? allowed, string note)
    {
        var store = new Attestations();

        var result = await Handler(store).HandleAsync(Command(ip, allowed, note), Ct);

        result.Error.Code.ShouldBe("execution.values_invalid");
        store.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_note_of_201_characters_is_refused_and_200_is_kept()
    {
        var store = new Attestations();

        (await Handler(store).HandleAsync(Command(note: new string('x', 201)), Ct)).IsFailure.ShouldBeTrue();
        (await Handler(store).HandleAsync(Command(note: new string('x', 200)), Ct)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_unknown_account_is_404_and_a_non_owner_actor_is_refused()
    {
        var missing = new Attestations(exists: false);

        (await Handler(missing).HandleAsync(Command(), Ct)).Error.Code.ShouldBe("execution.not_found");
        (await Handler(new Attestations()).HandleAsync(Command(actor: "system:x"), Ct)).Error.Code.ShouldBe("execution.owner_only");
        missing.Rows.ShouldBeEmpty();
    }

    [Fact]
    public async Task Check_now_leaves_exactly_one_readiness_now_command_with_the_owner_actor_and_no_data()
    {
        var commands = new Commands();
        var handler = new RequestReadinessCheckHandler(commands, NullLogger<RequestReadinessCheckHandler>.Instance);

        (await handler.HandleAsync(new(Owner), Ct)).IsSuccess.ShouldBeTrue();
        (await handler.HandleAsync(new("system:x"), Ct)).IsFailure.ShouldBeTrue();

        commands.Rows.ShouldBe([("readiness_now", null, "{}", Owner)]);
    }

    private static ExecutionReadinessAccount Row(string state = "ready") => new(Account, "Main", state, [], null, null, null, null, null, null, [], null);

    [Fact]
    public async Task The_view_defaults_to_the_session_of_india_today_and_carries_the_recheck_from_the_port()
    {
        var reader = new Reader(Row());
        var recheck = new ExecutionRecheckView("rechecked_no_change", new DateOnly(2026, 7, 30), new DateOnly(2026, 10, 30));
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 30, 19, 0, 0, TimeSpan.Zero)); // 00:30 on the 31st in India
        var handler = new GetExecutionReadinessHandler(reader, new Compliance(recheck), clock);

        var view = (await handler.HandleAsync(new(null), Ct)).Value;

        view.Date.ShouldBe(new DateOnly(2026, 10, 31));
        reader.AskedFor.ShouldBe(new DateOnly(2026, 10, 31));
        view.Accounts.Single().LastRecheck.ShouldBe(recheck);
    }

    [Fact]
    public async Task The_recheck_notice_is_due_when_there_is_no_record_or_the_review_date_has_come_and_not_otherwise()
    {
        var today = new DateOnly(2026, 10, 30);
        var none = await new Compliance(null).LatestSebiRecheckAsync(Ct);
        var due = await new Compliance(new("rechecked_no_change", new DateOnly(2026, 7, 30), today)).LatestSebiRecheckAsync(Ct);
        var later = await new Compliance(new("rechecked_no_change", new DateOnly(2026, 10, 1), today.AddDays(60))).LatestSebiRecheckAsync(Ct);

        ReadinessNotices.Recheck(today, none?.NextReviewOn, true).ShouldNotBeNull();
        ReadinessNotices.Recheck(today, due?.NextReviewOn, true).ShouldNotBeNull();
        ReadinessNotices.Recheck(today, later?.NextReviewOn, true).ShouldBeNull();
    }
}
