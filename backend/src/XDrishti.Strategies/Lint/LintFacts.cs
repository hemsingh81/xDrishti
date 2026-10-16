using XDrishti.Indicators;

namespace XDrishti.Strategies.Lint;

/// <summary>What the lint learned about a condition besides its problems: the features it reads, the history it needs and what the frame must be given.</summary>
/// <param name="Features">The distinct canonical specs the condition reads, in the order they first appear in the text.</param>
/// <param name="HistoryDepth">The largest lag the condition reads plus 1, at least 1 (valid only when the lint found no error).</param>
/// <param name="NeedsContext">Whether a feature of the daily market context is read.</param>
/// <param name="NeedsExpiryDays">Whether <c>is_expiry_day</c> is read.</param>
internal sealed record LintFacts(IReadOnlyList<FeatureSpec> Features, int HistoryDepth, bool NeedsContext, bool NeedsExpiryDays);
