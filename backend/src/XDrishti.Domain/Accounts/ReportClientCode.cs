namespace XDrishti.Domain.Accounts;

/// <summary>
/// The client code (UCC) that the downloaded reports of an account carry, remembered one way (ADR 0029): only a hash of the code is kept, never the code, so that a later report for another client code can be noticed. It is a different identifier from the Dhan client id of the
/// account. Remembered once, by the first report that is imported with a code; never changed or deleted (the runtime role can read and insert only).
/// </summary>
public sealed class ReportClientCode
{
    /// <summary>The length of a SHA-256 hash in hexadecimal.</summary>
    public const int HashLength = 64;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private ReportClientCode(Guid accountId, string hash, DateTimeOffset rememberedAt)
    {
        AccountId = accountId;
        Hash = hash;
        RememberedAt = rememberedAt;
    }

    public Guid AccountId { get; private set; }

    /// <summary>The SHA-256 of the code (lower-case hexadecimal), salted by the application.</summary>
    public string Hash { get; private set; }

    public DateTimeOffset RememberedAt { get; private set; }

    public static ReportClientCode Remember(Guid accountId, string hash, DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("A client code belongs to an account.", nameof(accountId));
        }

        var value = hash?.Trim().ToLowerInvariant();
        if (value is not { Length: HashLength } || !value.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException($"The hash is {HashLength} hexadecimal characters.", nameof(hash));
        }

        return new ReportClientCode(accountId, value, now);
    }
}
