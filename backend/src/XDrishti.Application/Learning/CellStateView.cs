namespace XDrishti.Application.Learning;

/// <summary>Where a cell is, replayed from its changelog rows (P6-08).</summary>
/// <param name="Cell">The cell key.</param>
/// <param name="State">The state after the last row (research without rows).</param>
/// <param name="LegalNext">The states it may move to.</param>
/// <param name="Rows">How many rows were replayed.</param>
/// <param name="Problems">Rows that do not add up; empty when consistent.</param>
/// <param name="PaperDaysBeforeActive">Paper days needed before Active (doc 16).</param>
/// <param name="Note">What a reader must know: the state is a record; nothing acts on it before paper data and live exist.</param>
public sealed record CellStateView(string Cell, string State, IReadOnlyList<string> LegalNext, int Rows, IReadOnlyList<string> Problems, int PaperDaysBeforeActive, string Note);
