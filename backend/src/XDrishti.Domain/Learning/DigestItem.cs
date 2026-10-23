namespace XDrishti.Domain.Learning;

/// <summary>One stored record as the weekly digest lists it (P7-15, ADR 0094): its id, what kind of record it is, what it is about, its stored state and one line of the stored text. Nothing in it is computed.</summary>
/// <param name="Id">The record id (a lesson or hypothesis is listed by the id of the lesson or hypothesis).</param>
/// <param name="Kind">The kind (<c>transition</c>, <c>lesson</c>, <c>hypothesis</c>, a model role ...).</param>
/// <param name="Subject">A cell key, a knob, a model; may be empty.</param>
/// <param name="State">The stored state or outcome.</param>
/// <param name="Text">One line of the stored text.</param>
public sealed record DigestItem(long Id, string Kind, string Subject, string State, string Text);
