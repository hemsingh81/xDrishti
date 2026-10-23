namespace XDrishti.Application.Learning;

/// <summary>One designation of a model.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Role">challenger, champion or retired.</param>
/// <param name="Reason">Why.</param>
/// <param name="CreatedAt">When.</param>
/// <param name="CreatedBy">Who.</param>
public sealed record DesignationView(long Id, string Role, string Reason, DateTimeOffset CreatedAt, string CreatedBy);
