namespace XDrishti.Domain.Reporting;

/// <summary>One member of a basket with its daily bars and its flags (BK-06).</summary>
/// <param name="Symbol">The symbol; membership is keyed by it.</param>
/// <param name="Closes">The daily closes, in any order.</param>
/// <param name="Volumes">The daily volumes, in any order; empty when unknown (capacity then lists the member as short).</param>
/// <param name="IsFno">Whether the member is in the F&amp;O segment.</param>
/// <param name="IsAsmGsm">Whether the member is under ASM or GSM.</param>
public sealed record MemberSeries(string Symbol, IReadOnlyList<DailyClose> Closes, IReadOnlyList<DailyVolume> Volumes, bool IsFno, bool IsAsmGsm);
