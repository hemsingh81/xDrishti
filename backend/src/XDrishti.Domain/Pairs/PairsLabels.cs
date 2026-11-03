namespace XDrishti.Domain.Pairs;

/// <summary>The stored snake_case words of the pairs enums (the labels other slices write next to a value).</summary>
public static class PairsLabels
{
    /// <summary>The label that travels with every p-value of the test: MacKinnon (1994) is an asymptotic approximation.</summary>
    public const string PValueAsymptotic = "asymptotic";

    public static string Label(this PairsReason reason) => reason switch
    {
        PairsReason.BadInput => "bad_input",
        PairsReason.TooShort => "too_short",
        PairsReason.DegenerateX => "degenerate_x",
        PairsReason.ZeroResidualVariance => "zero_residual_variance",
        PairsReason.NotMeanReverting => "not_mean_reverting",
        PairsReason.Oscillating => "oscillating",
        PairsReason.ShortWindow => "short_window",
        PairsReason.FlatWindow => "flat_window",
        PairsReason.ZUnavailable => "z_unavailable",
        _ => "bad_input",
    };

    public static string Label(this PairPosition position) => position switch
    {
        PairPosition.Flat => "flat",
        PairPosition.LongSpread => "long_spread",
        PairPosition.ShortSpread => "short_spread",
        _ => "flat",
    };

    public static string Label(this SignalAction action) => action switch
    {
        SignalAction.Hold => "hold",
        SignalAction.EnterLongSpread => "long_spread",
        SignalAction.EnterShortSpread => "short_spread",
        SignalAction.Exit => "exit",
        SignalAction.Stop => "stop",
        _ => "hold",
    };
}
