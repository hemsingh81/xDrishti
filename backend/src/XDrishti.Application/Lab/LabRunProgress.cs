namespace XDrishti.Application.Lab;

/// <summary>Client event <c>labRunProgress</c> (spec P5-02, D5): a run entered a stage or advanced in it. <c>done</c> and <c>total</c> count instruments in the quick test and are 0 elsewhere.</summary>
public sealed record LabRunProgress(Guid RunId, string State, string Stage, int Done, int Total, string? Message);
