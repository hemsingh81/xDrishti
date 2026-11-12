namespace XDrishti.Application.Abstractions.Market.Lists;

/// <summary>
/// The insert-only dated rows of the back-filled ban list (DATA-01). A day is a coverage row plus one member row per banned instrument; an empty ban list is a covered day with no member rows.
/// There is no update and no delete: a day already stored is left as it is.
/// </summary>
public interface IListDayStore
{
    /// <summary>The dates in the range that already have a coverage row for the list.</summary>
    ValueTask<IReadOnlySet<DateOnly>> CoveredAsync(Guid basketId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Stores the day in one transaction; false (nothing written) when the day is stored already.</summary>
    ValueTask<bool> InsertAsync(Guid basketId, DateOnly date, IReadOnlyCollection<Guid> instrumentIds, string source, CancellationToken cancellationToken);
}
