using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Reads;

/// <summary>
/// What the owner asks for when starting a replay (SR-01 part C). <paramref name="To"/> null is the last readable session (before the hold-out and its embargo); an empty scope is the owner's stored auto scope with its baskets as of <paramref name="From"/> (Q8); <paramref name="Capital"/> null is Rs 10,00,000;
/// <paramref name="RiskProfileId"/> null is the seeded standard profile; <paramref name="CostSheetId"/> null is gross; <paramref name="Versions"/> is <c>start</c> or <c>asOf</c> (null is <c>asOf</c>, the version in force at each session); <paramref name="Modes"/> null is frozen only.
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
    bool IncludeResearchCells) : ICommand;

/// <summary>Turns the request into the parameters of one run, or of a frozen and a learning run, and starts them through <see cref="StartReplayHandler"/>, which stores, hashes, checks the hold-out and the memory cap and queues them.</summary>
internal sealed class StartReplayRequestHandler(IReplayFacts facts, IReplayReads reads, IAutoScopeSource autoScope, IRiskProfileStore profiles, ICommandHandler<StartReplayCommand, StartedReplay> start)
    : ICommandHandler<StartReplayRequest, StartedReplay>
{
    public const decimal DefaultCapital = 1_000_000m;

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

        if (!TryRule(request.Versions, out var rule))
        {
            return Invalid("versions is start or asOf");
        }

        if (await reads.AnyRunningAsync(cancellationToken))
        {
            return Result.Failure<StartedReplay>(Error.Conflict(StartReplayHandler.BusyCode, "A replay is queued or running; start another when it has finished or been cancelled."));
        }

        DateOnly to;
        if (request.To is { } given)
        {
            to = given;
        }
        else
        {
            try
            {
                to = await facts.LastReadableSessionAsync(cancellationToken);
            }
            catch (ReplayHoldOutException)
            {
                return Result.Failure<StartedReplay>(Error.Conflict(ReplayDriver.HoldOutCode, "No hold-out block is fixed, so the replay cannot know how far it may read."));
            }
        }

        var scope = new ReplayScope([.. request.BasketIds ?? []], [.. request.InstrumentIds ?? []]);
        if (scope.IsEmpty && await autoScope.ResolveAsync(request.From, cancellationToken) is { } auto)
        {
            scope = new ReplayScope([], [.. auto.InstrumentIds.Order()]);
        }

        var profile = ReplayRiskProfile.Standard;
        if (request.RiskProfileId is { } id)
        {
            if (await profiles.FindAsync(id, cancellationToken) is not { } found)
            {
                return Invalid("no such risk profile");
            }

            profile = From(found);
        }
        else if ((await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.IsSeeded && string.Equals(p.Name, "Standard", StringComparison.OrdinalIgnoreCase)) is { } standard)
        {
            profile = From(standard);
        }

        var runs = modes.Select(mode => new ReplayParams(request.From, to, scope, request.Capital ?? DefaultCapital, profile, request.CostSheetId, rule, mode, request.FiltersWithoutHistory, request.IncludeResearchCells)).ToList();
        return await start.HandleAsync(new StartReplayCommand(runs), cancellationToken);
    }

    private static ReplayRiskProfile From(XDrishti.Domain.Accounts.RiskProfile p) =>
        new((double)p.RiskPerTradePct, (double)p.MaxCapitalPerTradePct, (double)p.MaxTotalRiskPct, p.MaxPerSector, (double)p.DailyLossPct);

    private static bool TryRule(string? text, out ReplayVersionRule rule)
    {
        rule = ReplayVersionRule.AsOf;
        switch (text)
        {
            case null:
            case "asOf":
                return true;
            case "start":
                rule = ReplayVersionRule.Start;
                return true;
            default:
                return false;
        }
    }

    private static Result<StartedReplay> Invalid(string message) => Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, message));
}
