using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Job;

/// <summary>Starts the replays of one request (SR-01 part B): one run, or the frozen and the learning run of a pair, which share everything but the mode. Part C's endpoint builds the parameters (the scope, the profile, the default range) and sends this.</summary>
/// <param name="Runs">One or two runs.</param>
public sealed record StartReplayCommand(IReadOnlyList<ReplayParams> Runs) : ICommand;

/// <summary>The runs that were stored.</summary>
/// <param name="RunIds">The ids, in the order of the request.</param>
/// <param name="PairId">The id the two runs of a pair share; null for one run.</param>
/// <param name="TrialNumber">How many replays have looked at this range, this one included (AC 12): "the 7th replay of this range".</param>
public sealed record StartedReplay(IReadOnlyList<Guid> RunIds, Guid? PairId, int TrialNumber);
