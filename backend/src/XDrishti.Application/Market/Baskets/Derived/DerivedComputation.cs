using System.Text.Json;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Domain.Baskets.Derived;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>
/// The glue between the pure engine (BK-02) and the stored version (BK-03): the universe of a date, the previous version as the engine's input, and the engine's answer as a <see cref="DerivedVersionWrite"/>.
/// No I/O: the same bars always give the same version, whoever calls it (the scheduled run or a backfill).
/// </summary>
internal static class DerivedComputation
{
    /// <summary>Calendar days of daily bars read before the date: 252 sessions plus the skipped month of basket 6 and a margin for holidays.</summary>
    public const int LookbackDays = 420;

    /// <summary>The instruments with a daily bar on <paramref name="asOf"/>: retired ones with a bar that day included, today's tracked set never consulted.</summary>
    public static List<UniverseInstrument> UniverseOf(IReadOnlyList<UniverseInstrument> all, DateOnly asOf) =>
        [.. all.Where(u => HasBar(u.Bars.Daily, asOf))];

    /// <summary>Whether the daily array (sorted by date) has a bar of the date.</summary>
    private static bool HasBar(Bar[] bars, DateOnly date)
    {
        int lo = 0, hi = bars.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            var found = bars[mid].Date;
            if (found == date)
            {
                return true;
            }

            if (found < date)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return false;
    }

    public static Result<DerivedParams> ParamsOf(DerivedDefinitionRow definition) => DerivedParams.Create(1, definition.Overrides);

    public static string ParamsJson(DerivedParams parameters) => JsonSerializer.Serialize(new { version = parameters.Version, values = parameters.Values });

    public static Result<DerivedVersionWrite> Build(
        DerivedDefinitionRow definition,
        DerivedParams parameters,
        IReadOnlyList<UniverseInstrument> all,
        DateOnly asOf,
        DateOnly effectiveFrom,
        DateOnly? validTo,
        PreviousVersion? previous,
        int scoresKept,
        string provenance,
        DateTimeOffset now,
        string actor)
    {
        var universe = UniverseOf(all, asOf);
        var symbols = universe.ToDictionary(u => u.InstrumentId, u => u.Bars.Symbol);
        var ids = universe.ToDictionary(u => u.Bars.Symbol, u => u.InstrumentId, StringComparer.Ordinal);
        var evaluated = DerivedCatalogue.Evaluator(definition.DefinitionNo).Evaluate([.. universe.Select(u => u.Bars)], asOf, parameters, PreviousResult(previous, symbols, parameters.Hash));
        if (evaluated.IsFailure)
        {
            return Result.Failure<DerivedVersionWrite>(evaluated.Error);
        }

        var result = evaluated.Value;
        var scoreOf = result.Scores.ToDictionary(s => s.Symbol, StringComparer.Ordinal);
        var scores = new List<VersionScore>();
        foreach (var member in result.Members)
        {
            var score = scoreOf.GetValueOrDefault(member.Symbol);
            scores.Add(new VersionScore(ids[member.Symbol], score?.Score ?? 0, score?.Rank ?? 0, true, member.SessionsHeld));
        }

        var memberSet = result.Members.Select(m => m.Symbol).ToHashSet(StringComparer.Ordinal);
        scores.AddRange(result.Scores.Where(s => s.Rank > 0 && !memberSet.Contains(s.Symbol)).OrderBy(s => s.Rank).Take(scoresKept)
            .Select(s => new VersionScore(ids[s.Symbol], s.Score, s.Rank, false, 0)));
        var excluded = result.Excluded.Select(e => new VersionExclusion(ids[e.Symbol], e.Symbol, e.Reason)).ToList();
        return Result.Success(new DerivedVersionWrite(
            Guid.CreateVersion7(now), definition.BasketId, definition.BasketName, asOf, effectiveFrom, validTo, null, ParamsJson(parameters), parameters.Hash,
            universe.Count, result.BelowThreshold.Count, excluded.Count, JsonSerializer.Serialize(excluded), provenance, VersionOutcomes.Applied, scores, now, actor));
    }

    /// <summary>The previous version as the engine's input: the members with the sessions they have held (the engine counts one more) and, for the clusters, the cluster of each member (kept in the score).</summary>
    private static DerivedResult? PreviousResult(PreviousVersion? previous, Dictionary<Guid, string> symbols, string hash)
    {
        if (previous is null)
        {
            return null;
        }

        var members = previous.Scores.Where(s => s.IsMember && symbols.ContainsKey(s.InstrumentId)).ToList();
        return new DerivedResult(
            [.. members.Select(m => new DerivedMember(symbols[m.InstrumentId], m.SessionsHeld))],
            [],
            [],
            [],
            [.. members.Select(m => new ClusterAssignment(symbols[m.InstrumentId], (int)m.Score, string.Empty))],
            null,
            hash);
    }

    /// <summary>The previous version as the next one's "previous", for a backfill that chains its own versions in memory.</summary>
    public static PreviousVersion AsPrevious(DerivedVersionWrite version) => new(version.VersionId, version.AsOf, version.EffectiveFrom, version.Scores);
}
