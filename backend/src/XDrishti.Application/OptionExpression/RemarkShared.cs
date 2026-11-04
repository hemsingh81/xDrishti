using System.Globalization;
using System.Text;
using System.Text.Json;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;

namespace XDrishti.Application.OptionExpression;

/// <summary>The parts of the option re-mark that its handlers share (P14-08, P14-10): the stretches, the usable key-sessions, the one-minute closes, the cash trade as the re-mark reads it, and the two JSON texts of a run row. No I/O of its own: the ports are passed in.</summary>
internal static class RemarkShared
{
    public const int ChunkDays = 31;
    public const int QualityRowLimit = 1_000_000;

    /// <summary>A cash trade with its instrument.</summary>
    internal sealed record Prepared(Guid InstrumentId, RemarkTrade Trade);

    /// <summary>Splits trades ordered by date into stretches of at most a month of entries: the bars of a series are read stretch by stretch.</summary>
    internal static List<List<(Prepared Trade, SeriesPlan Plan)>> Chunks(List<(Prepared Trade, SeriesPlan Plan)> ordered)
    {
        var chunks = new List<List<(Prepared, SeriesPlan)>>();
        DateOnly? start = null;
        foreach (var item in ordered)
        {
            if (start is null || item.Trade.Trade.TradeDate.DayNumber - start.Value.DayNumber >= ChunkDays)
            {
                chunks.Add([]);
                start = item.Trade.Trade.TradeDate;
            }

            chunks[^1].Add(item);
        }

        return chunks;
    }

    /// <summary>The usable key-sessions of the underlying over the dates of all its trades, read once: <c>fetch_key|date</c> for every key-session whose stored verdicts make it usable (ADR 0178). A key-session with no verdict is not in it.</summary>
    internal static async ValueTask<HashSet<string>> UsableAsync(IOptionQualityStore quality, Guid underlyingId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var results = await quality.RollingResultsAsync(underlyingId, from, to, QualityRowLimit, cancellationToken);
        var usable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keySession in results.Where(r => r.FetchKey is not null).GroupBy(r => (Key: r.FetchKey!, r.SessionDate)))
        {
            if (SessionUsability.IsUsable(QualityCodes.Rolling, [.. keySession.Select(r => r.Check)]))
            {
                usable.Add(string.Create(CultureInfo.InvariantCulture, $"{keySession.Key.Key}|{keySession.Key.SessionDate:yyyy-MM-dd}"));
            }
        }

        return usable;
    }

    internal static string KeySession(Guid underlyingId, char flag, int code, string side, int interval, int offset, DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{underlyingId}:{flag}{code}:{offset}:{side}:{interval}|{date:yyyy-MM-dd}");

    internal static async ValueTask<Dictionary<long, int>> SpotAsync(IBarStore minuteBars, Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var minutes = await minuteBars.ReadAsync(instrumentId, from.ToUniversalTime(), to.ToUniversalTime(), cancellationToken);
        var map = new Dictionary<long, int>(minutes.Count);
        foreach (var bar in minutes)
        {
            map[bar.EpochSeconds] = (int)Math.Round(bar.Close * 100, MidpointRounding.AwayFromZero);
        }

        return map;
    }

    internal static Prepared Prepare(SimTrade trade, bool openAtEnd)
    {
        var costing = trade.Costing;
        var net = trade.Costs == CostBasis.Charged && costing is not null;
        var quantity = costing?.Quantity ?? 1L;
        var entry = (decimal)trade.EntryPrice;
        var stop = (decimal)trade.StopPrice;
        var risk = costing?.RiskTaken ?? Math.Abs(entry - stop);
        return new Prepared(
            trade.InstrumentId,
            new RemarkTrade(trade.Key, trade.Side == Side.Buy, trade.TradeDate, trade.EntryEpochSeconds, trade.ExitEpochSeconds, ExitWord(trade.ExitReason), entry, stop, quantity, costing is not null, risk, net ? costing!.RNet : trade.RFilled, net, openAtEnd));
    }

    private static string ExitWord(ExitReason reason)
    {
        var name = reason.ToString();
        var text = new StringBuilder(name.Length + 2);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(name[i]));
        }

        return text.ToString();
    }

    internal static string UnpairedJson(IReadOnlyDictionary<string, int> counts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (reason, count) in counts.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                writer.WriteNumber(reason, count);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static string InputsJson(string cashRunKey, string profileHash, long watermark, string usableSha, List<string> spreadInputs, string sheet, string lotsSha, string epochsSha, string contractsSha, string settingsText, int usableKeySessions, IReadOnlyDictionary<string, string>? extra = null)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", RemarkRunId.Format);
            writer.WriteString("cash_run_key", cashRunKey);
            writer.WriteString("profile_hash", profileHash);
            writer.WriteString("remark_version", RemarkRunId.Version);
            writer.WriteNumber("archive_watermark", watermark);
            writer.WriteString("usable_key_sessions_sha256", usableSha);
            writer.WriteNumber("usable_key_sessions", usableKeySessions);
            writer.WriteStartArray("spread");
            foreach (var input in spreadInputs.Order(StringComparer.Ordinal))
            {
                writer.WriteStringValue(input);
            }

            writer.WriteEndArray();
            writer.WriteString("cost_sheet", sheet);
            writer.WriteString("lots_sha256", lotsSha);
            writer.WriteString("epochs_sha256", epochsSha);
            writer.WriteString("contract_expiries_sha256", contractsSha);
            writer.WriteString("settings", settingsText);
            foreach (var (name, value) in (extra ?? new Dictionary<string, string>()).OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                writer.WriteString(name, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
