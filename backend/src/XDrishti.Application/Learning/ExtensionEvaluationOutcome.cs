using XDrishti.Application.Selection;

namespace XDrishti.Application.Learning;

/// <summary>What one evaluation stored or found stored, what the event-filter auto rule did after it (null when the family has no filter verdict), and a note when the hold-out was asked for but not read.</summary>
/// <param name="Family">The family evaluated.</param>
/// <param name="Records">The verdicts.</param>
/// <param name="Note">A sentence for the verb, or null.</param>
/// <param name="Auto">The one run of the auto rule after a new filter verdict, or null.</param>
public sealed record ExtensionEvaluationOutcome(string Family, IReadOnlyList<EvaluatedRecord> Records, string? Note, EventFilterAutoResult? Auto = null);
