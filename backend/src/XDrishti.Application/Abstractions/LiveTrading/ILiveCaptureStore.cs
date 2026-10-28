using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>One run of the capture or the settle to store (it is written when it has ended).</summary>
public sealed record NewCaptureRun(Guid AccountId, string Kind, DateOnly ForDate, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Outcome, int FillsSeen, int FillsNew, int OrdersSeen, int OrdersNew);

/// <summary>An outcome to store: the ticket, the account, the session and the size the owner was allocated.</summary>
public sealed record NewTicketOutcome(long ItemId, Guid AccountId, DateOnly ForDate, long AllocatedQty, TicketOutcome Outcome);

/// <summary>A stored outcome, as written.</summary>
public sealed record StoredOutcome(long Id, long ItemId, Guid AccountId, DateOnly ForDate, long AllocatedQty, TicketOutcome Outcome, DateTimeOffset WrittenAt);

/// <summary>A page of stored outcomes with the total that matched.</summary>
public sealed record StoredOutcomePage(IReadOnlyList<StoredOutcome> Items, int Total);

/// <summary>
/// The live capture record (<c>trading.live_capture_runs</c>, <c>live_fills</c>, <c>live_fill_charges</c>, <c>live_orders</c>, <c>live_ticket_outcomes</c>, spec P11-01, ADR 0149): insert-only, a row that exists is final. Writes skip what is already stored
/// (the same trade seen by ten polls is one row; an order status revision is stored once). Reads only what the owner's own day shows. Nothing here reaches a broker.
/// </summary>
public interface ILiveCaptureStore
{
    /// <summary>Stores the fills not stored yet (unique per account and broker trade id); returns how many were new.</summary>
    ValueTask<int> InsertFillsAsync(IReadOnlyList<LiveFill> fills, string source, CancellationToken cancellationToken);

    /// <summary>Stores the order revisions not stored yet (unique per account, order, status and filled quantity); returns how many were new.</summary>
    ValueTask<int> InsertOrdersAsync(IReadOnlyList<LiveOrder> orders, CancellationToken cancellationToken);

    ValueTask InsertRunAsync(NewCaptureRun run, CancellationToken cancellationToken);

    /// <summary>The stored charges of fills (rupees); a fill without one is absent (unavailable).</summary>
    ValueTask<int> InsertChargesAsync(IReadOnlyList<(Guid FillId, decimal Charges)> charges, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<LiveFill>> FillsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    ValueTask<IReadOnlyDictionary<Guid, decimal>> ChargesAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The newest revision of each order of the day, oldest order first.</summary>
    ValueTask<IReadOnlyList<LiveOrder>> LatestOrdersAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<CaptureRun>> RunsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Per run: the counts too (for the screen).</summary>
    ValueTask<IReadOnlyList<StoredCaptureRun>> RunDetailsAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Stores the outcomes not stored yet (unique per ticket and account); returns how many were written.</summary>
    ValueTask<int> InsertOutcomesAsync(IReadOnlyList<NewTicketOutcome> outcomes, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<StoredOutcome>> OutcomesOfDateAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken);

    /// <summary>Outcomes in a range, newest session first, one page.</summary>
    ValueTask<StoredOutcomePage> ListOutcomesAsync(Guid? accountId, DateOnly from, DateOnly to, int skip, int take, CancellationToken cancellationToken);
}

/// <summary>One stored run with its counts.</summary>
public sealed record StoredCaptureRun(string Kind, DateTimeOffset StartedAt, DateTimeOffset FinishedAt, string Outcome, int FillsNew, int OrdersNew);
