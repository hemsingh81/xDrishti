using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sweeps;

/// <summary>
/// The owner's request for a start-date sweep (SR-02, D3): 2 to 12 start dates that share one end date (default the last readable session) and one parameter set, frozen mode, selection arm. Each start is a trial-ledger look.
/// </summary>
public sealed record StartReplaySweepRequest(
    IReadOnlyList<DateOnly> Starts,
    DateOnly? To,
    IReadOnlyList<Guid>? BasketIds,
    IReadOnlyList<Guid>? InstrumentIds,
    decimal? Capital,
    Guid? RiskProfileId,
    string? CostSheetId,
    string? Versions,
    bool FiltersWithoutHistory,
    bool IncludeResearchCells) : ICommand;

/// <summary>Builds one run per start from the shared parameters and queues them together through <see cref="StartReplayHandler"/>: it checks every start (the sessions before the end, the frozen cell statistics as of the start) and answers 422 naming each bad date; 409 <c>replay.busy</c> while any replay is active.</summary>
internal sealed class StartReplaySweepRequestHandler(IReplayFacts facts, IReplayReads reads, IAutoScopeStore autoScope, IRiskProfileStore profiles, ICommandHandler<StartReplayCommand, StartedReplay> start)
    : ICommandHandler<StartReplaySweepRequest, StartedReplay>
{
    public async ValueTask<Result<StartedReplay>> HandleAsync(StartReplaySweepRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var early = ReplaySweepRules.Problems(request.Starts, DateOnly.MaxValue);
        if (early.Count > 0)
        {
            return Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, string.Join("; ", early)));
        }

        var resolved = await ReplayRequestResolver.ResolveAsync(facts, reads, autoScope, profiles, new ReplayCommon(request.BasketIds, request.InstrumentIds, request.Capital, request.RiskProfileId, request.To, request.Versions), cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<StartedReplay>(resolved.Error);
        }

        var r = resolved.Value;
        var problems = ReplaySweepRules.Problems(request.Starts, r.To);
        if (problems.Count > 0)
        {
            return Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, string.Join("; ", problems)));
        }

        var ordered = request.Starts.Order().ToList();
        var runs = ordered.Select(from => new ReplayParams(from, r.To, r.Scope, request.Capital ?? ReplayRequestResolver.DefaultCapital, r.Profile, request.CostSheetId, r.Rule, ReplayMode.Frozen, request.FiltersWithoutHistory, request.IncludeResearchCells)).ToList();
        return await start.HandleAsync(new StartReplayCommand(runs, ordered), cancellationToken);
    }
}
