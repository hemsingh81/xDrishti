namespace XDrishti.Application.Common;

/// <summary>Small checks on collections shared by the validation code of the use cases.</summary>
internal static class CollectionChecks
{
    /// <summary>True when the same value appears more than once.</summary>
    public static bool HasDuplicates<T>(this IReadOnlyCollection<T> items) => items.Distinct().Count() != items.Count;
}
