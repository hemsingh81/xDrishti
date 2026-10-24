namespace XDrishti.Application.Abstractions.Planning;

/// <summary>An armed ticket as the feed and the expiry job need it.</summary>
/// <param name="ItemId">The plan item.</param>
/// <param name="PlanId">Its plan.</param>
/// <param name="Rank">The ticket's rank in the plan (1 is first).</param>
/// <param name="Symbol">The canonical symbol.</param>
public sealed record ArmedTicket(long ItemId, long PlanId, int Rank, string Symbol);

/// <summary>Reads the tickets whose newest state is Armed, for plans of one session (spec P7-11). Read only.</summary>
public interface IArmedTicketReader
{
    /// <summary>The armed tickets of the plans for <paramref name="forDate"/>, in rank order.</summary>
    ValueTask<IReadOnlyList<ArmedTicket>> ListAsync(DateOnly forDate, CancellationToken cancellationToken);
}
