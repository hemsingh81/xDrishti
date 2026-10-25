using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The cost basis of a set of runs from their headers (P8-12, ADR 0110; shared by the stored kinds): gross, costed_unconfirmed or net_confirmed. Gross and costed runs are never mixed.</summary>
internal static class RunCostBasis
{
    public static async ValueTask<Result<string>> ResolveAsync(IRunStore runs, IChargeSheetConfirmationSource confirmations, IReadOnlyList<CellSource> sources, CancellationToken cancellationToken)
    {
        var headers = new List<StoredRunHeader>();
        foreach (var key in sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal))
        {
            var header = await runs.FindAsync(key, cancellationToken);
            if (header is null)
            {
                return Result.Failure<string>(LearningErrors.RunNotFound(key));
            }

            headers.Add(header);
        }

        var costed = headers.Count(h => h.CostsSheetId is not null);
        if (costed == 0)
        {
            return Result.Success("gross");
        }

        if (costed != headers.Count)
        {
            return Result.Failure<string>(LearningErrors.Invalid([new FieldError("sources", "Gross and costed runs are never mixed; name runs costed the same way.")]));
        }

        var confirmation = await confirmations.FindAsync(cancellationToken);
        var confirmed = confirmation is not null && headers.All(h => string.Equals(h.CostsSheetId, confirmation.SheetId, StringComparison.Ordinal) && string.Equals(h.CostsRatesSha256, confirmation.RatesSha256, StringComparison.Ordinal));
        return Result.Success(confirmed ? "net_confirmed" : "costed_unconfirmed");
    }
}
