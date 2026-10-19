namespace XDrishti.Domain.Research;

/// <summary>A parameter of a built-in strategy as the catalog lists it (spec P3-04b2, D3): its name, the default a run uses when it is not given, and a one-line note.</summary>
public sealed record BuiltInParameterInfo(string Name, double Default, string Note);
