using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>The plans, newest first (P7-03).</summary>
/// <param name="Date">Only this session date, or null.</param>
/// <param name="AccountId">Only plans with an allocation to this account, or null.</param>
/// <param name="Limit">1 to 200.</param>
public sealed record ListPlansQuery(DateOnly? Date, Guid? AccountId, int Limit) : IQuery<Result<IReadOnlyList<PlanView>>>;

/// <summary>One plan with its tickets and trail (P7-03).</summary>
/// <param name="Id">The plan id.</param>
public sealed record GetPlanQuery(long Id) : IQuery<Result<PlanView>>;
