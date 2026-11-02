using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The stratified sample of announcements for labelling (P15-07), pure and seeded. It draws 240 (200 main and 40 reserve): half from the baseline-<c>material</c> categories and half from the others (a half that has fewer than its share gives all it has and the other half makes
/// up the difference), and within each half proportionally across (category, India year) strata with at least one in every non-empty stratum (when a half has more strata than places, the strata whose hash ranks first get the places). Every choice is made by the SHA-256 of the
/// seed and the filing id (or stratum), never by the order of the input, so the same announcements, seed and baseline give the same sample whatever the insert order.
/// </summary>
public static class AnnouncementSampler
{
    public const string Version = "1";

    public const int Main = 200;

    public const int Reserve = 40;

    public const string MaterialHalf = "material";

    public const string OtherHalf = "other";

    public const string TooFewCode = "eval.too_few_announcements";

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public static int YearOf(DateTimeOffset publishedAt) => publishedAt.ToOffset(India).Year;

    public static Result<AnnouncementSamplePlan> Sample(IReadOnlyCollection<SampleCandidate> candidates, AnnouncementCategoryBaseline baseline, long seed)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(baseline);
        const int total = Main + Reserve;
        if (candidates.Count < total)
        {
            return Result.Failure<AnnouncementSamplePlan>(Error.Validation(TooFewCode, $"Only {candidates.Count} dated announcements are stored in that range; {total} are needed."));
        }

        var ranked = candidates.Select(c => (Candidate: c, Rank: Rank(seed, c.FilingId.ToString("D")))).ToList();
        var material = ranked.Where(r => baseline.IsMaterial(r.Candidate.Category)).ToList();
        var other = ranked.Where(r => !baseline.IsMaterial(r.Candidate.Category)).ToList();
        var quotaMaterial = Math.Min(total / 2, material.Count);
        var quotaOther = Math.Min(total - quotaMaterial, other.Count);
        quotaMaterial = Math.Min(total - quotaOther, material.Count);
        var mainMaterial = quotaMaterial * Main / total;
        var mainOther = Main - mainMaterial;

        var strata = new List<SampleStratum>();
        var drawn = new List<(string Half, bool Reserve, (SampleCandidate Candidate, ulong Rank) Row, SampleStratum Stratum)>();
        foreach (var (half, pool, quota, main) in new[] { (MaterialHalf, material, quotaMaterial, mainMaterial), (OtherHalf, other, quotaOther, mainOther) })
        {
            var picks = Draw(half, pool, quota, seed, strata);
            var ordered = picks.OrderBy(p => p.Row.Rank).ThenBy(p => p.Row.Candidate.FilingId).ToList();
            for (var i = 0; i < ordered.Count; i++)
            {
                drawn.Add((half, i >= main, ordered[i].Row, ordered[i].Stratum));
            }
        }

        var rows = new List<SampledAnnouncement>(total);
        foreach (var group in new[] { false, true })
        {
            foreach (var pick in drawn.Where(d => d.Reserve == group).OrderBy(d => d.Row.Rank).ThenBy(d => d.Row.Candidate.FilingId))
            {
                rows.Add(new SampledAnnouncement($"a{rows.Count + 1:000}", pick.Row.Candidate, pick.Half, pick.Reserve, pick.Stratum.Eligible, pick.Stratum.Sampled));
            }
        }

        return Result.Success(new AnnouncementSamplePlan(Version, seed, rows, [.. strata.OrderBy(s => s.Half, StringComparer.Ordinal).ThenBy(s => s.Category, StringComparer.Ordinal).ThenBy(s => s.Year)], material.Count, other.Count));
    }

    private static List<(SampleStratum Stratum, (SampleCandidate Candidate, ulong Rank) Row)> Draw(string half, List<(SampleCandidate Candidate, ulong Rank)> pool, int quota, long seed, List<SampleStratum> output)
    {
        var groups = pool.GroupBy(r => (r.Candidate.Category, Year: YearOf(r.Candidate.PublishedAt)))
            .OrderBy(g => g.Key.Category, StringComparer.Ordinal).ThenBy(g => g.Key.Year).ToList();
        var allowed = new int[groups.Count];
        if (groups.Count > quota)
        {
            // More strata than places: the strata whose own hash ranks first get one place each (the choice never depends on the input order).
            foreach (var index in Enumerable.Range(0, groups.Count).OrderBy(i => Rank(seed, $"{half}|{groups[i].Key.Category}|{groups[i].Key.Year}")).ThenBy(i => i).Take(quota))
            {
                allowed[index] = 1;
            }
        }
        else
        {
            Array.Fill(allowed, 1);
            for (var spare = quota - groups.Count; spare > 0; spare--)
            {
                var best = -1;
                var bestDeficit = double.NegativeInfinity;
                for (var i = 0; i < groups.Count; i++)
                {
                    var deficit = quota * (double)groups[i].Count() / pool.Count - allowed[i];
                    if (allowed[i] < groups[i].Count() && deficit > bestDeficit)
                    {
                        best = i;
                        bestDeficit = deficit;
                    }
                }

                allowed[best]++;
            }
        }

        var picks = new List<(SampleStratum, (SampleCandidate, ulong))>();
        for (var i = 0; i < groups.Count; i++)
        {
            var stratum = new SampleStratum(half, groups[i].Key.Category, groups[i].Key.Year, groups[i].Count(), allowed[i]);
            output.Add(stratum);
            picks.AddRange(groups[i].OrderBy(r => r.Rank).ThenBy(r => r.Candidate.FilingId).Take(allowed[i]).Select(r => (stratum, r)));
        }

        return picks;
    }

    private static ulong Rank(long seed, string key) =>
        System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + key)));
}
