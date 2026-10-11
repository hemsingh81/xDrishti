namespace XDrishti.Application.Portfolio.Returns;

/// <summary>Whether an XIRR can be shown. Wire names: <c>ok</c>, <c>insufficient</c>, <c>n_m</c>, <c>no_data</c>, <c>no_price</c>.</summary>
public enum XirrState
{
    /// <summary>A rate was found and is meaningful.</summary>
    Ok,

    /// <summary>No rate can be given: no outflow or no inflow, a period under 30 days, or the solver did not converge. Shown as "—".</summary>
    Insufficient,

    /// <summary>The rate is beyond ±1,000 % a year. Shown as "n/m".</summary>
    NotMeaningful,

    /// <summary>There are no cash flows at all (nothing invested).</summary>
    NoData,

    /// <summary>The holding has no usable price yet (a last price of 0): its value, profit and XIRR are unknown, not a total loss. Shown as "—" with a hint.</summary>
    NoPrice,
}

internal static class XirrStateCodes
{
    public static string Name(XirrState state) => state switch
    {
        XirrState.Ok => "ok",
        XirrState.Insufficient => "insufficient",
        XirrState.NotMeaningful => "n_m",
        XirrState.NoData => "no_data",
        XirrState.NoPrice => "no_price",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
