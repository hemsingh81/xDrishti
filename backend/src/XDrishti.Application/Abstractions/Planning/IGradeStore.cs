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

/// <summary>The grades (<c>trading.grades</c>), insert-only, one per ticket. Nothing here places an order.</summary>
public interface IGradeStore
{
    /// <summary>Stores the grades in one transaction; a ticket that already has a grade keeps it and is skipped. Returns how many rows were written.</summary>
    ValueTask<int> InsertAsync(IReadOnlyList<NewGrade> grades, CancellationToken cancellationToken);

    /// <summary>The grades of the plans, by ticket id.</summary>
    ValueTask<IReadOnlyDictionary<long, StoredGrade>> ForPlansAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken);
}
