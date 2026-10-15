namespace XDrishti.Domain.Portfolio;

/// <summary>
/// Where one dropped row of a broker read lies, and why it was dropped: the dates of the request that returned it (<see cref="WindowFrom"/>, <see cref="WindowTo"/>; after a split, the half that was asked), the <see cref="Page"/> of the answer (0 for the
/// ledger, which has one) and the zero-based <see cref="RowIndex"/> of the row in that answer, so the owner can look the row up in Dhan's own statement. <b>Location and reason code only</b>: never a name, symbol, amount, id or narration of the row (roadmap ISS-03).
/// </summary>
public sealed record BrokerDroppedRow(DateOnly WindowFrom, DateOnly WindowTo, int Page, int RowIndex, string Reason);
