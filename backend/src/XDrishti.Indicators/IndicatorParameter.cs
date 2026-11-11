namespace XDrishti.Indicators;

/// <summary>
/// One parameter of an indicator for the DSL validator: its name, type, inclusive range and default. <paramref name="Allowed"/>, when given, is the closed set of values that are valid (a pre-registered choice such as the
/// momentum lookbacks of BK-09): any other value is refused with a message that names the set.
/// </summary>
public sealed record IndicatorParameter(string Name, ParameterKind Kind, double Min, double Max, double Default, IReadOnlyList<double>? Allowed = null);
