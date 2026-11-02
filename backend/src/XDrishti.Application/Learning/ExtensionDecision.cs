namespace XDrishti.Application.Learning;

/// <summary>One trade of a stored run as an extension extraction sees it: the trade's key and its decision instant. Nothing of its outcome is given, so no outcome can reach a feature.</summary>
internal readonly record struct ExtensionDecision(string TradeKey, DateTimeOffset At);
