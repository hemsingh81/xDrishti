namespace XDrishti.Domain.Options.Quality;

/// <summary>The words stored in <c>options.quality_results</c>: the sources, the check codes and the closed list of reasons (spec P14-04 criterion 1; <see cref="NoStrikeStep"/> is the one addition, see ADR 0178).</summary>
public static class QualityCodes
{
    public const string Chain = "chain";
    public const string Rolling = "rolling";

    public const string Coverage = "coverage";
    public const string Parity = "parity";
    public const string Greeks = "greeks";
    public const string OiVolume = "oi_volume";
    public const string Stitching = "stitching";

    public const string NotRecorded = "not_recorded";
    public const string Gap = "gap";
    public const string NoTwoSidedQuote = "no_two_sided_quote";
    public const string NoUnderlyingBar = "no_underlying_bar";
    public const string TooFewPoints = "too_few_points";
    public const string OutOfEra = "out_of_era";

    /// <summary>The strike step of the underlying is not known, so a strike jump cannot be judged.</summary>
    public const string NoStrikeStep = "no_strike_step";
}
