using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Reads;

/// <summary>What a start and a sweep ask in common (SR-02): the scope, capital, risk profile, end date and version rule. Nulls are the defaults of <see cref="StartReplayRequest"/>.</summary>
internal sealed record ReplayCommon(IReadOnlyList<Guid>? BasketIds, IReadOnlyList<Guid>? InstrumentIds, decimal? Capital, Guid? RiskProfileId, DateOnly? To, string? Versions);

/// <summary>The common part of a start, resolved: the end date, the scope, the profile and the version rule.</summary>
internal sealed record ResolvedReplay(DateOnly To, ReplayScope Scope, ReplayRiskProfile Profile, ReplayVersionRule Rule);

/// <summary>Resolves the shared part of a start request and of a sweep request, in the order the single start always did: counts, version rule, busy, end date, scope, profile.</summary>
internal static class ReplayRequestResolver
{
    public const decimal DefaultCapital = 1_000_000m;

    /// <summary>The most baskets and instruments one request may name (checked before anything is read).</summary>
    public const int MaxBaskets = 200;

    public const int MaxInstruments = 2000;

    public static async ValueTask<Result<ResolvedReplay>> ResolveAsync(IReplayFacts facts, IReplayReads reads, IAutoScopeStore autoScope, IRiskProfileStore profiles, ReplayCommon request, CancellationToken cancellationToken)
    {
        if (request.BasketIds is { Count: > MaxBaskets } || request.InstrumentIds is { Count: > MaxInstruments })
        {
            return Result.Failure<ResolvedReplay>(Error.Validation(ReplayDriver.InvalidCode, $"at most {MaxBaskets} baskets and {MaxInstruments} instruments"));
        }

        if (!TryRule(request.Versions, out var rule))
        {
            return Result.Failure<ResolvedReplay>(Error.Validation(ReplayDriver.InvalidCode, "versions is start or asOf"));
        }

        if (await reads.AnyRunningAsync(cancellationToken))
        {
            return Result.Failure<ResolvedReplay>(Error.Conflict(StartReplayHandler.BusyCode, "A replay is queued or running; start another when it has finished or been cancelled."));
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
                return Result.Failure<ResolvedReplay>(Error.Conflict(ReplayDriver.HoldOutCode, "No hold-out block is fixed, so the replay cannot know how far it may read."));
            }
        }

        var scope = new ReplayScope([.. request.BasketIds ?? []], [.. request.InstrumentIds ?? []]);
        if (scope.IsEmpty && await autoScope.LatestAsync(cancellationToken) is { } stored)
        {
            scope = new ReplayScope(stored.BasketIds, stored.InstrumentIds); // the baskets stay baskets: members are read as of each session, with the survivorship label
        }

        var profile = ReplayRiskProfile.Standard;
        if (request.RiskProfileId is { } id)
        {
            if (await profiles.FindAsync(id, cancellationToken) is not { } found)
            {
                return Result.Failure<ResolvedReplay>(Error.Validation(ReplayDriver.InvalidCode, "no such risk profile"));
            }

            profile = From(found);
        }
        else if ((await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.IsSeeded && string.Equals(p.Name, "Standard", StringComparison.OrdinalIgnoreCase)) is { } standard)
        {
            profile = From(standard);
        }

        return Result.Success(new ResolvedReplay(to, scope, profile, rule));
    }

    private static ReplayRiskProfile From(XDrishti.Domain.Accounts.RiskProfile p) =>
        new((double)p.RiskPerTradePct, (double)p.MaxCapitalPerTradePct, (double)p.MaxTotalRiskPct, p.MaxPerSector, (double)p.DailyLossPct);

    private static bool TryRule(string? text, out ReplayVersionRule rule)
    {
        rule = ReplayVersionRule.Start;
        switch (text)
        {
            case null:
            case "start":
                return true;
            case "asOf":
                rule = ReplayVersionRule.AsOf;
                return true;
            default:
                return false;
        }
    }

}
