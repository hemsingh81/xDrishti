namespace XDrishti.Application.Lab;

/// <summary>A clone (spec P5-01): the text with the new id, the hash of the version it came from (post it as <c>parentHash</c> to record the lineage) and the source.</summary>
public sealed record ClonedStrategy(string Id, string Text, string ParentHash, string SourceId, int SourceVersion);
