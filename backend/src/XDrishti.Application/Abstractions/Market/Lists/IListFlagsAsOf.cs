namespace XDrishti.Application.Abstractions.Market.Lists;

/// <summary>
/// What the dated lists say for a set of instruments: whether each list is covered on its date, and the banned or surveilled instruments. An instrument of a list that is not covered answers <c>null</c> (unavailable), never false.
/// </summary>
public sealed record ListFlags(bool BanCovered, bool SurveillanceCovered, IReadOnlySet<Guid> Banned, IReadOnlySet<Guid> Surveilled)
{
    public static ListFlags None { get; } = new(false, false, new HashSet<Guid>(), new HashSet<Guid>());

    /// <summary>True when banned on the date, false when the list is covered and the instrument is not in it, null when the list is not covered for the date.</summary>
    public bool? OnBan(Guid instrumentId) => BanCovered ? Banned.Contains(instrumentId) : null;

    /// <summary>True when under ASM or GSM on the date, false when covered and not in the list, null when not covered.</summary>
    public bool? UnderSurveillance(Guid instrumentId) => SurveillanceCovered ? Surveilled.Contains(instrumentId) : null;
}

/// <summary>
/// The as-of port of DATA-01 (ADR 0274): the F&amp;O-ban list for <c>banDate</c> (the session the plan is for) and the ASM/GSM list for <c>surveillanceDate</c> (the session the plan is built at), from the stored dated rows.
/// A date with no stored row is not covered. An empty instrument set still answers the coverage.
/// </summary>
public interface IListFlagsAsOf
{
    ValueTask<ListFlags> ForAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly banDate, DateOnly surveillanceDate, CancellationToken cancellationToken);
}
