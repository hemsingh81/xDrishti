using XDrishti.Domain.Checklist;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>What a live account's standing adds to the plan for a session (spec P11-04): the facts of the live-ready gate, and the factor on its scale (1, 0.5 after a weekly halve, 0 under a stop).</summary>
public sealed record LiveStanding(LiveReadyFacts Facts, double ScaleFactor);

/// <summary>Reads the halt, capture and loss facts of a live account for the session being planned. Reads only. A fact that cannot be read is null, which is not a pass.</summary>
public interface ILiveReadyFactsSource
{
    ValueTask<LiveStanding> ForAsync(Guid accountId, DateOnly forDate, bool stepSet, CancellationToken cancellationToken);
}
