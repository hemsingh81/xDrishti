using System.Globalization;

namespace XDrishti.Domain.Research;

/// <summary>
/// How the members of a basket run were chosen when the request named a <c>membersBasis</c> (BK-11b, ADR 0266): structured, so that a reader never has to parse the label. <c>single_snapshot</c> is the members on the run date (survivorship applies);
/// <c>dated</c> is the members of each replayed day. Part of the run key (line <c>umembers</c>). Null for a run that did not say, whose record and key are what they were.
/// </summary>
/// <param name="UniverseBasketId">The basket.</param>
/// <param name="UniverseParamsHash">The parameter hash of the version of a derived basket in force at the window start; null for a manual or index basket.</param>
/// <param name="MembersAsOf">The run date for <c>single_snapshot</c>; null for <c>dated</c>.</param>
/// <param name="UniverseBasis"><see cref="Dated"/> or <see cref="SingleSnapshot"/>.</param>
/// <param name="MemberDaysHash">Lower-case hex SHA-256 of the member intervals used, clipped to the window and ordered (<see cref="DatedMembers.Hash"/>); <c>dated</c> only.</param>
public sealed record UniverseBasisRecord(Guid UniverseBasketId, string? UniverseParamsHash, DateOnly? MembersAsOf, string UniverseBasis, string? MemberDaysHash)
{
    /// <summary>The members of each replayed day.</summary>
    public const string Dated = "dated";

    /// <summary>The members on the run date only.</summary>
    public const string SingleSnapshot = "single_snapshot";

    /// <summary>Whether this is the point-in-time basis: the only one that may be called that.</summary>
    public bool IsDatedBasis() => string.Equals(UniverseBasis, Dated, StringComparison.Ordinal);

    /// <summary>The value of the run key line <c>umembers</c>: basis, hash (or the date), parameter hash.</summary>
    public string KeyValue() =>
        UniverseBasis + "|" + (IsDatedBasis() ? MemberDaysHash ?? "-" : MembersAsOf?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-") + "|" + (UniverseParamsHash ?? "-");

    /// <summary>The words every view prints for the basis; "point in time" only for <c>dated</c>.</summary>
    public string Statement() => IsDatedBasis() ? "members: point in time (dated members)" : "members: single snapshot, survivorship applies";
}
