using XDrishti.Domain.Replay.Comparison;

namespace XDrishti.Application.Replay.Comparison;

/// <summary>The two runs of a pair and their comparison.</summary>
internal sealed record ReplayPairRun(ReplayRunResult Frozen, ReplayRunResult Learning, ReplayPairResult Comparison);
