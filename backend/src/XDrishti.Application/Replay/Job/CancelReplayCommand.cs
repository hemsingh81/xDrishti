using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Replay.Job;

/// <summary>Asks a replay to stop after the session in flight (SR-01, AC 15). Answers the new state: <c>cancelled</c> for a run that had not started, <c>cancelling</c> for one that is running.</summary>
/// <param name="RunId">The run.</param>
public sealed record CancelReplayCommand(Guid RunId) : ICommand;
