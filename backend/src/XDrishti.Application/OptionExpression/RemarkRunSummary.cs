namespace XDrishti.Application.OptionExpression;

/// <summary>What a re-mark did: the run id, whether it was written (false: the same inputs were stored already), the counts, the unpaired counts per reason, the model-price share and the lines the verb prints.</summary>
public sealed record RemarkRunSummary(string RunId, bool Stored, int Trades, int Paired, IReadOnlyDictionary<string, int> Unpaired, double? BsFallbackShare, IReadOnlyList<string> Lines);
