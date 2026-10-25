using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Planning;

/// <summary>The charge choice of a replay and the sheet behind it (null: gross).</summary>
/// <param name="Choice">What the simulator is asked for.</param>
/// <param name="Sheet">The stored sheet, or null when gross.</param>
internal sealed record ResolvedCosts(CostChoice Choice, CostSheet? Sheet);

/// <summary>Resolves <c>Planning:Grading:CostSheetId</c> and <c>Capital</c> for grading and for the paper capture alike, so the two cannot differ (spec P10-01). Gross when unset; a configured sheet that is not stored is a failure, never a silent gross.</summary>
internal static class ReplayCosts
{
    public static async Task<Result<ResolvedCosts>> ResolveAsync(IGradeSettings settings, ICostSheetStore costSheets, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.CostSheetId))
        {
            return Result.Success(new ResolvedCosts(CostChoice.Gross, null));
        }

        var sheet = await costSheets.FindAsync(settings.CostSheetId, cancellationToken);
        return sheet.IsFailure
            ? Result.Failure<ResolvedCosts>(sheet.Error)
            : Result.Success(new ResolvedCosts(CostChoice.Sheet(sheet.Value.Id, settings.Capital), sheet.Value));
    }
}
