using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

/// <summary>Builders for stored filings with deterministic ids.</summary>
internal static class FilingSamples
{
    public static readonly Guid Instrument = new("11111111-1111-1111-1111-111111111111");

    public static readonly DateTimeOffset Ingested = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public static string Sha(int n) => n.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);

    public static StoredFiling Ok(int n, DateOnly? period, DateTimeOffset ts, Guid? supersedes = null, FilingKind kind = FilingKind.Results, DateTimeOffset? ingested = null, long bytes = 100) =>
        new(FilingId.Create(Instrument, kind, period, Sha(n)), Instrument, kind, period, ts, BroadcastState.Ok, null, ingested ?? Ingested, Sha(n), bytes, $"f{n}.xml", supersedes);

    public static StoredFiling Undated(int n, DateOnly? period, string reason = "date_only", FilingKind kind = FilingKind.Results) =>
        new(FilingId.Create(Instrument, kind, period, Sha(n)), Instrument, kind, period, null, BroadcastState.Unavailable, reason, Ingested, Sha(n), 100, $"f{n}.xml", null);

    public static NewFiling New(int n, DateOnly? period, DateTimeOffset? ts, FilingKind kind = FilingKind.Results) =>
        new(Instrument, kind, period, ts, ts is null ? BroadcastState.Unavailable : BroadcastState.Ok, ts is null ? "date_only" : null, Ingested, Sha(n), 100, $"f{n}.xml");

    /// <summary>The serialized answer, for byte-for-byte comparison.</summary>
    public static string Serialize(AsOfResult result, bool withCounts = true) =>
        (withCounts ? $"{result.State}|{result.UnavailableCount}|" : string.Empty)
        + string.Join(";", result.Filings.Select(f => $"{f.Id}/{f.PeriodEnd:yyyy-MM-dd}/{f.BroadcastTs?.UtcDateTime:O}/{f.IngestedAt.UtcDateTime:O}/{f.FileBytes}/{f.FileName}/{f.Supersedes}"));
}
