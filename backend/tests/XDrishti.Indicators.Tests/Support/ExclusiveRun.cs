namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// The allocation and benchmark tests run alone: the thread allocation counter they read is exact, but a collection or tiered-JIT activity triggered by tests running in parallel can land in their measurement window.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ExclusiveRun
{
    public const string Name = "Allocation and timing (not parallel)";
}
