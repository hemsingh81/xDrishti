namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// The canonical text of a run record without its trades and the two digests the store keeps (spec P3-10, D4): the SHA-256 of the UTF-8 bytes of <see cref="Json"/> and the digest of the pair of canonical trade files. The verb produces them
/// from the in-memory run; the store never serialises. Each figure is 64 lower-case hex characters.
/// </summary>
public sealed record RunRecordText
{
    /// <summary>The length of a SHA-256 in lower-case hex.</summary>
    public const int HexLength = 64;

    /// <summary>The text and its digests.</summary>
    /// <exception cref="ArgumentException">The text is empty or a figure is not 64 lower-case hex characters.</exception>
    public RunRecordText(string json, string recordSha256, string tradesSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        Json = json;
        RecordSha256 = RequireHex(recordSha256, nameof(recordSha256));
        TradesSha256 = RequireHex(tradesSha256, nameof(tradesSha256));
    }

    /// <summary>The record in the canonical form of <c>run.json</c>, without <c>trades</c> and <c>open_at_end</c>.</summary>
    public string Json { get; }

    /// <summary>The SHA-256 of the UTF-8 bytes of <see cref="Json"/>.</summary>
    public string RecordSha256 { get; }

    /// <summary>The digest of the pair of canonical trade files (<c>SHA-256(sha256(trades.csv) ":" sha256(open_at_end.csv))</c> over the lower-case hex of the two).</summary>
    public string TradesSha256 { get; }

    /// <summary>True for 64 lower-case hexadecimal characters.</summary>
    public static bool IsHex64(string? value) => value is { Length: HexLength } && value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static string RequireHex(string value, string name) =>
        IsHex64(value) ? value : throw new ArgumentException("A digest is 64 lower-case hex characters.", name);
}
