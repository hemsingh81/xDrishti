using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// A valid condition made by <see cref="ValidConditionGenerator"/> together with what the generator knows about it from how it was built: the features it reads in the order they first appear in the text, the history
/// depth (the largest lag it reads plus 1), and what the frame must be given. These facts are worked out by the generator, not by the library.
/// </summary>
internal sealed record GeneratedCondition(string Source, IReadOnlyList<FeatureSpec> Features, int HistoryDepth, bool NeedsContext, bool NeedsExpiryDays, bool UsesTime);
