using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Abstractions.Replay;

/// <summary>Builds the conditional tickets of the next session from the as-of facts only (the way the live ticket source does, through <see cref="IReplayFacts"/>). Read only; the implementation runs the pure ticket builder.</summary>
public interface IReplayCandidates
{
    /// <summary>The tickets for <paramref name="forDate"/> built from data up to the close of <paramref name="session"/>.</summary>
    ValueTask<ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken);
}
