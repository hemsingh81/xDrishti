using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>An instrument of a basket in the window, with what the capacity and the flags need; <paramref name="FlagsKnown"/> is false for one added and removed inside the window, whose F&amp;O and ASM/GSM flags are not read.</summary>
internal sealed record MemberRef(Guid Id, string Symbol, string? Sector, bool IsFno, bool IsAsmGsm, bool FlagsKnown = true);

/// <summary>
/// A basket's membership over a window (BK-07 criterion 4): every instrument that is a member on some day, the set by date (<see cref="At"/>), the dates on which a snapshot exists and the basis: <see cref="MemberTurnover.Dated"/> when there is membership history,
/// <see cref="MemberTurnover.SingleSnapshot"/> when the members are those of one date (survivorship applies, ISS-22).
/// </summary>
/// <param name="Id">The basket.</param>
/// <param name="Name">Its name.</param>
/// <param name="Kind">manual, portfolio, index, list or derived.</param>
/// <param name="ParamsHash">The parameter hash of the newest version of a derived basket.</param>
/// <param name="Instruments">Every instrument that is a member on some day of the window, in symbol order.</param>
/// <param name="At">The symbols in the basket at the end of a date.</param>
/// <param name="SnapshotDates">The India dates of the membership changes, ascending.</param>
/// <param name="AsOf">The date of the member list.</param>
/// <param name="Basis">dated or single_snapshot.</param>
/// <param name="Version">Changes with every membership change: part of the cache key.</param>
internal sealed record BasketMembership(
    Guid Id,
    string Name,
    string Kind,
    string? ParamsHash,
    IReadOnlyList<MemberRef> Instruments,
    Func<DateOnly, IReadOnlySet<string>> At,
    IReadOnlyList<DateOnly> SnapshotDates,
    DateOnly AsOf,
    string Basis,
    string Version);
