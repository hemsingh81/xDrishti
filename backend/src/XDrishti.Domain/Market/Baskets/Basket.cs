using System.Buffers;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Baskets;

/// <summary>
/// A named, manual group of instruments (docs/design/06 §3). An instrument can be in many baskets. Names are unique among the active baskets
/// (ignoring case; the database enforces it). Archiving hides a basket and keeps its membership history. Every change raises
/// <see cref="Revision"/>, which is the optimistic concurrency token: whoever saves second finds the revision moved and must read again, so
/// concurrent edits of a basket or its member order never overwrite each other.
/// </summary>
public sealed class Basket : Entity<Guid>
{
    /// <summary>Colour of a basket created without one (the indigo of the app's accent).</summary>
    public const string DefaultColour = "#6366f1";

    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private Basket(Guid id, string name, string colour, BasketPurpose purpose, string? description, int position, DateTimeOffset createdAt, DateTimeOffset? archivedAt, int revision)
        : base(id)
    {
        Name = name;
        Colour = colour;
        Purpose = purpose;
        Description = description;
        Position = position;
        CreatedAt = createdAt;
        ArchivedAt = archivedAt;
        Revision = revision;
    }

    public string Name { get; private set; }

    /// <summary>A hex colour such as <c>#6366f1</c> (lower case).</summary>
    public string Colour { get; private set; }

    public BasketPurpose Purpose { get; private set; }

    public string? Description { get; private set; }

    /// <summary>Place among the active baskets (0 first); the owner reorders them.</summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Set when archived: hidden from lists, history kept, no more changes.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Concurrency token: raised by every change of the basket or of its members.</summary>
    public int Revision { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    /// <summary>
    /// Checks and normalises the attributes of a basket: name 1 to <see cref="BasketLimits.MaxNameLength"/> characters (trimmed, no control characters),
    /// colour <c>#rrggbb</c> (default <see cref="DefaultColour"/>), purpose <c>trading</c> · <c>long_term</c> · <c>watch</c> (default <c>trading</c>),
    /// description up to <see cref="BasketLimits.MaxDescriptionLength"/> characters (blank = none). All problems are reported together.
    /// </summary>
    public static Result<BasketDetails> Check(string? name, string? colour, string? purpose, string? description)
    {
        var fields = new FieldErrors();

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName) || trimmedName.Length > BasketLimits.MaxNameLength || trimmedName.Any(char.IsControl))
        {
            fields.Add("name", $"Use 1 to {BasketLimits.MaxNameLength} characters.");
        }

        var checkedColour = DefaultColour;
        if (!string.IsNullOrWhiteSpace(colour))
        {
            var trimmedColour = colour.Trim();
            if (IsHexColour(trimmedColour))
            {
                checkedColour = trimmedColour.ToLowerInvariant();
            }
            else
            {
                fields.Add("colour", "Use a hex colour such as #6366f1.");
            }
        }

        var checkedPurpose = BasketPurpose.Trading;
        if (!string.IsNullOrWhiteSpace(purpose) && !BasketPurposeCodes.TryParse(purpose.Trim(), out checkedPurpose))
        {
            fields.Add("purpose", $"Use {BasketPurposeCodes.Trading}, {BasketPurposeCodes.LongTerm} or {BasketPurposeCodes.Watch}.");
        }

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is not null
            && (trimmedDescription.Length > BasketLimits.MaxDescriptionLength || trimmedDescription.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))))
        {
            fields.Add("description", $"Use at most {BasketLimits.MaxDescriptionLength} characters.");
        }

        return fields.HasErrors
            ? Result.Failure<BasketDetails>(fields.ToError(BasketErrors.InvalidCode, "Some values are not valid."))
            : Result.Success(new BasketDetails(trimmedName!, checkedColour, checkedPurpose, trimmedDescription));
    }

    /// <summary>A new basket at <paramref name="position"/> among the active baskets.</summary>
    public static Basket Create(BasketDetails details, int position, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new Basket(Guid.CreateVersion7(now), details.Name, details.Colour, details.Purpose, details.Description, position, now, null, 0);
    }

    /// <summary>Applies new attributes; false (and no new revision) when nothing differs. The name is compared exactly, so a change of case counts.</summary>
    public bool Update(BasketDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (Name == details.Name && Colour == details.Colour && Purpose == details.Purpose && Description == details.Description)
        {
            return false;
        }

        Name = details.Name;
        Colour = details.Colour;
        Purpose = details.Purpose;
        Description = details.Description;
        Revision++;
        return true;
    }

    /// <summary>Hides the basket; false when it is archived already.</summary>
    public bool Archive(DateTimeOffset now)
    {
        if (IsArchived)
        {
            return false;
        }

        ArchivedAt = now;
        Revision++;
        return true;
    }

    /// <summary>Puts the basket at a place among the active baskets; false when it is there already.</summary>
    public bool MoveTo(int position)
    {
        if (Position == position)
        {
            return false;
        }

        Position = position;
        Revision++;
        return true;
    }

    /// <summary>Raises the revision because members changed, so a concurrent change of the same basket is noticed when saving.</summary>
    public void Touch() => Revision++;

    private static bool IsHexColour(string value) =>
        value.Length == 7 && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept(HexDigits) < 0;
}
