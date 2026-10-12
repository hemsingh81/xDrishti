using XDrishti.Domain.Common;

namespace XDrishti.Domain.Market.Baskets;

/// <summary>Stable error codes of the baskets (part of the API contract). Field problems come back as <c>errors</c> of the problem details.</summary>
public static class BasketErrors
{
    public const string NotFoundCode = "baskets.not_found";
    public const string InvalidCode = "baskets.invalid";
    public const string NameTakenCode = "baskets.name_taken";
    public const string ArchivedCode = "baskets.archived";
    public const string ConflictCode = "baskets.conflict";
    public const string TooManyMembersCode = "baskets.too_many_members";
    public const string SystemCode = "baskets.system";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The basket does not exist.");

    public static readonly Error Archived = Error.Conflict(ArchivedCode, "The basket is archived, so it cannot be changed.");

    public static readonly Error System = Error.Conflict(SystemCode, "A portfolio basket is maintained automatically from the account's holdings, so it cannot be changed here.");

    public static readonly Error Conflict = Error.Conflict(ConflictCode, "The basket was changed at the same time. Reload it and try again.");

    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }

    public static Error NameTaken(string name)
    {
        var fields = new FieldErrors();
        fields.Add("name", $"A basket called '{name}' exists already.");
        return Error.Conflict(NameTakenCode, "A basket with this name exists already.") with { Fields = fields.Items.ToArray() };
    }

    public static Error TooManyMembers(int current, int adding)
    {
        var fields = new FieldErrors();
        fields.Add("instrumentIds", $"A basket holds at most {BasketLimits.MaxActiveMembers:N0} instruments; it has {current:N0} and {adding:N0} more were added.");
        return fields.ToError(TooManyMembersCode, "The basket would hold too many instruments.");
    }
}
