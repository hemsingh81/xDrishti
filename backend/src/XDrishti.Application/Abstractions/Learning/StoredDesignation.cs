using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>One appended designation of a model (P6-07): the history is never changed.</summary>
/// <param name="Id">The row id; a larger id is later.</param>
/// <param name="ModelId">The model.</param>
/// <param name="Role">The role it was given.</param>
/// <param name="Reason">Why.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="CreatedBy">Who.</param>
public sealed record StoredDesignation(long Id, long ModelId, ModelRole Role, string Reason, DateTimeOffset CreatedAt, string CreatedBy);
