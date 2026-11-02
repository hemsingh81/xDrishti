using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Filings;

namespace XDrishti.Api.IntegrationTests.Filings;

internal static class FilingTestKit
{
    public static readonly DateOnly Q1 = new(2026, 6, 30);

    // Friday 2026-07-24 16:12 IST.
    public static readonly DateTimeOffset Broadcast = new(2026, 7, 24, 10, 42, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset Ingested = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    public static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public static NewFiling Ok(Guid instrument, string content, DateOnly? period, DateTimeOffset? ts, FilingKind kind = FilingKind.Results) =>
        new(instrument, kind, period, ts, ts is null ? BroadcastState.Unavailable : BroadcastState.Ok, ts is null ? "date_only" : null, Ingested, Sha(content), content.Length, $"{Sha(content)[..8]}.xml");
}
