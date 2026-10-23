namespace XDrishti.Domain.Planning;

/// <summary>Kleene's three-valued logic for the parts of a setup (P7-04). Pure. False wins an AND, True wins an OR, Unknown is never turned into True.</summary>
public static class Kleene
{
    /// <summary>AND of two values.</summary>
    public static SetupTruth And(SetupTruth left, SetupTruth right) =>
        left == SetupTruth.False || right == SetupTruth.False ? SetupTruth.False
        : left == SetupTruth.Unknown || right == SetupTruth.Unknown ? SetupTruth.Unknown
        : SetupTruth.True;

    /// <summary>OR of two values.</summary>
    public static SetupTruth Or(SetupTruth left, SetupTruth right) =>
        left == SetupTruth.True || right == SetupTruth.True ? SetupTruth.True
        : left == SetupTruth.Unknown || right == SetupTruth.Unknown ? SetupTruth.Unknown
        : SetupTruth.False;

    /// <summary>NOT of a value; Unknown stays Unknown.</summary>
    public static SetupTruth Not(SetupTruth value) => value switch
    {
        SetupTruth.True => SetupTruth.False,
        SetupTruth.False => SetupTruth.True,
        _ => SetupTruth.Unknown,
    };

    /// <summary>All of the values (True for none) or any of them (False for none).</summary>
    public static SetupTruth Fold(IEnumerable<SetupTruth> values, bool all)
    {
        ArgumentNullException.ThrowIfNull(values);
        var result = all ? SetupTruth.True : SetupTruth.False;
        foreach (var value in values)
        {
            result = all ? And(result, value) : Or(result, value);
        }

        return result;
    }
}
