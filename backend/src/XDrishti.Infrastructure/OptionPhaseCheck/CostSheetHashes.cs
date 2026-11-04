using XDrishti.Application.Abstractions.OptionPhaseCheck;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Infrastructure.OptionPhaseCheck;

/// <summary>The read side of the charge-sheet store alone (P14-17): the rates hash of a stored sheet. It calls <c>FindAsync</c> and nothing else.</summary>
internal sealed class CostSheetHashes(ICostSheetStore sheets) : ICostSheetHashes
{
    public async ValueTask<string?> RatesHashAsync(string sheetId, CancellationToken cancellationToken)
    {
        var found = await sheets.FindAsync(sheetId, cancellationToken);
        return found.IsSuccess ? found.Value.RatesSha256 : null;
    }
}
