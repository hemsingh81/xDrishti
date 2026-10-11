using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>A named group of holdings (Trading, MyLongTerm, MyChoice, …) that portfolio accounts can default to.</summary>
public sealed class PortfolioBucket : Entity<Guid>
{
    public const int MaxNameLength = 40;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private PortfolioBucket(Guid id, string name, DateTimeOffset createdAt)
        : base(id)
    {
        Name = name;
        CreatedAt = createdAt;
    }

    public string Name { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Letters, digits, spaces, '_' and '-', 1 to <see cref="MaxNameLength"/> characters, not starting or ending with a space.</summary>
    public static bool IsValidName(string? name) =>
        name is { Length: > 0 and <= MaxNameLength }
        && name.Trim().Length == name.Length
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '_' or '-');

    public static Result<PortfolioBucket> Create(string? name, DateTimeOffset now)
    {
        var trimmed = name?.Trim();
        if (!IsValidName(trimmed))
        {
            return Result.Failure<PortfolioBucket>(AccountErrors.Invalid("name", $"Use 1 to {MaxNameLength} letters, digits, spaces, '_' or '-'."));
        }

        return Result.Success(new PortfolioBucket(Guid.CreateVersion7(now), trimmed!, now));
    }
}
