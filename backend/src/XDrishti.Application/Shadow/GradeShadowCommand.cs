using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Grades the shadow record after the close (spec CF-08 part B, ADR 0212): each candidate that counts for edge and has no grade is walked through the SAME replay as grading and paper capture (<c>TicketReplayer</c>) and its grade is inserted once. A pending replay is
/// not stored and is tried again by the next run; a stored grade is never rewritten. With the switch OFF it reads and writes nothing. It places no order, calls no broker and moves no ticket.
/// </summary>
public sealed record GradeShadowCommand : ICommand;

/// <summary>What a shadow grade run did. Counts only.</summary>
/// <param name="Enabled">Whether the switch was ON.</param>
/// <param name="Seen">Candidates without a grade that were looked at.</param>
/// <param name="Graded">Stored with an R.</param>
/// <param name="NotTriggered">Stored as not triggered.</param>
/// <param name="NotGradable">Stored as not gradable (a final fact).</param>
/// <param name="Pending">Not stored: the replay cannot say yet; the next run tries again.</param>
/// <param name="Failed">Candidates whose replay failed unexpectedly; the next run tries again.</param>
public sealed record ShadowGradeOutcome(bool Enabled, int Seen, int Graded, int NotTriggered, int NotGradable, int Pending, int Failed)
{
    /// <summary>One line for the Services screen.</summary>
    public string Summary => !Enabled
        ? "Switched off (paper.shadow.enabled): nothing graded."
        : string.Create(CultureInfo.InvariantCulture, $"{Seen} candidate(s) looked at: {Graded} graded, {NotTriggered} not triggered, {NotGradable} not gradable, {Pending} pending (tried again next run), {Failed} failed.");
}
