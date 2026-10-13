namespace XDrishti.Application.Portfolio.Returns;

/// <summary>Whether the benchmark index can be compared with a series (never taken as zero).</summary>
public enum BenchmarkAvailability
{
    /// <summary>The index has a close on or before the first cash flow.</summary>
    Available,

    /// <summary>The index has no stored day bars at all.</summary>
    NoBars,

    /// <summary>The index's bars begin after the first cash flow.</summary>
    StartsAfterFirstFlow,

    /// <summary>The index's bars end before the first cash flow (nothing to compare with).</summary>
    EndsBeforeFirstFlow,

    /// <summary>The series has no cash flows, so there is nothing to compare.</summary>
    NoFlows,
}
