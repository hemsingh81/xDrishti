namespace XDrishti.Domain.Learning.Extensions;

/// <summary>The event-risk switch in force and the row that put it there (P15-12). <see cref="Readable"/> false means the decision history could not be read; the filter then counts as not acting (ADR 0182).</summary>
/// <param name="Readable">Whether the history could be read.</param>
/// <param name="On">The effective switch.</param>
/// <param name="Days">N of the effective switch.</param>
/// <param name="DecisionId">The id of the row in force, or null.</param>
/// <param name="DecidedBy">owner or auto, or null.</param>
public sealed record EventsFilterFact(bool Readable, bool On, int Days, long? DecisionId, string? DecidedBy);
