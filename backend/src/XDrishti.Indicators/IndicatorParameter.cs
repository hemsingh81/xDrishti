namespace XDrishti.Indicators;

/// <summary>One parameter of an indicator for the DSL validator: its name, type, inclusive range and default.</summary>
public sealed record IndicatorParameter(string Name, ParameterKind Kind, double Min, double Max, double Default);
