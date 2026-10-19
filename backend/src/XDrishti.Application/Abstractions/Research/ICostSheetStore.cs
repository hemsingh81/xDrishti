using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// Where the owner's charge sheets are kept (spec P3-03f, F3): one row per sheet id of the settings table, the canonical JSON as the value. A sheet is configuration written by the one-shot verb <c>cost-sheet</c> and read by a run.
/// </summary>
public interface ICostSheetStore
{
    /// <summary>The stored sheet: <c>costs.sheet_not_found</c> (not found) for an id that was never stored and for any <c>test-</c> id, or <c>costs.sheet_invalid</c> when the stored text no longer parses.</summary>
    ValueTask<Result<CostSheet>> FindAsync(string sheetId, CancellationToken cancellationToken);

    /// <summary>Collects the change (the caller saves the unit of work). A <c>test-</c> id is refused with an argument exception: it is a caller error, the verb refuses it earlier.</summary>
    ValueTask SaveAsync(CostSheet sheet, string actor, DateTimeOffset now, CancellationToken cancellationToken);
}
