namespace XDrishti.SimExport.Verification;

/// <summary>
/// What the check mode found (spec P3-09, V8). <see cref="Problems"/> is non-empty when the input is not a usable log (not CSV, a key that is not in the pack, a row twice, a missing column): then nothing else is meaningful and the host exits 2.
/// Otherwise <see cref="Lines"/> are the lines to print: one block per row, then, never before, the lines of the pack. <see cref="Complete"/> is true when every row is signed and explained, the field checks have nothing missing, nothing is
/// unexplained and at least <see cref="CheckLogReader.MinExternal"/> trade rows are against an external chart; the reasons it is not are in <see cref="Reasons"/>. It never states that P3 passes: that is the owner's to say.
/// </summary>
public sealed record CheckReport(
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Lines,
    int Rows,
    int SignedRows,
    int External,
    int Unexplained,
    bool Complete,
    IReadOnlyList<string> Reasons);
