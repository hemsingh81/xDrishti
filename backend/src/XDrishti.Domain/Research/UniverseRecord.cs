namespace XDrishti.Domain.Research;

/// <summary>The universe of a run as the record keeps it: how it was chosen, the basket (when it was one), the label that says what it is, and the members in run order (spec P3-04, Contract).</summary>
/// <param name="Kind">How the instruments were chosen.</param>
/// <param name="BasketId">The basket, for <see cref="UniverseKind.Basket"/>; its id goes into <see cref="RunTrade.BasketIds"/> of every trade.</param>
/// <param name="BasketName">The basket's name at run time.</param>
/// <param name="Label">See <see cref="UniverseLabels"/>.</param>
/// <param name="Members">The instruments, in run order (the order of the universe, not of the symbols).</param>
public sealed record UniverseRecord(UniverseKind Kind, Guid? BasketId, string? BasketName, string Label, IReadOnlyList<UniverseMember> Members);
