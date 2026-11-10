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
    public const string FetchDisabledCode = "baskets.fetch_disabled";
    public const string FetchRunningCode = "baskets.fetch_running";
    public const string DerivedDisabledCode = "baskets.derived_disabled";
    public const string DerivedRunningCode = "baskets.derived_running";
    public const string BackfillOverlapCode = "baskets.backfill_overlap";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The basket does not exist.");

    public static readonly Error Archived = Error.Conflict(ArchivedCode, "The basket is archived, so it cannot be changed.");

    public static readonly Error System = Error.Conflict(SystemCode, "A system basket is maintained automatically (from the account's holdings or from a published list), so it cannot be changed here.");

    public static readonly Error FetchDisabled = Error.Conflict(FetchDisabledCode, "Fetching the system baskets is switched off (Baskets:System:Fetch:Enabled).");

    public static readonly Error FetchRunning = Error.Conflict(FetchRunningCode, "A fetch of the system baskets is running already.");

    public static readonly Error DerivedDisabled = Error.Conflict(DerivedDisabledCode, "The derived baskets are switched off (Baskets:System:Derived:Enabled).");

    public static readonly Error DerivedRunning = Error.Conflict(DerivedRunningCode, "A run of the derived baskets is running already.");

    public static readonly Error BackfillOverlap = Error.Conflict(BackfillOverlapCode, "A backfill covers only dates before the first published version of the basket and never overlaps its current membership.");

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
