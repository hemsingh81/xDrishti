namespace XDrishti.Domain.Paper;

/// <summary>One fact of the configuration: a flat key and its value as text (spec P10-05). The value never holds a secret.</summary>
/// <param name="Key">The key, for example <c>autonomy.max_auto_per_day</c> or <c>cells.eligible/&lt;cell&gt;</c>.</param>
/// <param name="Value">The value in invariant text.</param>
public sealed record ConfigComponent(string Key, string Value);
