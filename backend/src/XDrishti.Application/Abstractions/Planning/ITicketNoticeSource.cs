using XDrishti.Application.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>
/// The ticket notices of P7-20 (<c>ticket_level_crossed</c>, <c>post_trade_prompt</c>), exactly as that slice derives them, for the plan notices of the bell (P8-15b). Read only; counts and words, never a symbol or a price; route only, no command.
/// The source has already dropped what the owner snoozed or dismissed and what is past its lifetime; it owns the ids (<c>crossing:{id}</c>, <c>prompt:{id}</c>, never starting with <c>plan:</c>), maps its severity to error/warning/info, and keeps <c>Since</c> and <c>Resolved</c>
/// (a matched prompt stays visible as resolved for a day). An ambiguous prompt (two armed tickets fit one trade) has its own text and id, apart from an open one, because nothing is guessed (ADR 0093).
/// </summary>
public interface ITicketNoticeSource
{
    /// <summary>The current ticket notices at <paramref name="now"/>; empty when ticket alerts are off or not built.</summary>
    ValueTask<IReadOnlyList<PlanNotice>> ListAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
