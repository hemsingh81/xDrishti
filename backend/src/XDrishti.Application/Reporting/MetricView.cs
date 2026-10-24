namespace XDrishti.Application.Reporting;

/// <summary>One reported figure on the wire (P8-00 D3): a state and, only for <c>value</c> and <c>provisional</c>, a number. For every other state (<c>insufficient</c>, <c>unavailable</c>, <c>not_evaluable</c>, <c>empty</c>) the number is absent and the reason says why; a screen draws a word, never a digit.</summary>
/// <param name="State">One of value, provisional, insufficient, unavailable, not_evaluable, empty.</param>
/// <param name="Number">The number; null unless the state is value or provisional.</param>
/// <param name="Reason">Why the figure is not a plain value; null for a value.</param>
public sealed record MetricView(string State, double? Number, string? Reason);
