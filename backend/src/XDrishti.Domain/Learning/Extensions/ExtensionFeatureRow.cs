namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One stored row of an extension (P13-05): the values of one trade for one extension version, in the order of the group's names (NaN = unknown, never filled).</summary>
/// <param name="RunKey">The simulation run the trade belongs to (64 lower-case hex).</param>
/// <param name="TradeKey">The trade's key.</param>
/// <param name="Extension">The extension id.</param>
/// <param name="Version">The extension version.</param>
/// <param name="DecisionAt">The decision instant the values were computed for.</param>
/// <param name="Values">The values.</param>
public sealed record ExtensionFeatureRow(string RunKey, string TradeKey, string Extension, string Version, DateTimeOffset DecisionAt, IReadOnlyList<double> Values);
