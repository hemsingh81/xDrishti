namespace XDrishti.Application.Portfolio.Returns;

/// <summary>Whether a benchmark XIRR can be shown. Wire names: <c>ok</c>, <c>unavailable</c> (the index has no usable history), <c>insufficient</c> (the same as for XIRR: under 30 days or one-sided flows), <c>n_m</c> (beyond ±1,000 %).</summary>
public enum BenchmarkState
{
    Ok,
    Unavailable,
    Insufficient,
    NotMeaningful,
}

internal static class BenchmarkStateCodes
{
    public static string Name(BenchmarkState state) => state switch
    {
        BenchmarkState.Ok => "ok",
        BenchmarkState.Unavailable => "unavailable",
        BenchmarkState.Insufficient => "insufficient",
        BenchmarkState.NotMeaningful => "n_m",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
