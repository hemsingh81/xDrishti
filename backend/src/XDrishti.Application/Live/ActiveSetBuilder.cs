using System.Globalization;
using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Application.Live;

/// <summary>
/// Builds the active set from what the providers propose (ADR 0024 §2, P2-10 criterion 7) - pure, no I/O. Candidates of the same security (segment and id) are merged into one member: the reasons are added, the mode is the richer
/// one (quote over ticker) and the value is the largest. The members are ordered alerts first, then indices, then holdings by value (largest first; equal values by security id, so that the same input always gives the same set) and
/// cut at the cap; <see cref="ActiveSet.Cut"/> says how many were dropped. A candidate whose security id is not a positive number cannot be subscribed to and is left out.
/// </summary>
internal static class ActiveSetBuilder
{
    public static ActiveSet Build(IEnumerable<ActiveSetCandidate> candidates, int max)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        var merged = new Dictionary<(string Segment, int SecurityId), Merged>();
        foreach (var candidate in candidates)
        {
            if (!int.TryParse(candidate.SecurityId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0 || string.IsNullOrWhiteSpace(candidate.Segment))
            {
                continue;
            }

            var key = (candidate.Segment, id);
            if (merged.TryGetValue(key, out var existing))
            {
                existing.Reasons |= candidate.Reason;
                existing.Mode = (FeedMode)Math.Max((int)existing.Mode, (int)candidate.Mode);
                existing.Value = Math.Max(existing.Value, candidate.Value);
            }
            else
            {
                merged[key] = new Merged(candidate.InstrumentId, candidate.Segment, id.ToString(CultureInfo.InvariantCulture), candidate.Mode, candidate.Reason, candidate.Value);
            }
        }

        var ordered = merged.Values
            .OrderBy(m => Rank(m.Reasons))
            .ThenByDescending(m => m.Value)
            .ThenBy(m => m.Segment, StringComparer.Ordinal)
            .ThenBy(m => int.Parse(m.SecurityId, CultureInfo.InvariantCulture))
            .ToList();
        var kept = ordered.Take(max).Select(m => new ActiveSetMember(m.InstrumentId, m.Segment, m.SecurityId, m.Mode, m.Reasons)).ToList();
        return new ActiveSet(kept, ordered.Count - kept.Count);
    }

    /// <summary>What has to be subscribed to and unsubscribed from to go from <paramref name="current"/> (null: nothing subscribed yet) to <paramref name="next"/>.</summary>
    public static ActiveSetDiff Diff(ActiveSet? current, ActiveSet next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var before = (current?.Members ?? []).ToDictionary(m => (m.Key, m.Mode));
        var after = next.Members.ToDictionary(m => (m.Key, m.Mode));
        return new ActiveSetDiff(
            [.. next.Members.Where(m => !before.ContainsKey((m.Key, m.Mode)))],
            [.. (current?.Members ?? []).Where(m => !after.ContainsKey((m.Key, m.Mode)))]);
    }

    private static int Rank(ActiveSetReason reasons) =>
        reasons.HasFlag(ActiveSetReason.Alert) ? 0 : reasons.HasFlag(ActiveSetReason.Index) ? 1 : 2;

    private sealed class Merged(Guid instrumentId, string segment, string securityId, FeedMode mode, ActiveSetReason reasons, decimal value)
    {
        public Guid InstrumentId { get; } = instrumentId;

        public string Segment { get; } = segment;

        public string SecurityId { get; } = securityId;

        public FeedMode Mode { get; set; } = mode;

        public ActiveSetReason Reasons { get; set; } = reasons;

        public decimal Value { get; set; } = value;
    }
}
