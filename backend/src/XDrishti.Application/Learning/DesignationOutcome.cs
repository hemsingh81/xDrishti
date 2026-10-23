namespace XDrishti.Application.Learning;

/// <summary>What a designation did.</summary>
/// <param name="ModelId">The model.</param>
/// <param name="Role">Its role now (champion, challenger or retired).</param>
/// <param name="Changed">False when the model had the role already and nothing was written.</param>
public sealed record DesignationOutcome(long ModelId, string Role, bool Changed);
