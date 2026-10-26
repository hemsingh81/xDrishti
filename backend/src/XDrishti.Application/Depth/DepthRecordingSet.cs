using XDrishti.Application.Abstractions.Depth;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Planning;

namespace XDrishti.Application.Depth;

/// <summary>The day's set and how many named shares had no usable Dhan id.</summary>
internal sealed record DepthRecordingSetResult(IReadOnlyList<DepthSetEntry> Entries, int Unmapped);

/// <summary>
/// The shares whose depth is recorded for a day (P7-16 criterion 5) - pure, from read-only inputs: the armed tickets of today in rank order (at most <c>MaxArmed</c>), then the owner's pinned symbols, then the most liquid tracked shares, until
/// <c>MaxInstruments</c> distinct instruments. A share is taken once, by its first reason. A named symbol with no entry in the securities map is skipped and counted. This is not an active-set provider: it changes nothing the live feed or a ticket uses.
/// </summary>
internal static class DepthRecordingSet
{
    public static DepthRecordingSetResult Build(
        IReadOnlyList<ArmedTicket> armed,
        IReadOnlyList<string> pinnedSymbols,
        IReadOnlyList<DepthTurnover> liquid,
        IReadOnlyDictionary<string, InstrumentSecurity> securities,
        int maxInstruments,
        int maxArmed)
    {
        ArgumentNullException.ThrowIfNull(armed);
        ArgumentNullException.ThrowIfNull(pinnedSymbols);
        ArgumentNullException.ThrowIfNull(liquid);
        ArgumentNullException.ThrowIfNull(securities);
        var entries = new List<DepthSetEntry>();
        var seen = new HashSet<Guid>();
        var unmapped = 0;

        void Add(string symbol, InstrumentSecurity security, string reason)
        {
            if (entries.Count < maxInstruments && int.TryParse(security.SecurityId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var securityId) && securityId > 0 && seen.Add(security.InstrumentId))
            {
                entries.Add(new DepthSetEntry(security.InstrumentId, securityId, symbol, reason, entries.Count + 1));
            }
        }

        foreach (var ticket in armed.OrderBy(t => t.Rank).ThenBy(t => t.Symbol, StringComparer.Ordinal).Take(Math.Max(0, maxArmed)))
        {
            if (securities.TryGetValue(ticket.Symbol, out var security))
            {
                Add(ticket.Symbol, security, DepthReasons.Armed);
            }
            else
            {
                unmapped++;
            }
        }

        foreach (var symbol in pinnedSymbols.Distinct(StringComparer.Ordinal))
        {
            if (securities.TryGetValue(symbol, out var security))
            {
                Add(symbol, security, DepthReasons.Pinned);
            }
            else
            {
                unmapped++;
            }
        }

        foreach (var row in liquid.OrderByDescending(l => l.MeanTurnover).ThenBy(l => l.Security.Symbol, StringComparer.Ordinal))
        {
            Add(row.Security.Symbol, row.Security, DepthReasons.Turnover);
        }

        return new DepthRecordingSetResult(entries, unmapped);
    }
}
