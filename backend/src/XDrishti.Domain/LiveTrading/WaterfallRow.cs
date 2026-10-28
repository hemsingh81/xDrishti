namespace XDrishti.Domain.LiveTrading;

/// <summary>One line of the waterfall of a round trip. <see cref="Op"/> is <c>start</c> (the plan), <c>minus</c> (a loss against the plan, as a positive amount) or <c>total</c> (a subtotal that must equal the line before it minus the minus lines since the last total). <see cref="R"/> is null when the plan has no stop (the rupees are then the result).</summary>
public sealed record WaterfallRow(string Label, string Op, double? R, decimal Rupees);

/// <summary>The waterfall of one round trip: the rows, whether it is gross or net, and whether the rows add up exactly.</summary>
public sealed record WaterfallResult(IReadOnlyList<WaterfallRow> Rows, string Basis, bool Balanced, IReadOnlyList<string> Notes);
