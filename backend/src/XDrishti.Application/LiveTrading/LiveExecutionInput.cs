using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;

namespace XDrishti.Application.LiveTrading;

/// <summary>Everything one entry execution is built from (spec P11-02): the matched fills, the ticket's own levels, the stored plan grade, and the stored market facts. Any fact that does not exist is null.</summary>
/// <param name="OutcomeId">The outcome row.</param>
/// <param name="AccountId">The live account.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="Date">The session.</param>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side">The entry side.</param>
/// <param name="EntryFills">The fills that entered the position (at least one).</param>
/// <param name="ExitFills">The fills that left it.</param>
/// <param name="TicketEntry">The ticket's numeric entry level (P7-19), when it had one.</param>
/// <param name="TicketStop">The ticket's numeric stop, when it had one.</param>
/// <param name="Grade">The stored plan grade, when there is one.</param>
/// <param name="Arrival">The quantity-weighted open of the minute bars the entry orders were created in; null without order evidence or bars.</param>
/// <param name="FirstOrderAt">When the first entry order was created, when known.</param>
/// <param name="Tick">The tick in rupees.</param>
/// <param name="AdvTurnoverCr">The 20-session average daily turnover in crore rupees.</param>
/// <param name="Charges">The recorded charges by fill id (a fill without one is absent).</param>
internal sealed record LiveExecutionInput(
    long OutcomeId,
    Guid AccountId,
    long ItemId,
    DateOnly Date,
    string Symbol,
    Side Side,
    IReadOnlyList<LiveFill> EntryFills,
    IReadOnlyList<LiveFill> ExitFills,
    decimal? TicketEntry,
    decimal? TicketStop,
    TicketGrade? Grade,
    decimal? Arrival,
    DateTimeOffset? FirstOrderAt,
    decimal? Tick,
    decimal? AdvTurnoverCr,
    IReadOnlyDictionary<Guid, decimal> Charges);
