using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// The owner's CSV files are already adjusted for splits and bonus issues at source (ADR 0017 §5). An action whose ex-date is on or before the date an instrument's loaded files reach would adjust
/// those prices a second time, so adding, confirming, editing or importing such an action needs the owner's explicit consent (<c>acknowledgeCsvAdjusted</c>); until then it is refused with
/// <c>data.csv_already_adjusted</c> (409).
/// </summary>
internal static class CsvAdjustment
{
    /// <summary>Whether an action on <paramref name="exDate"/> falls into the part of the instrument's prices the loaded files already adjusted.</summary>
    public static bool AdjustsTwice(DateOnly exDate, IReadOnlyDictionary<Guid, DateOnly> through, Guid instrumentId) =>
        through.TryGetValue(instrumentId, out var last) && exDate <= last;

    /// <summary>Refuses (409) an action that adjusts the loaded CSV prices twice unless the owner acknowledged it; null when it may go ahead.</summary>
    public static async ValueTask<Error?> GuardAsync(ICorporateActionQueries queries, Guid instrumentId, DateOnly exDate, bool acknowledged, CancellationToken cancellationToken)
    {
        if (acknowledged)
        {
            return null;
        }

        var through = await queries.ListCsvAdjustedThroughAsync([instrumentId], cancellationToken);
        return AdjustsTwice(exDate, through, instrumentId) ? DataErrors.CsvAlreadyAdjusted(1, 1, through[instrumentId]) : null;
    }

    /// <summary>The view with the date the instrument's loaded CSV files reach.</summary>
    public static async ValueTask<CorporateActionView> WithThroughAsync(this CorporateActionView view, ICorporateActionQueries queries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        var through = await queries.ListCsvAdjustedThroughAsync([view.InstrumentId], cancellationToken);
        return through.TryGetValue(view.InstrumentId, out var last) ? view with { CsvAdjustedThrough = last } : view;
    }
}
