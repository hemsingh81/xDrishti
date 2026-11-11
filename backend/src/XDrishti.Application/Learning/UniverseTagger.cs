using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Gives each trade the liquidity tier of its instrument on its decision date (BK-11, ADR 0261): one read of the daily traded values of the tracked shares, then one tier table per distinct date, built from sessions strictly before that date
/// (<see cref="TierCandidates"/>, <see cref="UniverseTiers"/>), so no later bar can change a past assignment. A share with no session before the date is not in that day's set: <see cref="UniverseTier.Unknown"/>, never today's tier. Reads only.
/// </summary>
internal static class UniverseTagger
{
    /// <param name="source">The daily traded values.</param>
    /// <param name="trades">The decision date and instrument of each trade, in the order of the result.</param>
    /// <param name="options">The tier rule.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<IReadOnlyList<UniverseTier>> TagAsync(IUniverseSeriesSource source, IReadOnlyList<(DateOnly Date, Guid InstrumentId)> trades, UniverseTierOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(options);
        if (trades.Count == 0)
        {
            return [];
        }

        var first = trades.Min(t => t.Date);
        var last = trades.Max(t => t.Date);
        var lookBackDays = (int)Math.Ceiling(options.MinBars * 1.7) + 20;   // 60 sessions are about 90 calendar days; the rest is room for holidays
        var series = await source.ReadAsync(first.AddDays(-lookBackDays), last, cancellationToken);
        var tables = new Dictionary<DateOnly, IReadOnlyDictionary<Guid, UniverseTier>>();
        var result = new UniverseTier[trades.Count];
        for (var i = 0; i < trades.Count; i++)
        {
            var (date, id) = trades[i];
            if (!tables.TryGetValue(date, out var table))
            {
                cancellationToken.ThrowIfCancellationRequested();
                tables[date] = table = UniverseTiers.Table(TierCandidates.At(series, date), options);
            }

            result[i] = table.GetValueOrDefault(id, UniverseTier.Unknown);
        }

        return result;
    }

    /// <summary>The text that names the tiers used, for the snapshot key: the rule and a hash of the assignments in order.</summary>
    public static string Canonical(UniverseTierOptions options, IReadOnlyList<UniverseTier> tiers)
    {
        var text = string.Join(',', tiers.Select(t => (int)t));
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
        return string.Create(CultureInfo.InvariantCulture, $"terciles/{options.MinBars}/{options.MinEligible}/{hash}");
    }
}
