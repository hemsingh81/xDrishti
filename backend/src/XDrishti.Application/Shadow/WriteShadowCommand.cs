using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;

namespace XDrishti.Application.Shadow;

/// <summary>
/// Writes the shadow record of a nightly plan before its session (spec CF-08, ADR 0211): every candidate of the stored plan becomes a row, once, and the session gets a chained digest. It places no order, calls no broker, moves no ticket and changes nothing
/// the plan reads. A session that has opened is never written: it gets a gap row and is never back-filled. With the switch OFF it writes nothing.
/// </summary>
/// <param name="Session">One session, or null for every session of the stored plans that is not written yet.</param>
/// <param name="Manual">Whether a person started it.</param>
public sealed record WriteShadowCommand(DateOnly? Session, bool Manual) : ICommand;

/// <summary>What a shadow write did. Counts only.</summary>
/// <param name="Enabled">Whether the switch was ON.</param>
/// <param name="Sessions">Sessions written with their rows.</param>
/// <param name="Rows">Rows written.</param>
/// <param name="EdgeRows">Of those, rows that count for edge.</param>
/// <param name="Gaps">Gap rows written.</param>
/// <param name="DroppedSameKey">Candidates not stored because an earlier candidate of the session has the same trade key.</param>
public sealed record ShadowWriteOutcome(bool Enabled, int Sessions, int Rows, int EdgeRows, int Gaps, int DroppedSameKey)
{
    /// <summary>One line for the Services screen.</summary>
    public string Summary => !Enabled
        ? "Switched off (paper.shadow.enabled): nothing written."
        : string.Create(CultureInfo.InvariantCulture, $"{Sessions} session(s) written with {Rows} row(s), {EdgeRows} counting for edge; {Gaps} gap(s) recorded; {DroppedSameKey} candidate(s) with an earlier same trade key left out.");
}
