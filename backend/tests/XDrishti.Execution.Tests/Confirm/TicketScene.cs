using Microsoft.Extensions.Time.Testing;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Intents;

namespace XDrishti.Execution.Tests.Confirm;

/// <summary>
/// One fake for every port of the ticket issuer and the closer, so a test reads like a scene: the intent, the account's mode, the plan approval and capital, the newest full run, the live price, the settings and the stored tickets are
/// plain fields. 2026-10-27 is a Tuesday; the clock starts at 09:46:00.4 India time, inside the entry window.
/// </summary>
internal sealed class TicketScene : IConfirmTicketStore, IConfirmDecisions, IModeSource, IIntentQuotes, IIntentSettings, IConfirmSettings
{
    public static readonly Guid Account = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly DateOnly Session = new(2026, 10, 27);
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private readonly List<(StoredTicket Ticket, TicketDraft? Draft)> _tickets = [];

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 9, 46, 0, Ist).AddMilliseconds(400));

    public TicketIntent? Intent { get; set; } = new(7, Guid.Parse("44444444-4444-4444-4444-444444444444"), Account, 501, Session, "NSE:TATAPOWER", ExecSide.Buy, 120, 412.35m, 405.10m, 425.80m, ExecState.Checking);

    public TicketContext? Context { get; set; } = new("Dhan", "Main", new DateTimeOffset(2026, 10, 23, 19, 41, 37, Ist), 400_000m, new TicketRun(91, 0, []));

    public Dictionary<Guid, ModeAnswer?> Modes { get; } = new() { [Account] = new ModeAnswer(ExecMode.SemiAuto, true) };

    public LivePrice? Price { get; set; }

    public IntentSettings? Window { get; set; } = new(new TimeOnly(9, 20), new TimeOnly(15, 0), 2.0m, CanaryPending: false, TimeSpan.FromSeconds(5));

    public ConfirmSettings? Confirm { get; set; } = ConfirmSettings.Default;

    public string? RefuseNextInsert { get; set; }

    public List<string> Expired { get; } = [];

    public IReadOnlyList<StoredTicket> Tickets => [.. _tickets.Select(t => t.Ticket)];

    public IReadOnlyList<TicketDraft> Drafts => [.. _tickets.Where(t => t.Draft is not null).Select(t => t.Draft!)];

    public TicketScene() => PriceNow(412.30m);

    public ConfirmTicketIssuer Issuer(ExecEnvironment environment = ExecEnvironment.Sandbox) => new(this, this, this, this, this, environment, Clock);

    public void PriceNow(decimal last) => Price = new LivePrice(last, Clock.GetUtcNow());

    /// <summary>Adds an open ticket of another intent of the same account (for the open-ticket cap).</summary>
    public void OtherOpenTicket() =>
        _tickets.Add((new StoredTicket(Guid.NewGuid(), Guid.NewGuid(), Account, 1, "x", new string('a', 64), false, Clock.GetUtcNow(), Clock.GetUtcNow().AddSeconds(90), null, null, null), null));

    public void Decide(Guid ticket, string outcome, string reason)
    {
        var i = _tickets.FindIndex(t => t.Ticket.Id == ticket);
        _tickets[i] = (_tickets[i].Ticket with { Outcome = outcome, OutcomeReason = reason, DecidedAt = Clock.GetUtcNow() }, _tickets[i].Draft);
    }

    public Task<TicketIntent?> IntentAsync(long intentId, CancellationToken cancellationToken) => Task.FromResult(Intent?.IntentId == intentId ? Intent : null);

    public Task<TicketContext?> ContextAsync(TicketIntent intent, CancellationToken cancellationToken) => Task.FromResult(Context);

    public Task<IReadOnlyList<StoredTicket>> TicketsAsync(Guid intentPublicId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StoredTicket>>([.. Tickets.Where(t => t.IntentPublicId == intentPublicId).OrderBy(t => t.Attempt)]);

    public Task<int> OpenCountAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult(Tickets.Count(t => t.AccountId == accountId && t.Outcome is null && t.ExpiresAt > Clock.GetUtcNow()));

    public Task<TicketInsert> InsertAsync(TicketDraft draft, DateTimeOffset issuedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        if (RefuseNextInsert is { } refused)
        {
            RefuseNextInsert = null;
            return Task.FromResult(new TicketInsert(null, refused));
        }

        if (Tickets.Any(t => t.IntentPublicId == draft.Intent.PublicId && t.Attempt == draft.Attempt))
        {
            return Task.FromResult(new TicketInsert(null, null));
        }

        var stored = new StoredTicket(
            Guid.NewGuid(), draft.Intent.PublicId, draft.Intent.AccountId, draft.Attempt, draft.Text, draft.TextHash, draft.Password.Required, issuedAt, expiresAt, draft.RefuseAs is null ? null : TicketOutcomes.Refused, draft.RefuseAs,
            draft.RefuseAs is null ? null : issuedAt);
        _tickets.Add((stored, draft));
        return Task.FromResult(new TicketInsert(stored, null));
    }

    public Task<IReadOnlyList<ClosedTicket>> ExpireDueAsync(string actor, CancellationToken cancellationToken)
    {
        var now = Clock.GetUtcNow();
        List<ClosedTicket> closed = [];
        foreach (var t in Tickets.Where(t => t.Outcome is null && t.ExpiresAt <= now).ToList())
        {
            Decide(t.Id, TicketOutcomes.Expired, TicketOutcomes.TtlReason);
            Expired.Add(actor);
            closed.Add(new ClosedTicket(t.Id, t.IntentPublicId, t.AccountId, TicketOutcomes.Expired, TicketOutcomes.TtlReason, (long)(now - t.IssuedAt).TotalMilliseconds));
        }

        return Task.FromResult<IReadOnlyList<ClosedTicket>>(closed);
    }

    public Task<bool?> RefuseAsync(Guid ticketId, string reason, string actor, CancellationToken cancellationToken)
    {
        if (Tickets.FirstOrDefault(t => t.Id == ticketId) is not { } ticket)
        {
            return Task.FromResult<bool?>(null);
        }

        if (ticket.Outcome is not null)
        {
            return Task.FromResult<bool?>(false);
        }

        actor.ShouldBe(TicketOutcomes.TicketActor);
        Decide(ticketId, TicketOutcomes.Refused, reason);
        return Task.FromResult<bool?>(true);
    }

    public Task<ConfirmDecision?> NewestAsync(long intentId, CancellationToken cancellationToken)
    {
        var newest = Intent?.IntentId == intentId ? Tickets.Where(t => t.IntentPublicId == Intent.PublicId).OrderBy(t => t.Attempt).LastOrDefault() : null;
        return Task.FromResult(newest is null ? null : new ConfirmDecision(newest.Id, newest.Outcome, newest.DecidedAt, Clock.GetUtcNow()));
    }

    public Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken) => Task.FromResult(Modes.GetValueOrDefault(accountId));

    public Task<LivePrice?> LastAsync(string symbol, CancellationToken cancellationToken) => Task.FromResult(Price);

    IntentSettings? IIntentSettings.Current() => Window;

    ConfirmSettings? IConfirmSettings.Current() => Confirm;
}
