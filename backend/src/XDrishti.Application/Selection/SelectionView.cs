namespace XDrishti.Application.Selection;

/// <summary>The cell-level selection as the verb prints it.</summary>
/// <param name="Eligible">Cells that passed every required filter.</param>
/// <param name="Rejected">Cells with a failed filter.</param>
/// <param name="Held">Cells with a required filter unavailable.</param>
/// <param name="Text">The printable report, limits included.</param>
public sealed record SelectionView(int Eligible, int Rejected, int Held, string Text);
