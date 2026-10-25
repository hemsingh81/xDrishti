using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Plan versus actual over the plans of a date range (P8-04). Read only.</summary>
/// <param name="From">First planned session; null = no lower bound.</param>
/// <param name="To">Last planned session; null = no upper bound.</param>
public sealed record GetPlanVsActualQuery(DateOnly? From, DateOnly? To) : IQuery<Result<PlanVsActualView>>;
