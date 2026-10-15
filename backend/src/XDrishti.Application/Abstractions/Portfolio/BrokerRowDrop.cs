namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>One row of an answer that a mapper dropped: its zero-based index in the answer and the reason code (<see cref="BrokerDropReasons"/>). Nothing of the row's content.</summary>
public readonly record struct BrokerRowDrop(int RowIndex, string Reason);
