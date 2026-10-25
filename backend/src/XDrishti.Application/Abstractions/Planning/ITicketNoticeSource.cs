namespace XDrishti.Application.Abstractions.Planning;

/// <summary>What the bell may say about one plan's watched tickets beyond the plan record (P7-20 via P8-15b): counts only, never a symbol or a price.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="Crossings">Armed tickets whose indicative stop or target level was crossed, not snoozed or dismissed and within their lifetime.</param>
/// <param name="Prompts">Post-trade prompts still open or ambiguous (tickets that were armed; never the approved tickets the fills notice already counts).</param>
public sealed record TicketNoticeCount(long PlanId, int Crossings, int Prompts);

/// <summary>Counts of ticket alerts and post-trade prompts for the plan notices. Read only. The default source returns nothing (alerts off), so the notices answer exactly as before.</summary>
public interface ITicketNoticeSource
{
    /// <summary>The counts for <paramref name="planIds"/> at <paramref name="now"/>; plans with nothing to say may be left out.</summary>
    ValueTask<IReadOnlyList<TicketNoticeCount>> CountsAsync(IReadOnlyList<long> planIds, DateTimeOffset now, CancellationToken cancellationToken);
}
