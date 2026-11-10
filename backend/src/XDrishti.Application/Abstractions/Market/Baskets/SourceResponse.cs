namespace XDrishti.Application.Abstractions.Market.Baskets;

/// <summary>What one conditional GET brought back. <c>Status</c> is the HTTP status, or 0 when nothing came back (timeout, no connection; <c>Error</c> says which).</summary>
public sealed record SourceResponse(int Status, string? ContentType, byte[]? Body, string? Etag, string? LastModified, string? Error)
{
    public static SourceResponse Unreachable(string error) => new(0, null, null, null, null, error);
}
