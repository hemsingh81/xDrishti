using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>A grade to store (P7-13, ADR 0090).</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket (a plan item of any group).</param>
/// <param name="ForDate">The planned session the ticket was walked through.</param>
/// <param name="Grade">The grade.</param>
/// <param name="RunKey">The key of the simulation run it came from; null when the ticket was not replayed.</param>
/// <param name="GradedAt">When.</param>
public sealed record NewGrade(long PlanId, long ItemId, DateOnly ForDate, TicketGrade Grade, string? RunKey, DateTimeOffset GradedAt);

/// <summary>A stored grade.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Grade">What was stored.</param>
public sealed record StoredGrade(long Id, NewGrade Grade);

/// <summary>A page of graded tickets for the report.</summary>
/// <param name="Items">The tickets.</param>
/// <param name="Truncated">More tickets matched than the limit.</param>
public sealed record GradedPage(IReadOnlyList<GradedItem> Items, bool Truncated);

/// <summary>The grades (<c>trading.grades</c>), insert-only, one per ticket. Nothing here places an order.</summary>
public interface IGradeStore : IGradeReader
{
    /// <summary>Stores the grades in one transaction; a ticket that already has a grade keeps it and is skipped. Returns how many rows were written.</summary>
    ValueTask<int> InsertAsync(IReadOnlyList<NewGrade> grades, CancellationToken cancellationToken);

    /// <summary>The grades of the plans, by ticket id.</summary>
    ValueTask<IReadOnlyDictionary<long, StoredGrade>> ForPlansAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken);
}

/// <summary>The read of graded tickets alone (spec P10-04): a consumer that holds this cannot write a grade.</summary>
public interface IGradeReader
{
    /// <summary>
    /// The graded tickets of the planned sessions in <c>[from, to]</c> (open ends allowed), newest session first, in one query: the newest plan version of each date only, with each ticket's newest state. At most <paramref name="limit"/>
    /// tickets; <c>Truncated</c> says more matched.
    /// </summary>
    ValueTask<GradedPage> ReportItemsAsync(DateOnly? from, DateOnly? to, int limit, CancellationToken cancellationToken);
}
