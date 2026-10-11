using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// A named group of holdings. Two kinds: the default bucket of an account that has the Portfolio role (created automatically, named
/// "{account name} · Portfolio", linked by <see cref="DefaultForAccountId"/>), and custom buckets the owner adds.
/// </summary>
public sealed class PortfolioBucket : Entity<Guid>
{
    /// <summary>Longest name of a custom bucket (typed by the owner).</summary>
    public const int MaxNameLength = 40;

    /// <summary>Longest stored name: room for an account name, the default suffix and a numeric collision suffix.</summary>
    public const int MaxStoredNameLength = 100;

    /// <summary>Follows the account name in the name of its default bucket.</summary>
    public const string DefaultSuffix = " · Portfolio";

    /// <summary>Added to the name of the bucket of an archived account, so that the name becomes free again.</summary>
    public const string ArchivedSuffix = " (archived)";

    /// <summary>Highest numeric suffix tried when names collide (" 2", " 3", …).</summary>
    public const int MaxCollisionSuffix = 99;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private PortfolioBucket(Guid id, string name, Guid? defaultForAccountId, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        DefaultForAccountId = defaultForAccountId;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }

    /// <summary>The account this is the default bucket of; null for custom buckets.</summary>
    public Guid? DefaultForAccountId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when the account was archived: the bucket and its history stay, but it is not offered any more.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    public bool IsAccountDefault => DefaultForAccountId is not null;

    /// <summary>Letters, digits, spaces, '_' and '-', 1 to <see cref="MaxNameLength"/> characters, not starting or ending with a space.</summary>
    public static bool IsValidName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength }
        && name.Trim().Length == name.Length
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-');

    /// <summary>The name of the default bucket of an account called <paramref name="accountName"/>, before any collision suffix.</summary>
    public static string DefaultNameFor(string accountName)
    {
        ArgumentNullException.ThrowIfNull(accountName);
        return accountName.Trim() + DefaultSuffix;
    }

    /// <summary>
    /// <paramref name="baseName"/> if no bucket has it yet, otherwise the first of "{baseName} 2", "{baseName} 3", … that is free
    /// (<paramref name="isTaken"/> compares ignoring case). Null when all are taken.
    /// </summary>
    public static string? FreeName(string baseName, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(isTaken);
        if (!isTaken(baseName))
        {
            return baseName;
        }

        for (var n = 2; n <= MaxCollisionSuffix; n++)
        {
            var candidate = $"{baseName} {n}";
            if (!isTaken(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// The name a new default bucket of an account called <paramref name="accountName"/> gets, given the names that exist (compared ignoring case):
    /// <see cref="DefaultNameFor"/>, with a numeric suffix when it is taken. Null when all numbered names are taken.
    /// </summary>
    public static string? FreeDefaultName(string accountName, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(existingNames);
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        return FreeName(DefaultNameFor(accountName), taken.Contains);
    }

    /// <summary>A custom bucket the owner adds.</summary>
    public static Result<PortfolioBucket> Create(string? name, DateTimeOffset now)
    {
        var trimmed = name?.Trim();
        if (!IsValidName(trimmed))
        {
            return Result.Failure<PortfolioBucket>(AccountErrors.Invalid("name", $"Use 1 to {MaxNameLength} letters, digits, spaces, '_' or '-'."));
        }

        return Result.Success(new PortfolioBucket(Guid.CreateVersion7(now), trimmed!, null, now));
    }

    /// <summary>The default bucket of an account; <paramref name="name"/> is built from the account name (see <see cref="DefaultNameFor"/>).</summary>
    public static Result<PortfolioBucket> CreateDefault(Guid accountId, string? name, DateTimeOffset now)
    {
        if (!IsValidStoredName(name))
        {
            return Result.Failure<PortfolioBucket>(AccountErrors.Invalid("name", $"The bucket name must have 1 to {MaxStoredNameLength} characters."));
        }

        return Result.Success(new PortfolioBucket(Guid.CreateVersion7(now), name!, accountId, now));
    }

    /// <summary>Gives the default bucket of a renamed account its new name. Custom buckets cannot be renamed.</summary>
    public Result Rename(string? name)
    {
        if (!IsAccountDefault)
        {
            return Result.Failure(AccountErrors.Conflict("Only the default bucket of an account is renamed with the account."));
        }

        if (!IsValidStoredName(name))
        {
            return Result.Failure(AccountErrors.Invalid("name", $"The bucket name must have 1 to {MaxStoredNameLength} characters."));
        }

        Name = name!;
        return Result.Success();
    }

    /// <summary>The name an archived bucket starts from: the current name plus <see cref="ArchivedSuffix"/>, kept within the stored length.</summary>
    public string ArchivedBaseName()
    {
        var room = MaxStoredNameLength - ArchivedSuffix.Length - " 99".Length;
        var stem = Name.Length <= room ? Name : Name[..room].TrimEnd();
        return stem + ArchivedSuffix;
    }

    /// <summary>
    /// Marks the bucket as belonging to an archived account and gives it a name that frees the old one: <see cref="ArchivedBaseName"/>, numbered when
    /// <paramref name="isTaken"/> says it exists. Does nothing when it is archived already.
    /// </summary>
    public void Archive(DateTimeOffset now, Func<string, bool> isTaken)
    {
        ArgumentNullException.ThrowIfNull(isTaken);
        if (ArchivedAt is not null)
        {
            return;
        }

        ArchivedAt = now;
        Name = FreeName(ArchivedBaseName(), isTaken) ?? Name;
    }

    private static bool IsValidStoredName(string? name) =>
        name is { Length: > 0 and <= MaxStoredNameLength } && name.Trim().Length == name.Length && !name.Any(char.IsControl);
}
