namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The result of a registration.</summary>
/// <param name="Id">The registry id of the model (the existing one when it was registered already).</param>
/// <param name="Created">False when the same registration was there already and nothing was written.</param>
public sealed record RegisteredModel(long Id, bool Created);
