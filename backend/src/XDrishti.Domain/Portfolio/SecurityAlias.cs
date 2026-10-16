using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// A security name that a file taught the application belongs to an ISIN (ADR 0029): the depository's holding statement and the depository-charges sheet give the ISIN together with the security's name, which the transaction report lacks. The name is kept as the
/// matcher compares names (<see cref="NameKey"/>: lower case words, punctuation and company suffixes gone) with the name as the file wrote it for display. A name can lead to more than one ISIN (a rename, a demerger): the application then asks rather than guesses.
/// Immutable and never deleted.
/// </summary>
public sealed class SecurityAlias : Entity<Guid>
{
    public const int MaxNameKeyLength = 200;

    public const int MaxNameLength = 200;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private SecurityAlias(Guid id, Guid accountId, string nameKey, string isin, string name, SecurityAliasSource source, string createdBy, DateTimeOffset createdAt)
        : base(id)
    {
        AccountId = accountId;
        NameKey = nameKey;
        Isin = isin;
        Name = name;
        Source = source;
        CreatedBy = createdBy;
        CreatedAt = createdAt;
    }

    public Guid AccountId { get; private set; }

    public string NameKey { get; private set; }

    public string Isin { get; private set; }

    /// <summary>The name as the file wrote it (plain text, untrusted, cut to <see cref="MaxNameLength"/>).</summary>
    public string Name { get; private set; }

    public SecurityAliasSource Source { get; private set; }

    public string CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static SecurityAlias Create(Guid accountId, string nameKey, string isin, string? name, SecurityAliasSource source, string createdBy, DateTimeOffset now)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An alias belongs to an account.", nameof(accountId));
        }

        var key = nameKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxNameKeyLength)
        {
            throw new ArgumentException($"The name key is 1 to {MaxNameKeyLength} characters.", nameof(nameKey));
        }

        var code = TradeRules.NormaliseIsin(isin);
        if (code is null || code.Length > Holding.MaxIsinLength)
        {
            throw new ArgumentException("An alias needs an ISIN.", nameof(isin));
        }

        var clean = new string([.. (name ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c)]).Trim();
        clean = clean.Length <= MaxNameLength ? clean : clean[..MaxNameLength];
        var actor = createdBy?.Trim();
        actor = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= Transaction.MaxCreatedByLength ? actor : actor[..Transaction.MaxCreatedByLength];
        return new SecurityAlias(Guid.CreateVersion7(now), accountId, key, code, clean, source, actor, now);
    }
}
