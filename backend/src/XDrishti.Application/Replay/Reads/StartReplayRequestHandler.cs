using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Reads;

/// <summary>
/// What the owner asks for when starting a replay (SR-01 part C). <paramref name="To"/> null is the last readable session (before the hold-out and its embargo); an empty scope is the owner's stored auto scope, its baskets and instruments, so the members are read as of each session (Q8); <paramref name="Capital"/> null is Rs 10,00,000;
/// <paramref name="RiskProfileId"/> null is the seeded standard profile; <paramref name="CostSheetId"/> null is gross; <paramref name="Versions"/> is <c>start</c> or <c>asOf</c> (null is <c>start</c>, Q1); <paramref name="Modes"/> null is frozen only.
/// </summary>
public sealed record StartReplayRequest(
    DateOnly From,
    DateOnly? To,
    IReadOnlyList<Guid>? BasketIds,
    IReadOnlyList<Guid>? InstrumentIds,
    decimal? Capital,
    Guid? RiskProfileId,
    string? CostSheetId,
    string? Versions,
    IReadOnlyList<string>? Modes,
    bool FiltersWithoutHistory,
    bool IncludeResearchCells,
    bool Baseline = false) : ICommand;

/// <summary>Turns the request into the parameters of one run, or of a frozen and a learning run, and starts them through <see cref="StartReplayHandler"/>, which stores, hashes, checks the hold-out and the memory cap and queues them.</summary>
internal sealed class StartReplayRequestHandler(IReplayFacts facts, IReplayReads reads, IAutoScopeStore autoScope, IRiskProfileStore profiles, ICommandHandler<StartReplayCommand, StartedReplay> start)
    : ICommandHandler<StartReplayRequest, StartedReplay>
{
    public const decimal DefaultCapital = ReplayRequestResolver.DefaultCapital;

    public async ValueTask<Result<StartedReplay>> HandleAsync(StartReplayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var modes = new List<ReplayMode>();
        foreach (var m in (request.Modes is { Count: > 0 } ? request.Modes : ["frozen"]).Distinct(StringComparer.Ordinal))
        {
            switch (m)
            {
                case "frozen":
                    modes.Add(ReplayMode.Frozen);
                    break;
                case "learning":
                    modes.Add(ReplayMode.Learning);
                    break;
                default:
                    return Invalid("modes are frozen and/or learning");
            }
        }

        if (request.Baseline && (modes.Count != 1 || modes[0] != ReplayMode.Frozen))
        {
            return Invalid("the baseline runs in frozen mode only");
        }

        var resolved = await ReplayRequestResolver.ResolveAsync(facts, reads, autoScope, profiles, new ReplayCommon(request.BasketIds, request.InstrumentIds, request.Capital, request.RiskProfileId, request.To, request.Versions), cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<StartedReplay>(resolved.Error);
        }

        var r = resolved.Value;
        var runs = modes.Select(mode => new ReplayParams(request.From, r.To, r.Scope, request.Capital ?? DefaultCapital, r.Profile, request.CostSheetId, r.Rule, mode, request.FiltersWithoutHistory, request.IncludeResearchCells)).ToList();
        if (request.Baseline)
        {
            runs.Add(runs[0] with { Arm = ReplayArm.Baseline });
        }

        return await start.HandleAsync(new StartReplayCommand(runs), cancellationToken);
    }

    private static Result<StartedReplay> Invalid(string message) => Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, message));
}
