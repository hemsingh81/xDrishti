using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>An accepted file of the inbox. <see cref="BroadcastTs"/> is a UTC instant or null (then <see cref="Broadcast"/> says why not).</summary>
public sealed record FilingEntry(string Symbol, FilingKind Kind, DateOnly? PeriodEnd, DateTimeOffset? BroadcastTs, BroadcastState Broadcast, string? BroadcastReason, string FileName, long SizeBytes, string Sha256)
{
    public FilingRef Ref => new(FileName, Sha256);
}
