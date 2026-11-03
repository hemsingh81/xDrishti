namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>The header of an expression run (<c>research.expression_runs</c>): the identity, the inputs as JSON, the counts, the unpaired counts per reason as JSON, the share of paired trades that used a model price, and the labels.</summary>
public sealed record ExpressionRunRow(string RunId, string CashRunKey, string ProfileHash, string RemarkVersion, string InputsJson, int Trades, int Paired, string UnpairedJson, double? BsFallbackShare, IReadOnlyList<string> Labels);
