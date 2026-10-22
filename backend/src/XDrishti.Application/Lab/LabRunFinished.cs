namespace XDrishti.Application.Lab;

/// <summary>Client event <c>labRunFinished</c> (spec P5-02, D5): the last event of a run, with its final state.</summary>
public sealed record LabRunFinished(Guid RunId, string State);
